using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Repositories;
using FiscalizeXero.Api.Services.CurrentUser;
using FiscalizeXero.Api.Services.DeviceConfig;
using FiscalizeXero.Api.Services.DeviceSession;
using FiscalizeXero.Api.Services.FiscalDocuments;
using FiscalizeXero.Api.Services.HsCodes;
using FiscalizeXero.Api.Services.XeroAuth;
using FiscalizeXero.Fiscalize.Exceptions;
using FiscalizeXero.Fiscalize.Services.CreditNotes;
using FiscalizeXero.Fiscalize.Services.DeviceApiKeys;
using FiscalizeXero.Fiscalize.Services.Invoices;
using FiscalizeXero.Shared.Models.Auths;
using FiscalizeXero.Shared.Models.Fiscalize;
using FiscalizeXero.Xero.Exceptions;
using FiscalizeXero.Xero.Models;
using FiscalizeXero.Xero.Services;
using System.Collections.Concurrent;
using System.Net;
using XeroAuthModel = FiscalizeXero.Xero.XeroAuth;

namespace FiscalizeXero.Api.Services.Fiscalization
{
    public class FiscalizationService : IFiscalizationService
    {
        /// <summary>ZIMRA's limit for the document number of a credit note.</summary>
        public const int MaxCreditNoteNumberLength = 20;

        private readonly ICurrentUserService _currentUserService;
        private readonly IXeroAuthBuilder _xeroAuthBuilder;
        private readonly IXeroTaxRateCache _taxRates;
        private readonly IDeviceSessionService _deviceSessionService;
        private readonly IContactVatRepository _contactVatRepository;
        private readonly IItemHsCodeRepository _itemHsCodeRepository;
        private readonly IDeviceConfigService _deviceConfigService;
        private readonly IAttachmentQueue _attachments;
        private readonly IFiscalRecordRepository _fiscalRecordRepository;
        private readonly FiscalizeSession.IFiscalizeSessionService _fiscalizeSession;
        private readonly IFiscalFailureService _failures;
        private readonly ILogger<FiscalizationService> _logger;

        public FiscalizationService(
            ICurrentUserService currentUserService,
            IXeroAuthBuilder xeroAuthBuilder,
            IXeroTaxRateCache taxRates,
            IDeviceSessionService deviceSessionService,
            IContactVatRepository contactVatRepository,
            IItemHsCodeRepository itemHsCodeRepository,
            IDeviceConfigService deviceConfigService,
            IAttachmentQueue attachments,
            IFiscalRecordRepository fiscalRecordRepository,
            FiscalizeSession.IFiscalizeSessionService fiscalizeSession,
            IFiscalFailureService failures,
            ILogger<FiscalizationService> logger)
        {
            _failures = failures;
            _fiscalizeSession = fiscalizeSession;
            _currentUserService = currentUserService;
            _xeroAuthBuilder = xeroAuthBuilder;
            _taxRates = taxRates;
            _deviceSessionService = deviceSessionService;
            _contactVatRepository = contactVatRepository;
            _itemHsCodeRepository = itemHsCodeRepository;
            _deviceConfigService = deviceConfigService;
            _attachments = attachments;
            _fiscalRecordRepository = fiscalRecordRepository;
            _logger = logger;
        }

        // ===== Controller convenience =====

        public async Task<FiscalizeResultModel> FiscalizeInvoiceAsync(Guid invoiceId)
        {
            var (config, company) = await ResolveCurrentUserConnectionAsync();
            // A person clicking Fiscalize always submits; the platform decides what to do with a document it has seen.
            return await TrackAsync(config.AppUserCompanyId, FiscalDocumentType.Invoice, invoiceId, FiscalFailureSource.Manual, async () =>
                await FiscalizeInvoiceCoreAsync(await BuildContextAsync(config, company), invoiceId, allowResubmit: true));
        }

        public async Task<FiscalizeResultModel> FiscalizeCreditNoteAsync(Guid creditNoteId, string? baseDocNo = null)
        {
            var (config, company) = await ResolveCurrentUserConnectionAsync();
            return await TrackAsync(config.AppUserCompanyId, FiscalDocumentType.CreditNote, creditNoteId, FiscalFailureSource.Manual, async () =>
                await FiscalizeCreditNoteCoreAsync(await BuildContextAsync(config, company), creditNoteId, baseDocNo, allowResubmit: true));
        }

        /// <summary>
        /// The invoice a credit note reverses, as far as Xero can tell (the credit note's allocations; none until the
        /// user allocates the credit), plus this company's fiscalised invoices for the user to choose from otherwise.
        /// </summary>
        public async Task<CreditNoteBaseDocumentModel> GetCreditNoteBaseDocumentAsync(Guid creditNoteId)
        {
            var ctx = await ResolveCurrentUserContextAsync();
            var companyId = ctx.Config.AppUserCompanyId;
            var creditNote = await GetCreditNoteAsync(ctx, creditNoteId);

            var model = new CreditNoteBaseDocumentModel();
            var allocated = CreditNoteOriginalInvoice.Candidates(creditNote);
            foreach (var candidate in allocated)
            {
                var record = await FindFiscalisedInvoiceAsync(companyId, candidate);
                model.Allocations.Add(new CreditNoteAllocationModel
                {
                    InvoiceId = FiscalDocumentType.Key(candidate.InvoiceId),
                    InvoiceNumber = record?.DocumentNumber ?? candidate.InvoiceNumber,
                    Amount = candidate.Allocated,
                    IsFiscalized = record != null
                });
            }

            var resolved = model.Allocations.FirstOrDefault();
            if (resolved != null)
            {
                model.ResolvedDocNo = resolved.InvoiceNumber ?? resolved.InvoiceId;
                model.ResolvedIsFiscalized = resolved.IsFiscalized;
                if (!resolved.IsFiscalized)
                    model.Problem = $"Invoice {model.ResolvedDocNo} has not been fiscalized here. ZIMRA needs the original receipt, so fiscalize that invoice first or choose another.";
            }
            else
            {
                model.Problem = $"Credit note {XeroDocumentTypes.NumberOf(creditNote)} has not been allocated to an invoice in Xero. " +
                                "Allocate the credit in Xero, or choose the invoice it reverses below.";
            }

            model.Candidates = (await _fiscalRecordRepository.GetRecentAsync(companyId, FiscalDocumentType.Invoice, 50))
                .Select(r => new BaseInvoiceCandidate { DocumentId = r.DocumentId, DocumentNumber = r.DocumentNumber, DocumentType = r.DocumentType, ReceiptNumber = r.ReceiptNumber, FiscalizedAt = r.FiscalizedAt })
                .ToList();
            return model;
        }

        // ===== Background worker (explicit context) =====

        public Task<FiscalizeResultModel> FiscalizeForConfigAsync(ClientConfig config, AppUserCompany appUserCompany, string documentType, Guid documentId)
            => TrackAsync(config.AppUserCompanyId, documentType, documentId, FiscalFailureSource.Automatic, async () =>
            {
                var ctx = await BuildContextAsync(config, appUserCompany);
                return documentType == FiscalDocumentType.CreditNote
                    ? await FiscalizeCreditNoteCoreAsync(ctx, documentId)
                    : await FiscalizeInvoiceCoreAsync(ctx, documentId);
            });

        /// <summary>
        /// Runs a first-time fiscalisation and keeps the document's failure row in step: recorded (with what went
        /// wrong) when it throws, removed when it succeeds. A document that is not a sale at all (a draft, a purchase
        /// bill, a voided invoice) is not a failure: nothing is recorded for it and any earlier failure is removed.
        /// The original exception always reaches the caller unchanged.
        /// </summary>
        private async Task<FiscalizeResultModel> TrackAsync(int companyId, string documentType, Guid documentId, string source, Func<Task<FiscalizeResultModel>> fiscalize)
        {
            var key = FiscalDocumentType.Key(documentId);
            FiscalizeResultModel result;
            try
            {
                result = await fiscalize();
            }
            catch (DocumentNotFiscalisableException)
            {
                await _failures.ClearAsync(companyId, documentType, key);
                throw;
            }
            catch (Exception ex)
            {
                await _failures.RecordAsync(companyId, documentType, key, ex, source);
                throw;
            }
            await _failures.ClearAsync(companyId, documentType, key);
            // A person fiscalising a fiscally cancelled document hands it back to the automatic triggers.
            if (source == FiscalFailureSource.Manual)
                await _failures.UnsuppressAsync(companyId, key);
            // Credit notes held back because this invoice wasn't fiscalised yet can go now.
            if (documentType == FiscalDocumentType.Invoice && result.ReceiptNumber.HasValue)
                await _failures.RetrySoonAsync(companyId, FiscalFailureCategory.OriginalSale);
            return result;
        }

        // ===== Core orchestration (context already resolved) =====

        // One lock per (company, document). A webhook, a sync run and a manual click can all arrive for the same
        // document within seconds; whichever gets here second finds the record and returns it instead of submitting
        // the document to ZIMRA a second time. Process-wide is enough: the API runs as a single instance.
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> DocumentLocks = new();

        private Task<FiscalizeResultModel> FiscalizeInvoiceCoreAsync(FiscalizationContext ctx, Guid invoiceId, bool allowResubmit = false)
            => FiscalizeOnceAsync(ctx, invoiceId, FiscalDocumentType.Invoice, () => FiscalizeInvoiceUnguardedAsync(ctx, invoiceId), allowResubmit);

        private Task<FiscalizeResultModel> FiscalizeCreditNoteCoreAsync(FiscalizationContext ctx, Guid creditNoteId, string? baseDocNo = null, bool allowResubmit = false)
            => FiscalizeOnceAsync(ctx, creditNoteId, FiscalDocumentType.CreditNote, () => FiscalizeCreditNoteUnguardedAsync(ctx, creditNoteId, baseDocNo), allowResubmit);

        /// <summary>
        /// Runs a fiscalisation under the document's lock. <paramref name="allowResubmit"/> is true for explicit user
        /// actions: the document is sent to the platform even when a record exists, and the record is updated with
        /// the platform's answer. Automatic triggers (webhooks, the sync) leave it false so an already-fiscalised
        /// document is never resubmitted on their behalf.
        /// </summary>
        private async Task<FiscalizeResultModel> FiscalizeOnceAsync(FiscalizationContext ctx, Guid documentId, string documentType, Func<Task<FiscalizeResultModel>> fiscalize, bool allowResubmit = false)
        {
            var companyId = ctx.Config.AppUserCompanyId;
            var key = FiscalDocumentType.Key(documentId);
            var gate = DocumentLocks.GetOrAdd($"{companyId}:{documentType}:{key}", _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                var existing = await _fiscalRecordRepository.GetAsync(companyId, key);
                if (!allowResubmit && existing != null && existing.DocumentType == documentType && existing.ReceiptNumber.HasValue)
                {
                    _logger.LogInformation("{Type} {DocId} for company {CompanyId} is already fiscalised (receipt {Receipt}); returning the existing record",
                        documentType, key, companyId, existing.ReceiptNumber);
                    return ToResult(existing, null);
                }
                return await fiscalize();
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<T> WithDocumentLockAsync<T>(FiscalizationContext ctx, Guid documentId, string documentType, Func<Task<T>> action)
        {
            var gate = DocumentLocks.GetOrAdd($"{ctx.Config.AppUserCompanyId}:{documentType}:{FiscalDocumentType.Key(documentId)}", _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try { return await action(); }
            finally { gate.Release(); }
        }

        private async Task<FiscalizeResultModel> FiscalizeInvoiceUnguardedAsync(FiscalizationContext ctx, Guid invoiceId)
        {
            var (invoice, request) = await BuildInvoiceRequestAsync(ctx, invoiceId);
            var fiscalData = await CallAsDeviceAsync(ctx, auth => FiscalizeInvoiceService.FiscalizeAsync(auth, request))
                ?? throw new FiscalizeXero.Api.Exceptions.UserFacingException("Fiscalize platform returned no fiscal data");
            return await CompleteAsync(ctx, FiscalDocumentType.Invoice, invoice, request.DocNo, fiscalData, fiscalData.Exists);
        }

        private async Task<FiscalizeResultModel> FiscalizeCreditNoteUnguardedAsync(FiscalizationContext ctx, Guid creditNoteId, string? baseDocNo = null)
        {
            var (creditNote, request) = await BuildCreditNoteRequestAsync(ctx, creditNoteId, baseDocNo);
            var fiscalData = await CallAsDeviceAsync(ctx, auth => FiscalizeCreditNoteService.FiscalizeAsync(auth, request))
                ?? throw new FiscalizeXero.Api.Exceptions.UserFacingException("Fiscalize platform returned no fiscal data");
            return await CompleteAsync(ctx, FiscalDocumentType.CreditNote, creditNote, request.DocNo, fiscalData, fiscalData.Exists);
        }

        // ===== Reading from Xero =====

        private async Task<Invoice> GetInvoiceAsync(FiscalizationContext ctx, Guid invoiceId) =>
            await XeroDocumentTypes.GetAsync(ctx.Xero, FiscalDocumentType.Invoice, invoiceId) as Invoice
            ?? throw new DocumentNotFiscalisableException(
                $"Xero has no invoice with id {FiscalDocumentType.Key(invoiceId)} in this organisation. It may have been deleted in Xero; refresh the invoice list.");

        private async Task<CreditNote> GetCreditNoteAsync(FiscalizationContext ctx, Guid creditNoteId) =>
            await XeroDocumentTypes.GetAsync(ctx.Xero, FiscalDocumentType.CreditNote, creditNoteId) as CreditNote
            ?? throw new DocumentNotFiscalisableException(
                $"Xero has no credit note with id {FiscalDocumentType.Key(creditNoteId)} in this organisation. It may have been deleted in Xero; refresh the credit note list.");

        /// <summary>
        /// Everything the Fiscalize platform needs for an invoice: buyer, HS codes and tax per line. Refuses anything
        /// that is not an approved sales invoice.
        /// </summary>
        private async Task<(Invoice Invoice, FiscalizeInvoiceRequest Request)> BuildInvoiceRequestAsync(FiscalizationContext ctx, Guid invoiceId, bool resolveBuyer = true)
        {
            var invoice = await GetInvoiceAsync(ctx, invoiceId);
            DocumentGate.Ensure(invoice);

            // The fiscal-details update supplies its own buyer, so the contact on file isn't checked there.
            var buyer = resolveBuyer ? await ResolveBuyerAsync(ctx, invoice) : null;
            var hsCodes = await BuildHsCodeLookupAsync(ctx, invoice);
            var taxByType = await ResolveTaxRatesAsync(ctx, invoice);

            var request = XeroFiscalizeMapper.MapInvoice(invoice, ctx.DeviceId, buyer, hsCodes, taxByType);
            Finish(request, invoice, ctx.Config);
            return (invoice, request);
        }

        /// <summary>
        /// Everything the platform needs for a credit note: the credit note, its original invoice (ZIMRA credits a
        /// specific fiscal receipt, so the original must already be fiscalised here), buyer, HS codes and tax.
        /// </summary>
        private async Task<(CreditNote CreditNote, FiscalizeInvoiceRequest Request)> BuildCreditNoteRequestAsync(FiscalizationContext ctx, Guid creditNoteId, string? baseDocNo)
        {
            var creditNote = await GetCreditNoteAsync(ctx, creditNoteId);
            DocumentGate.Ensure(creditNote);
            var originalDocNo = await ResolveBaseDocNoAsync(ctx.Config.AppUserCompanyId, creditNote, baseDocNo);

            var buyer = await ResolveBuyerAsync(ctx, creditNote);
            var hsCodes = await BuildHsCodeLookupAsync(ctx, creditNote);
            var taxByType = await ResolveTaxRatesAsync(ctx, creditNote);

            var request = XeroFiscalizeMapper.MapCreditNote(creditNote, ctx.DeviceId, buyer, hsCodes, originalDocNo, taxByType);
            Finish(request, creditNote, ctx.Config);
            if (request.DocNo.Length > MaxCreditNoteNumberLength)
                throw new FiscalizeXero.Api.Exceptions.UserFacingException(
                    $"Credit note number \"{request.DocNo}\" is longer than the {MaxCreditNoteNumberLength} characters ZIMRA allows. Shorten the credit note number in Xero, then fiscalise it again.");
            return (creditNote, request);
        }

        /// <summary>What every request needs after mapping: our own marker out of the reference, and an HS code on every line.</summary>
        private static void Finish(FiscalizeInvoiceRequest request, SalesDocument document, ClientConfig config)
        {
            // The Reference may carry the fiscal marker of an earlier fiscalisation; that is ours, not the customer's.
            request.Reference = FiscalReferenceMarker.Strip(request.Reference);
            if (request.Lines.Count == 0)
                throw new FiscalizeXero.Api.Exceptions.UserFacingException(
                    $"{XeroDocumentTypes.Title(XeroDocumentTypes.TypeOf(document))} {XeroDocumentTypes.NumberOf(document)} has no lines with an amount, so there is nothing to fiscalise. Add a priced line in Xero, then fiscalise it again.");
            ItemHsCodes.ApplyDefault(request.Lines, config.EffectiveDefaultHsCode);
        }

        /// <summary>
        /// The buyer block for the document: null for a cash sale (no TIN); otherwise names, TIN/VAT, phone, email and
        /// address, checked against the company's Strict/Lenient setting by <see cref="BuyerCompliance"/>. Throws
        /// <see cref="BuyerDetailsException"/> with a readable message when the buyer can't be fiscalised.
        /// </summary>
        private async Task<FiscalizeInvoiceBuyerData?> ResolveBuyerAsync(FiscalizationContext ctx, SalesDocument document)
        {
            var contactId = document.Contact?.ContactID ?? Guid.Empty;
            if (contactId == Guid.Empty) return null;

            var saved = await _contactVatRepository.GetAsync(ctx.Config.AppUserCompanyId, FiscalDocumentType.Key(contactId));
            Contact? contact;
            try
            {
                contact = await ContactService.GetByIdAsync(ctx.Xero, contactId);
            }
            catch (XeroApiException ex)
            {
                // Don't guess: without the contact the TIN (often read from Xero) and the contact details are
                // unknown, and fiscalising anyway could report a B2B sale as a cash sale. Fail so the next try reads it.
                _logger.LogWarning(ex, "Could not read Xero contact {ContactId}; fiscalisation stops until it can be read", contactId);
                throw;
            }

            var assessment = BuyerCompliance.Assess(BuyerResolver.Resolve(contact, saved), contact, document.Contact?.Name);
            return BuyerCompliance.BuildBuyerData(assessment, ctx.Config.FiscalizationValidationMode);
        }

        /// <summary>
        /// Each line's own tax rate, from the organisation's tax rates (read at most once every few minutes). If Xero
        /// cannot be asked, null: the mapper then works each rate out from the line's own tax and amount.
        /// </summary>
        private async Task<IReadOnlyDictionary<string, LineTax>?> ResolveTaxRatesAsync(FiscalizationContext ctx, SalesDocument document)
        {
            if (document.IsNoTax || XeroFiscalizeMapper.SalesLines(document).All(l => string.IsNullOrWhiteSpace(l.TaxType)))
                return null; // nothing to look up
            try
            {
                return await _taxRates.GetLineTaxesAsync(ctx.Xero);
            }
            catch (Exception ex) when (ex is XeroApiException or HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "Could not read tax rates from Xero; working each line's rate out from its own tax and amount");
                return null;
            }
        }

        /// <summary>HS code per item code, as saved in Fiscalize. Lines whose item has none, or that carry no item code, take the default later.</summary>
        private async Task<Dictionary<string, string?>> BuildHsCodeLookupAsync(FiscalizationContext ctx, SalesDocument document)
        {
            var itemCodes = XeroFiscalizeMapper.SalesLines(document)
                .Select(l => l.ItemCode?.Trim())
                .Where(c => !string.IsNullOrEmpty(c)).Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (itemCodes.Count == 0) return ItemHsCodes.Lookup(Array.Empty<(string, string?)>());

            var records = await _itemHsCodeRepository.GetManyAsync(ctx.Config.AppUserCompanyId, itemCodes);
            return ItemHsCodes.Lookup(records.Select(r => (r.ItemCode, r.HsCode)));
        }

        /// <summary>
        /// The original invoice for a credit note: the one the caller chose, else the invoice the credit is allocated
        /// to in Xero (the largest allocation when there are several). Either way it must be an invoice fiscalised
        /// here, since the platform credits that fiscal receipt.
        /// </summary>
        internal async Task<string> ResolveBaseDocNoAsync(int companyId, CreditNote creditNote, string? chosenDocNo)
        {
            var chosen = chosenDocNo?.Trim();
            if (!string.IsNullOrEmpty(chosen))
            {
                var picked = await _fiscalRecordRepository.GetByDocumentNumberAsync(companyId, FiscalDocumentType.Invoice, chosen)
                    ?? throw new FiscalizeXero.Api.Exceptions.UserFacingException(CreditNoteOriginalInvoice.NotFiscalizedMessage(chosen));
                return picked.DocumentNumber;
            }

            var original = CreditNoteOriginalInvoice.Resolve(creditNote)
                ?? throw new FiscalizeXero.Api.Exceptions.UserFacingException(CreditNoteOriginalInvoice.UnknownMessage(XeroDocumentTypes.NumberOf(creditNote)));
            var record = await FindFiscalisedInvoiceAsync(companyId, original)
                ?? throw new FiscalizeXero.Api.Exceptions.UserFacingException(CreditNoteOriginalInvoice.NotFiscalizedMessage(original.InvoiceNumber ?? FiscalDocumentType.Key(original.InvoiceId)));
            return record.DocumentNumber;
        }

        /// <summary>The fiscal record of an allocated invoice: by its Xero id, else by its number.</summary>
        private async Task<FiscalRecord?> FindFiscalisedInvoiceAsync(int companyId, CreditNoteOriginalInvoice.Candidate invoice)
        {
            var byId = await _fiscalRecordRepository.GetAsync(companyId, FiscalDocumentType.Key(invoice.InvoiceId));
            if (byId is { DocumentType: FiscalDocumentType.Invoice, ReceiptNumber: not null }) return byId;
            return string.IsNullOrWhiteSpace(invoice.InvoiceNumber)
                ? null
                : await _fiscalRecordRepository.GetByDocumentNumberAsync(companyId, FiscalDocumentType.Invoice, invoice.InvoiceNumber);
        }

        // ===== After the platform answered =====

        /// <summary>Stores the fiscal record and writes the result back to Xero (best effort).</summary>
        private async Task<FiscalizeResultModel> CompleteAsync(FiscalizationContext ctx, string documentType, SalesDocument document,
            string docNo, FiscalizeInvoiceFiscalData fiscalData, bool alreadyFiscalized)
        {
            var companyId = ctx.Config.AppUserCompanyId;
            var key = FiscalDocumentType.Key(document.DocumentId);
            var previous = await _fiscalRecordRepository.GetAsync(companyId, key);
            var record = BuildRecord(companyId, key, documentType, docNo, fiscalData);
            // The same receipt again (someone clicked Fiscalize on a document ZIMRA already has): nothing new to note in Xero.
            var sameReceipt = previous != null && previous.ReceiptNumber.HasValue && previous.ReceiptNumber == record.ReceiptNumber
                              && string.Equals(previous.VerificationCode, record.VerificationCode, StringComparison.Ordinal);

            await _fiscalRecordRepository.UpsertAsync(record);
            await _failures.ClearAsync(companyId, documentType, key);

            var warning = await WriteBackAsync(ctx, document, record, sameReceipt);
            var attachmentQueued = QueueStampedPdfIfEnabled(ctx, record);
            return ToResult(record, warning, null, alreadyFiscalized, attachmentQueued);
        }

        /// <summary>
        /// Records the fiscal result on the Xero document, as the company's settings ask: a history note, and a short
        /// marker in the Reference. (The stamped PDF goes through the attachment queue.) Xero locks paid invoices and
        /// allocated credit notes against most changes, so every step is best effort: a refusal becomes a warning
        /// and never undoes or fails the fiscalisation.
        /// </summary>
        private async Task<string?> WriteBackAsync(FiscalizationContext ctx, SalesDocument document, FiscalRecord record, bool sameReceipt)
        {
            var warnings = new List<string>();
            var label = XeroDocumentTypes.Label(record.DocumentType);
            var endpoint = DocumentExtrasService.For(document);

            if (ctx.Config.WritebackHistoryNote)
            {
                try
                {
                    // History notes can never be edited or removed, so the same receipt is not noted twice.
                    if (!sameReceipt || !await HistoryMentionsAsync(ctx, endpoint, document.DocumentId, record))
                        await DocumentExtrasService.AddNoteAsync(ctx.Xero, endpoint, document.DocumentId, FiscalHistoryNote.Fiscalised(record));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Fiscalisation of {Type} {DocNo} succeeded but the Xero history note failed", label, record.DocumentNumber);
                    warnings.Add($"Fiscalized successfully, but the fiscal details could not be added to the history of {label} {record.DocumentNumber} in Xero ({Reason(ex)}).");
                }
            }

            if (ctx.Config.WritebackReference)
            {
                var reference = FiscalReferenceMarker.Add(document.Reference, record);
                if (reference != null)
                {
                    try
                    {
                        await XeroDocumentTypes.UpdateReferenceAsync(ctx.Xero, document, reference);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Fiscalisation of {Type} {DocNo} succeeded but Xero refused the Reference update", label, record.DocumentNumber);
                        warnings.Add($"Fiscalized successfully, but Xero did not accept the fiscal marker in the Reference of {label} {record.DocumentNumber} ({Reason(ex)}). " +
                                     "Xero locks paid, allocated and period-locked documents against changes; the fiscal details are still in Fiscalize.");
                    }
                }
                else if (!FiscalReferenceMarker.HasMarker(document.Reference) && record.ReceiptNumber.HasValue)
                {
                    warnings.Add($"Fiscalized successfully, but the Reference of {label} {record.DocumentNumber} in Xero has no room left for the fiscal marker, so it was left as it is.");
                }
            }

            return warnings.Count == 0 ? null : string.Join(" ", warnings);
        }

        /// <summary>Whether the document's history in Xero already carries this receipt's note. When Xero can't be asked, assume it does.</summary>
        private async Task<bool> HistoryMentionsAsync(FiscalizationContext ctx, string endpoint, Guid documentId, FiscalRecord record)
        {
            try
            {
                var needle = !string.IsNullOrWhiteSpace(record.VerificationCode) ? record.VerificationCode!.Trim() : $"Receipt no. {record.ReceiptNumber}";
                var history = await DocumentExtrasService.GetHistoryAsync(ctx.Xero, endpoint, documentId);
                return history.Any(h => h.Details != null && h.Details.Contains(needle, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Could not read the Xero history of {DocumentId}; not adding a second note", documentId);
                return true;
            }
        }

        private static string Reason(Exception ex) => ex is XeroApiException xero ? xero.Message : "Xero could not be reached";

        // ===== Explicit user actions on an already-fiscalized document =====

        /// <summary>
        /// Resubmits the invoice to the Fiscalize platform, which credits the original FDMS record and issues a
        /// fresh one. Used to recover from FDMS errors or to apply corrected data. Cannot be undone.
        /// </summary>
        public async Task<FiscalizeResultModel> RefiscalizeInvoiceAsync(Guid invoiceId)
        {
            var ctx = await ResolveCurrentUserContextAsync();
            return await WithDocumentLockAsync(ctx, invoiceId, FiscalDocumentType.Invoice, async () =>
            {
                var (invoice, request) = await BuildInvoiceRequestAsync(ctx, invoiceId);
                var fiscalData = await CallAsDeviceAsync(ctx, auth => FiscalizeInvoiceService.RefiscalizeAsync(auth, request.DocNo, request))
                    ?? throw new FiscalizeXero.Api.Exceptions.UserFacingException("Fiscalize platform returned no fiscal data");
                return await CompleteAsync(ctx, FiscalDocumentType.Invoice, invoice, request.DocNo, fiscalData, alreadyFiscalized: false);
            });
        }

        /// <summary>
        /// Applies corrected buyer details (or removes the buyer for a cash sale) and re-fiscalizes the invoice.
        /// The corrected identity is also saved against the customer so later fiscalizations use it.
        /// </summary>
        public async Task<FiscalizeResultModel> UpdateInvoiceFiscalInfoAsync(Guid invoiceId, UpdateInvoiceFiscalInfoRequest update)
        {
            var ctx = await ResolveCurrentUserContextAsync();
            return await WithDocumentLockAsync(ctx, invoiceId, FiscalDocumentType.Invoice, async () =>
            {
                var existing = await _fiscalRecordRepository.GetAsync(ctx.Config.AppUserCompanyId, FiscalDocumentType.Key(invoiceId));
                if (existing == null || !existing.ReceiptNumber.HasValue)
                    throw new FiscalizeXero.Api.Exceptions.UserFacingException("Only fiscalized invoices can have their fiscal details updated. Fiscalize the invoice first.");

                if (!update.RemoveBuyer)
                {
                    var validationError = ValidateBuyerData(update.BuyerData);
                    if (validationError != null) throw new FiscalizeXero.Api.Exceptions.UserFacingException(validationError);
                }

                var (invoice, request) = await BuildInvoiceRequestAsync(ctx, invoiceId, resolveBuyer: false);
                request.BuyerData = update.RemoveBuyer ? null : BuildBuyerDataFromRequest(update.BuyerData!);

                var fiscalData = await CallAsDeviceAsync(ctx, auth => FiscalizeInvoiceService.RefiscalizeAsync(auth, request.DocNo, request))
                    ?? throw new FiscalizeXero.Api.Exceptions.UserFacingException("Fiscalize platform returned no fiscal data");

                var contactId = invoice.Contact?.ContactID ?? Guid.Empty;
                if (!update.RemoveBuyer && update.BuyerData != null && contactId != Guid.Empty)
                {
                    try
                    {
                        await SaveCorrectedBuyerAsync(ctx, contactId, update.BuyerData);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Could not save corrected buyer details for Xero contact {ContactId}", contactId);
                    }
                }

                return await CompleteAsync(ctx, FiscalDocumentType.Invoice, invoice, request.DocNo, fiscalData, alreadyFiscalized: false);
            });
        }

        /// <summary>
        /// Reverses a fiscalized invoice at FDMS (the Fiscalize platform submits a credit note) and frees its document
        /// number. The invoice returns to "not fiscalized" and can be fiscalized again. Cannot be undone.
        /// </summary>
        public async Task<FiscalCancelResultModel> FiscalCancelInvoiceAsync(Guid invoiceId)
        {
            var ctx = await ResolveCurrentUserContextAsync();
            var key = FiscalDocumentType.Key(invoiceId);
            return await WithDocumentLockAsync(ctx, invoiceId, FiscalDocumentType.Invoice, async () =>
            {
                var existing = await _fiscalRecordRepository.GetAsync(ctx.Config.AppUserCompanyId, key);
                if (existing == null || !existing.ReceiptNumber.HasValue || existing.DocumentType != FiscalDocumentType.Invoice)
                    throw new FiscalizeXero.Api.Exceptions.UserFacingException("Only fiscalized invoices can be fiscally cancelled. Fiscalize the invoice first.");

                // The number it was fiscalised under, which is what the platform knows it by.
                var docNo = existing.DocumentNumber;
                await CallAsDeviceAsync(ctx, async auth => { await FiscalizeInvoiceService.FiscalCancelAsync(auth, docNo); return true; });
                _logger.LogInformation("Invoice {DocNo} fiscally cancelled for company {CompanyId}", docNo, ctx.Config.AppUserCompanyId);

                await _fiscalRecordRepository.DeleteAsync(ctx.Config.AppUserCompanyId, key);
                // The write-back below edits the invoice in Xero, and that change comes straight back as a webhook.
                // Without this the invoice would be sent to ZIMRA again, unchanged, within seconds.
                await _failures.SuppressAsync(ctx.Config.AppUserCompanyId, FiscalDocumentType.Invoice, key,
                    $"Fiscally cancelled {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC; fiscalise it from the invoice page to send it again.");

                var warning = await WriteBackCancellationAsync(ctx, invoiceId, existing);
                return new FiscalCancelResultModel
                {
                    DocumentId = key,
                    DocumentNumber = docNo,
                    Message = $"Invoice {docNo} fiscally cancelled. A credit note was submitted to FDMS to reverse it.",
                    WritebackWarning = warning
                };
            });
        }

        /// <summary>
        /// After a fiscal cancellation: a history note saying so, and our marker taken back out of the Reference if it
        /// is there (whatever the setting is now). Best effort; the stamped PDF stays attached, because Xero's API
        /// cannot remove an attachment.
        /// </summary>
        private async Task<string?> WriteBackCancellationAsync(FiscalizationContext ctx, Guid invoiceId, FiscalRecord cancelled)
        {
            var warnings = new List<string>();

            if (ctx.Config.WritebackHistoryNote)
            {
                try
                {
                    await DocumentExtrasService.AddNoteAsync(ctx.Xero, DocumentExtrasService.Invoices, invoiceId, FiscalHistoryNote.Cancelled(cancelled, DateTime.UtcNow));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Fiscal cancel of invoice {DocNo} succeeded but the Xero history note failed", cancelled.DocumentNumber);
                    warnings.Add($"Cancelled at FDMS, but the cancellation could not be noted in the history of invoice {cancelled.DocumentNumber} in Xero ({Reason(ex)}).");
                }
            }

            try
            {
                var invoice = await XeroDocumentTypes.GetAsync(ctx.Xero, FiscalDocumentType.Invoice, invoiceId);
                var reference = invoice == null ? null : FiscalReferenceMarker.Remove(invoice.Reference);
                if (invoice != null && reference != null)
                    await XeroDocumentTypes.UpdateReferenceAsync(ctx.Xero, invoice, reference);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Fiscal cancel of invoice {DocNo} succeeded but the fiscal marker could not be removed from its Reference", cancelled.DocumentNumber);
                warnings.Add($"Cancelled at FDMS, but the fiscal marker could not be removed from the Reference of invoice {cancelled.DocumentNumber} in Xero ({Reason(ex)}). Remove it in Xero if the invoice can still be edited.");
            }

            return warnings.Count == 0 ? null : string.Join(" ", warnings);
        }

        /// <summary>
        /// Resubmits a fiscalized credit note to the platform (credits the original fiscal credit note and issues a
        /// fresh one). Used to recover from FDMS errors or apply corrected data. Cannot be undone.
        /// </summary>
        public async Task<FiscalizeResultModel> RefiscalizeCreditNoteAsync(Guid creditNoteId, string? baseDocNo = null)
        {
            var ctx = await ResolveCurrentUserContextAsync();
            return await WithDocumentLockAsync(ctx, creditNoteId, FiscalDocumentType.CreditNote, async () =>
            {
                var existing = await _fiscalRecordRepository.GetAsync(ctx.Config.AppUserCompanyId, FiscalDocumentType.Key(creditNoteId));
                if (existing == null || !existing.ReceiptNumber.HasValue)
                    throw new FiscalizeXero.Api.Exceptions.UserFacingException("Only fiscalized credit notes can be re-fiscalized. Fiscalize the credit note first.");

                var (creditNote, request) = await BuildCreditNoteRequestAsync(ctx, creditNoteId, baseDocNo);
                var fiscalData = await CallAsDeviceAsync(ctx, auth => FiscalizeCreditNoteService.RefiscalizeAsync(auth, request.DocNo, request))
                    ?? throw new FiscalizeXero.Api.Exceptions.UserFacingException("Fiscalize platform returned no fiscal data");
                return await CompleteAsync(ctx, FiscalDocumentType.CreditNote, creditNote, request.DocNo, fiscalData, alreadyFiscalized: false);
            });
        }

        /// <summary>The Fiscalize platform's own rendering of the fiscal invoice (device session).</summary>
        public async Task<byte[]> GetInvoiceFiscalizePdfAsync(Guid invoiceId)
        {
            var (ctx, record) = await ResolveFiscalisedAsync(FiscalDocumentType.Invoice, invoiceId);
            return await CallAsDeviceAsync(ctx, auth => FiscalizeInvoiceService.GetPdfByDocNoAsync(auth, record.DocumentNumber));
        }

        /// <summary>The Fiscalize platform's own rendering of the fiscal credit note (device session).</summary>
        public async Task<byte[]> GetCreditNoteFiscalizePdfAsync(Guid creditNoteId)
        {
            var (ctx, record) = await ResolveFiscalisedAsync(FiscalDocumentType.CreditNote, creditNoteId);
            return await CallAsDeviceAsync(ctx, auth => FiscalizeCreditNoteService.GetPdfByDocNoAsync(auth, record.DocumentNumber));
        }

        /// <summary>The fiscal record gives the number the platform knows the document by, so Xero is not asked at all.</summary>
        private async Task<(FiscalizationContext Ctx, FiscalRecord Record)> ResolveFiscalisedAsync(string documentType, Guid documentId)
        {
            var ctx = await ResolveCurrentUserContextAsync();
            var record = await _fiscalRecordRepository.GetAsync(ctx.Config.AppUserCompanyId, FiscalDocumentType.Key(documentId));
            if (record == null || record.DocumentType != documentType || !record.ReceiptNumber.HasValue)
                throw new NotFiscalizedException($"This {XeroDocumentTypes.Label(documentType)} has not been fiscalized yet, so the Fiscalize platform has no PDF for it.");
            return (ctx, record);
        }

        /// <summary>
        /// Remembers corrected buyer details on the customer so the next document fiscalises without asking again.
        /// The TIN and VAT number are saved as typed; a name or contact detail only when it differs from the Xero contact.
        /// The TIN and VAT number are then copied onto the Xero contact, like a save on the Customers page.
        /// </summary>
        private async Task SaveCorrectedBuyerAsync(FiscalizationContext ctx, Guid contactId, UpdateInvoiceFiscalInfoBuyerData buyer)
        {
            var contact = await ContactService.GetByIdAsync(ctx.Xero, contactId);
            var fromXero = BuyerCompliance.Assess(BuyerResolver.Resolve(contact, null), contact);
            static string? Changed(string? typed, string? xero)
                => string.IsNullOrWhiteSpace(typed) || string.Equals(typed.Trim(), xero?.Trim(), StringComparison.OrdinalIgnoreCase) ? null : typed.Trim();

            var phone = BuyerContactResolver.NormalisePhone(buyer.Contacts?.PhoneNumber);
            var email = BuyerContactResolver.FirstEmail(buyer.Contacts?.Email);
            var values = new ContactVatRecord
            {
                VatNumber = buyer.VatNumber?.Trim(),
                Tin = buyer.Tin?.Trim(),
                RegisteredName = Changed(buyer.RegisteredName, fromXero.RegisteredName),
                TradeName = string.IsNullOrWhiteSpace(buyer.TradeName) ? null : Changed(buyer.TradeName, fromXero.TradeName),
                Phone = phone == fromXero.Phone ? null : phone,
                Email = Changed(email, fromXero.Email),
                HouseNo = Changed(buyer.Address?.HouseNo, fromXero.Address.HouseNo),
                Street = Changed(buyer.Address?.Street, fromXero.Address.Street),
                City = Changed(buyer.Address?.City, fromXero.Address.City),
                Province = Changed(buyer.Address?.Province, fromXero.Address.Province)
            };

            // Same as the Customers page: copy the TIN and VAT number onto the Xero contact, and keep in Fiscalize
            // only what Xero doesn't hold. A refusal from Xero never undoes the corrections.
            if (contact != null)
                values = (await XeroContactUpdater.ApplyAsync(ctx.Xero, contact, values, _logger)).Values;
            await _contactVatRepository.UpsertAsync(ctx.Config.AppUserCompanyId, FiscalDocumentType.Key(contactId), values);
        }

        // Mirrors the Fiscalize platform's buyer validation so users get immediate feedback before the round-trip.
        internal static string? ValidateBuyerData(UpdateInvoiceFiscalInfoBuyerData? buyer)
        {
            if (buyer == null)
                return "Buyer details are required. To remove the buyer and convert to a cash sale, use the dedicated option.";

            var tin = buyer.Tin?.Trim();
            var vat = buyer.VatNumber?.Trim();
            if (string.IsNullOrEmpty(tin))
                return "Buyer TIN is required. To remove the buyer and convert to a cash sale, use the dedicated option.";
            if (!System.Text.RegularExpressions.Regex.IsMatch(tin, @"^\d{10}$"))
                return "Buyer TIN must be exactly 10 numeric digits.";
            if (tin == "0000000000")
                return "Buyer TIN cannot be 0000000000. Provide a valid TIN or remove the buyer to fiscalize as a cash sale.";
            if (!string.IsNullOrEmpty(vat) && !System.Text.RegularExpressions.Regex.IsMatch(vat, @"^\d{9}$"))
                return "Buyer VAT number must be exactly 9 numeric digits.";
            if (string.IsNullOrWhiteSpace(buyer.RegisteredName))
                return "Buyer registered name is required when a TIN is provided.";
            if (string.IsNullOrWhiteSpace(buyer.Contacts?.PhoneNumber))
                return "Buyer phone number is required when a TIN is provided.";
            if (BuyerContactResolver.NormalisePhone(buyer.Contacts.PhoneNumber) == null)
                return "Buyer phone number must have between 7 and 15 digits, for example 0771234567 or +263771234567.";
            if (string.IsNullOrWhiteSpace(buyer.Contacts?.Email))
                return "Buyer email is required when a TIN is provided.";
            if (BuyerContactResolver.FirstEmail(buyer.Contacts.Email) == null)
                return "Buyer email is not a valid email address.";
            if (string.IsNullOrWhiteSpace(buyer.Address?.Province))
                return "Buyer province is required when a TIN is provided.";
            if (string.IsNullOrWhiteSpace(buyer.Address?.Street))
                return "Buyer street is required when a TIN is provided.";
            if (string.IsNullOrWhiteSpace(buyer.Address?.HouseNo))
                return "Buyer house/building number is required when a TIN is provided.";
            if (string.IsNullOrWhiteSpace(buyer.Address?.City))
                return "Buyer city is required when a TIN is provided.";
            return null;
        }

        private static FiscalizeInvoiceBuyerData BuildBuyerDataFromRequest(UpdateInvoiceFiscalInfoBuyerData buyer) => new()
        {
            RegisteredName = buyer.RegisteredName!.Trim(),
            TradeName = BuyerCompliance.CapTradeName(string.IsNullOrWhiteSpace(buyer.TradeName) ? buyer.RegisteredName : buyer.TradeName),
            VatNumber = string.IsNullOrWhiteSpace(buyer.VatNumber) ? null : buyer.VatNumber.Trim(),
            Tin = buyer.Tin!.Trim(),
            Contacts = new FiscalizeInvoiceBuyerContacts
            {
                PhoneNumber = BuyerContactResolver.NormalisePhone(buyer.Contacts!.PhoneNumber) ?? buyer.Contacts.PhoneNumber!.Trim(),
                Email = BuyerContactResolver.FirstEmail(buyer.Contacts.Email) ?? buyer.Contacts.Email!.Trim()
            },
            Address = new FiscalizeInvoiceBuyerAddress
            {
                Province = buyer.Address!.Province!.Trim(), Street = buyer.Address.Street!.Trim(),
                HouseNo = buyer.Address.HouseNo!.Trim() is var h && h.Length > BuyerAddressParser.MaxHouseNoLength ? h[..BuyerAddressParser.MaxHouseNoLength] : h,
                City = buyer.Address.City!.Trim()
            }
        };

        // ===== Context builders =====

        private async Task<FiscalizationContext> ResolveCurrentUserContextAsync()
        {
            var (config, company) = await ResolveCurrentUserConnectionAsync();
            return await BuildContextAsync(config, company);
        }

        /// <summary>The signed-in user's company connection, before any document-specific work (and failure tracking) starts.</summary>
        private async Task<(ClientConfig Config, AppUserCompany Company)> ResolveCurrentUserConnectionAsync()
        {
            var user = await _currentUserService.GetCurrentUserAsync()
                ?? throw new FiscalizeXero.Api.Exceptions.UserFacingException("No authenticated user");
            if (user.ActiveCompany == null)
                throw new FiscalizeXero.Api.Exceptions.UserFacingException("No active company on user");

            // The company's connection, whichever colleague set it up. Its company row holds the devices and the
            // fiscal records, so fiscalise against that row rather than the signed-in user's own.
            var config = await _currentUserService.GetCurrentClientConfigAsync()
                ?? throw new FiscalizeXero.Api.Exceptions.UserFacingException("Xero is not connected for this company. Connect Xero first.");

            return (config, config.AppUserCompany ?? user.ActiveCompany);
        }

        /// <summary>
        /// Fiscalisation is performed by the company's ZIMRA device, never by the signed-in user: the device's
        /// API key/secret logs in to the Fiscalize platform and that session is cached by <see cref="IDeviceSessionService"/>.
        /// The same context therefore serves the interactive controllers, the webhook worker and the sync.
        /// </summary>
        private async Task<FiscalizationContext> BuildContextAsync(ClientConfig config, AppUserCompany appUserCompany)
        {
            var xero = await _xeroAuthBuilder.BuildAsync(config)
                ?? throw new FiscalizeXero.Api.Exceptions.UserFacingException("The Xero connection is not authorised any more. Reconnect Xero for this company, then try again.");

            // The company entity doesn't always arrive with its devices loaded (the signed-in user's doesn't), so
            // look the device up explicitly rather than trusting the navigation property.
            var device = appUserCompany.DeviceConfigs?.FirstOrDefault(d => d.IsDefault)
                       ?? appUserCompany.DeviceConfigs?.FirstOrDefault()
                       ?? await _deviceConfigService.GetDefaultAsync(appUserCompany.Id)
                       ?? throw new FiscalizeXero.Api.Exceptions.UserFacingException("No ZIMRA device configured for this company. Set one up under Device Setup.");

            if (string.IsNullOrEmpty(device.ApiKey) || string.IsNullOrEmpty(device.ApiSecret))
                throw new FiscalizeXero.Api.Exceptions.UserFacingException(
                    $"Device #{device.DeviceNumber} has no API key configured. Generate one under Device Setup before fiscalising.");

            return new FiscalizationContext(config, xero, device, appUserCompany);
        }

        /// <summary>
        /// Runs a Fiscalize platform call with the device's session token. When the stored API key/secret are
        /// rejected outright, a fresh key is provisioned with the company's Fiscalize session (the same recovery the
        /// Zoho Books integration does) before failing with a message that says what to fix. A 401 on the call
        /// itself means the cached token is stale, so re-login once and retry.
        /// </summary>
        private async Task<T> CallAsDeviceAsync<T>(FiscalizationContext ctx, Func<FiscalizeAuthModel, Task<T>> call)
        {
            FiscalizeAuthModel auth;
            try
            {
                auth = await _deviceSessionService.GetAuthAsync(ctx.Device);
            }
            catch (FiscalizeApiException ex) when (ex.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
            {
                // The platform is down or erroring: the key is not at fault, so it is not rotated. The failure is
                // retried automatically once the platform answers again.
                _logger.LogWarning(ex, "Device login for config {DeviceConfigId} (device #{DeviceNumber}) failed with {StatusCode}; not rotating the key",
                    ctx.Device.Id, ctx.Device.DeviceNumber, ex.StatusCode);
                throw new FiscalizeApiException(ex.StatusCode, DeviceLoginMessage(ctx.Device, ex, "The Fiscalize platform did not accept the sign-in just now; this is retried automatically."));
            }
            catch (FiscalizeApiException ex)
            {
                _logger.LogError(ex, "Device login failed for config {DeviceConfigId} (device #{DeviceNumber}). Status: {StatusCode}. Body: {Body}",
                    ctx.Device.Id, ctx.Device.DeviceNumber, ex.StatusCode, ex.ResponseBody);

                if (!await TryAutoProvisionDeviceCredentialsAsync(ctx))
                    throw new FiscalizeApiException(ex.StatusCode, DeviceLoginMessage(ctx.Device, ex, "Open Device Setup and generate a new API key for this device, then try again."));

                try
                {
                    auth = await _deviceSessionService.GetAuthAsync(ctx.Device);
                }
                catch (FiscalizeApiException retryEx)
                {
                    _logger.LogError(retryEx, "Device login still failed for config {DeviceConfigId} after provisioning a new key", ctx.Device.Id);
                    throw new FiscalizeApiException(retryEx.StatusCode, DeviceLoginMessage(ctx.Device, retryEx, "A new API key was issued but the Fiscalize platform still refused it. Check the device is active on the Fiscalize platform."));
                }
            }

            try
            {
                return await call(auth);
            }
            catch (FiscalizeApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("Fiscalize rejected device #{DeviceNumber} session (401); re-logging in and retrying once", ctx.Device.DeviceNumber);
                try
                {
                    auth = await _deviceSessionService.ReloginAsync(ctx.Device);
                }
                catch (FiscalizeApiException loginEx)
                {
                    throw new FiscalizeApiException(loginEx.StatusCode, DeviceLoginMessage(ctx.Device, loginEx, "Open Device Setup and generate a new API key for this device, then try again."));
                }
                try
                {
                    return await call(auth);
                }
                catch (FiscalizeApiException retryEx) when (retryEx.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new FiscalizeApiException(retryEx.StatusCode,
                        $"The Fiscalize platform rejected the request even after a fresh device login. The API key for device #{ctx.Device.DeviceNumber} may not have permission to fiscalize ({retryEx.Message}).");
                }
            }
        }

        private static string DeviceLoginMessage(AppUserDeviceConfig device, FiscalizeApiException ex, string advice) =>
            $"Device #{device.DeviceNumber} could not sign in to the Fiscalize platform ({(int)ex.StatusCode} {ex.Message}). {advice}";

        /// <summary>
        /// Rotates (or creates) the device's API key using the company's own Fiscalize session, so a revoked or
        /// rotated key recovers without manual steps. Only the key provisioning uses the company session; the
        /// fiscalisation itself still runs as the device.
        /// </summary>
        private async Task<bool> TryAutoProvisionDeviceCredentialsAsync(FiscalizationContext ctx)
        {
            var company = ctx.Company;
            var device = ctx.Device;
            // The company's platform session is refreshed silently when it has lapsed; only a missing or rejected
            // refresh token leaves nothing to provision with.
            var companyAuth = await _fiscalizeSession.TryGetAuthForCompanyAsync(company);
            if (companyAuth == null)
            {
                _logger.LogInformation("Cannot re-provision device config {Id}: no usable company Fiscalize session", device.Id);
                return false;
            }
            try
            {
                FiscalizeDeviceApiKey newKey;
                if (device.ApiKeyId.HasValue)
                {
                    _logger.LogInformation("Rotating API key {KeyId} for device config {Id} (device #{DeviceNumber})", device.ApiKeyId, device.Id, device.DeviceNumber);
                    newKey = await FiscalizeDeviceApiKeyService.GenerateNewKeyAsync(companyAuth, device.ApiKeyId.Value);
                }
                else
                {
                    _logger.LogInformation("Creating API key for device config {Id} (device #{DeviceNumber})", device.Id, device.DeviceNumber);
                    newKey = await FiscalizeDeviceApiKeyService.CreateAsync(companyAuth, device.DeviceId,
                        new CreateFiscalizeApiKeyRequest { Description = $"Auto-provisioned {DateTime.UtcNow:u}", Permissions = "ReadWrite" });
                }
                if (string.IsNullOrEmpty(newKey.ApiKey) || string.IsNullOrEmpty(newKey.ApiSecret))
                {
                    _logger.LogWarning("Provisioned key for device config {Id} is missing the key or secret", device.Id);
                    return false;
                }

                device.ApiKeyId = newKey.Id;
                device.ApiKey = newKey.ApiKey;
                device.ApiSecret = newKey.ApiSecret;
                await _deviceConfigService.UpdateCredentialsAsync(device.Id, newKey.Id, newKey.ApiKey, newKey.ApiSecret);
                _deviceSessionService.InvalidateSession(device.Id);
                _logger.LogInformation("Provisioned new credentials for device config {Id} (device #{DeviceNumber}), key {KeyId}", device.Id, device.DeviceNumber, newKey.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auto-provisioning failed for device config {Id} (device #{DeviceNumber})", device.Id, device.DeviceNumber);
                return false;
            }
        }

        // ===== Shared helpers =====

        private static FiscalRecord BuildRecord(int appUserCompanyId, string docId, string docType, string docNumber, FiscalizeInvoiceFiscalData fiscalData) =>
            new()
            {
                AppUserCompanyId = appUserCompanyId,
                DocumentId = docId,
                DocumentType = docType,
                DocumentNumber = docNumber,
                VerificationCode = fiscalData.VerificationCode,
                QrCodeUrl = fiscalData.QrlUrl,
                ReceiptNumber = fiscalData.ReceiptNumber,
                GlobalNumber = fiscalData.GlobalNumber,
                DeviceId = (int)fiscalData.DeviceId,
                FiscalDayNumber = fiscalData.FiscalDayNumber,
                ValidationError = MapValidationError(fiscalData.ValidationError),
                FiscalizedAt = DateTime.UtcNow
            };

        /// <summary>
        /// With attaching switched on, the stamped PDF is attached to the Xero document in the background so the
        /// caller gets the receipt without waiting for the PDF round trips. Returns true when a job was queued.
        /// </summary>
        private bool QueueStampedPdfIfEnabled(FiscalizationContext ctx, FiscalRecord record)
        {
            if (!ctx.Config.FiscalStampEnabled) return false;
            _attachments.Enqueue(new AttachmentJob(ctx.Config.Id, ctx.Config.AppUserCompanyId, record.DocumentId, record.DocumentNumber));
            return true;
        }

        private static FiscalizeResultModel ToResult(FiscalRecord record, string? warning, string? attachmentWarning = null, bool alreadyFiscalized = false, bool attachmentQueued = false) =>
            new()
            {
                AlreadyFiscalized = alreadyFiscalized,
                AttachmentQueued = attachmentQueued,
                DocumentId = record.DocumentId,
                DocumentNumber = record.DocumentNumber,
                DocumentType = record.DocumentType,
                VerificationCode = record.VerificationCode,
                QrCodeUrl = record.QrCodeUrl,
                ReceiptNumber = record.ReceiptNumber,
                GlobalNumber = record.GlobalNumber,
                DeviceId = record.DeviceId,
                FiscalDayNumber = record.FiscalDayNumber,
                ValidationError = record.ValidationError,
                FiscalizedAt = record.FiscalizedAt,
                WritebackWarning = warning,
                AttachmentWarning = attachmentWarning
            };

        /// <summary>
        /// Fiscalize's ValidationErrorColors: None=0, Gray=1, Yellow=2, Red=3. Only Red means ZIMRA rejected the
        /// receipt; Gray and Yellow are remarks on a receipt that was accepted.
        /// </summary>
        private static string MapValidationError(int? code) => code switch
        {
            null or 0 => string.Empty,
            1 => "Gray",
            2 => "Yellow",
            3 => "Red",
            _ => $"Code {code}"
        };

        private record FiscalizationContext(ClientConfig Config, XeroAuthModel Xero, AppUserDeviceConfig Device, AppUserCompany Company)
        {
            public int DeviceId => (int)Device.DeviceId;
        }
    }
}
