using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Repositories;
using FiscalizeXero.Api.Services.CurrentUser;
using FiscalizeXero.Api.Services.DeviceSession;
using FiscalizeXero.Api.Services.Fiscalization;
using FiscalizeXero.Api.Services.Layouts;
using FiscalizeXero.Api.Services.PdfStamp;
using FiscalizeXero.Api.Services.XeroAuth;
using FiscalizeXero.Fiscalize.Services.CreditNotes;
using FiscalizeXero.Fiscalize.Services.Invoices;
using FiscalizeXero.Shared.Models.Fiscalize;
using FiscalizeXero.Xero.Exceptions;
using FiscalizeXero.Xero.Models;
using FiscalizeXero.Xero.Services;
using XeroAuthModel = FiscalizeXero.Xero.XeroAuth;

namespace FiscalizeXero.Api.Services.FiscalDocuments
{
    public class FiscalDocumentService : IFiscalDocumentService
    {
        private readonly ICurrentUserService _currentUser;
        private readonly IXeroAuthBuilder _authBuilder;
        private readonly IFiscalRecordRepository _records;
        private readonly IPdfStampService _stamper;
        private readonly IPlatformLayoutService _layouts;
        private readonly IContactVatRepository _contactVat;
        private readonly IXeroTaxRateCache _taxRates;
        private readonly IDeviceCaller _device;
        private readonly IFiscalPdfCounter _counter;
        private readonly ILogger<FiscalDocumentService> _logger;

        public FiscalDocumentService(
            ICurrentUserService currentUser,
            IXeroAuthBuilder authBuilder,
            IFiscalRecordRepository records,
            IPdfStampService stamper,
            IPlatformLayoutService layouts,
            IContactVatRepository contactVat,
            IXeroTaxRateCache taxRates,
            IDeviceCaller device,
            IFiscalPdfCounter counter,
            ILogger<FiscalDocumentService> logger)
        {
            _currentUser = currentUser;
            _authBuilder = authBuilder;
            _records = records;
            _stamper = stamper;
            _layouts = layouts;
            _contactVat = contactVat;
            _taxRates = taxRates;
            _device = device;
            _counter = counter;
            _logger = logger;
        }

        public async Task<StampedPdf> GetStampedPdfForCurrentUserAsync(string documentType, Guid documentId)
        {
            var (config, auth, record) = await ResolveAsync(documentType, documentId,
                "This document has not been fiscalized yet, so there is no fiscal stamp to print.");
            return await BuildStampedPdfAsync(config, auth, record);
        }

        public async Task<StampedPdf> GetLayoutPdfForCurrentUserAsync(string documentType, Guid documentId, string? paperSize)
        {
            var (config, auth, record) = await ResolveAsync(documentType, documentId,
                "This document has not been fiscalized yet, so there is no fiscal document to print.");
            var paper = LayoutPapers.Normalise(string.IsNullOrWhiteSpace(paperSize) ? config.LayoutPaperSize : paperSize);
            var (bytes, _) = await RenderLayoutAsync(config, auth, record, paper);
            return new StampedPdf(bytes, FiscalDocumentNaming.FileNameFor(record));
        }

        public async Task<FiscalAttachmentStatusModel> GetAttachmentStatusForCurrentUserAsync(string documentType, Guid documentId)
        {
            var (config, auth, record) = await ResolveAsync(documentType, documentId, "This document has not been fiscalized yet.");
            var fileName = FiscalDocumentNaming.FileNameFor(record);
            var existing = await DocumentExtrasService.GetAttachmentsAsync(auth, XeroDocumentTypes.Endpoint(record.DocumentType), documentId);
            var hit = existing.FirstOrDefault(a => string.Equals(a.FileName, fileName, StringComparison.OrdinalIgnoreCase));
            return new FiscalAttachmentStatusModel
            {
                Attached = hit != null,
                FileName = hit?.FileName ?? fileName,
                IncludeOnline = hit?.IncludeOnline,
                AttachmentDisabled = !config.FiscalStampEnabled,
                PdfLayout = FiscalPdfLayouts.Normalise(config.FiscalPdfLayout)
            };
        }

        public async Task<FiscalAttachmentStatusModel> AttachNowForCurrentUserAsync(string documentType, Guid documentId)
        {
            var (config, auth, record) = await ResolveAsync(documentType, documentId, "This document has not been fiscalized yet.");
            var warning = await AttachStampedPdfAsync(config, auth, record);
            return new FiscalAttachmentStatusModel
            {
                Attached = warning == null || warning.StartsWith(FallbackPrefix, StringComparison.Ordinal),
                FileName = FiscalDocumentNaming.FileNameFor(record),
                IncludeOnline = warning == null ? config.AttachmentIncludeOnline : null,
                AttachmentDisabled = !config.FiscalStampEnabled,
                PdfLayout = FiscalPdfLayouts.Normalise(config.FiscalPdfLayout),
                Warning = warning
            };
        }

        private async Task<(ClientConfig Config, XeroAuthModel Auth, FiscalRecord Record)> ResolveAsync(string documentType, Guid documentId, string notFiscalized)
        {
            var config = await _currentUser.GetCurrentClientConfigAsync()
                ?? throw new FiscalizeXero.Api.Exceptions.UserFacingException("Xero is not connected for this company. Connect Xero first.");
            var auth = await _authBuilder.BuildAsync(config)
                ?? throw new FiscalizeXero.Api.Exceptions.UserFacingException("The Xero connection is not authorised any more. Reconnect Xero for this company, then try again.");
            var record = await _records.GetAsync(config.AppUserCompanyId, FiscalDocumentType.Key(documentId));
            if (record == null || !string.Equals(record.DocumentType, documentType, StringComparison.OrdinalIgnoreCase))
                throw new NotFiscalizedException(notFiscalized);
            return (config, auth, record);
        }

        public async Task<StampedPdf> BuildStampedPdfAsync(ClientConfig config, XeroAuthModel auth, FiscalRecord record)
        {
            if (string.IsNullOrWhiteSpace(record.QrCodeUrl))
                throw new FiscalizeXero.Api.Exceptions.UserFacingException("The fiscal record has no QR link, so the stamp cannot be drawn. Fiscalize the document again.");
            if (!Guid.TryParse(record.DocumentId, out var documentId))
                throw new FiscalizeXero.Api.Exceptions.UserFacingException("The fiscal record does not name a Xero document.");

            var pdf = await XeroDocumentTypes.GetPdfAsync(auth, record.DocumentType, documentId);

            var options = new StampOptions(
                Position: string.IsNullOrWhiteSpace(config.FiscalStampPosition) ? "bottom-right" : config.FiscalStampPosition,
                MarginTop: config.FiscalStampMarginTop,
                MarginBottom: config.FiscalStampMarginBottom,
                MarginLeft: config.FiscalStampMarginLeft,
                MarginRight: config.FiscalStampMarginRight,
                QrlUrl: record.QrCodeUrl,
                ReceiptNumber: record.ReceiptNumber?.ToString(),
                DayNumber: record.FiscalDayNumber?.ToString(),
                DeviceId: record.DeviceId.ToString(),
                VerificationCode: record.VerificationCode,
                GlobalNumber: record.GlobalNumber?.ToString(),
                FiscalizedAt: record.FiscalizedAt,
                IsInvalid: string.Equals(record.ValidationError, "Red", StringComparison.OrdinalIgnoreCase));

            return new StampedPdf(_stamper.ApplyFiscalStamp(pdf, options), FiscalDocumentNaming.FileNameFor(record));
        }

        public async Task<FiscalPdf> BuildChosenPdfAsync(ClientConfig config, XeroAuthModel auth, FiscalRecord record)
        {
            var choice = FiscalPdfLayouts.Normalise(config.FiscalPdfLayout);
            var fileName = FiscalDocumentNaming.FileNameFor(record);
            string? fallback = null;

            if (choice == FiscalPdfLayouts.FiscalizeLayout)
            {
                try
                {
                    var (bytes, kind) = await RenderLayoutAsync(config, auth, record, LayoutPapers.Normalise(config.LayoutPaperSize));
                    return new FiscalPdf(bytes, fileName, kind);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    fallback = $"the Fiscalize layout could not be used ({Short(ex)})";
                }
            }
            else if (choice == FiscalPdfLayouts.FiscalizeStandard)
            {
                try
                {
                    return new FiscalPdf(await PlatformPdfAsync(config, record), fileName, FiscalPdfKinds.Platform);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    fallback = $"the Fiscalize standard invoice could not be fetched ({Short(ex)})";
                }
            }

            if (fallback != null)
                _logger.LogWarning("{Type} {DocNo}: {Reason}; attaching Xero's PDF with the fiscal stamp instead",
                    XeroDocumentTypes.Label(record.DocumentType), record.DocumentNumber, fallback);

            var stamped = await BuildStampedPdfAsync(config, auth, record);
            return new FiscalPdf(stamped.Bytes, stamped.FileName, FiscalPdfKinds.Stamp, fallback);
        }

        /// <summary>Draws the document with the company's layout for Xero (else the standard design). Throws when there is none or it cannot be drawn.</summary>
        private async Task<(byte[] Bytes, string Kind)> RenderLayoutAsync(ClientConfig config, XeroAuthModel auth, FiscalRecord record, string paper)
        {
            if (!Guid.TryParse(record.DocumentId, out var documentId))
                throw new FiscalizeXero.Api.Exceptions.UserFacingException("The fiscal record does not name a Xero document.");
            if (string.IsNullOrWhiteSpace(record.QrCodeUrl))
                throw new FiscalizeXero.Api.Exceptions.UserFacingException("The fiscal record has no QR link. Fiscalize the document again.");

            var pick = await _layouts.GetAsync(config, record.DocumentType, paper, auth)
                ?? throw new FiscalizeXero.Api.Exceptions.UserFacingException($"no Fiscalize layout is available for {XeroDocumentTypes.Label(record.DocumentType)}s on {paper} yet. Check that the device is set up, then try again.");

            var document = await XeroDocumentTypes.GetAsync(auth, record.DocumentType, documentId)
                ?? throw new FiscalizeXero.Api.Exceptions.UserFacingException($"Xero has no {XeroDocumentTypes.Label(record.DocumentType)} with this id any more.");

            Contact? contact = null;
            ContactVatRecord? buyer = null;
            if (document.Contact?.ContactID is { } contactId && contactId != Guid.Empty)
            {
                try { contact = await ContactService.GetByIdAsync(auth, contactId); }
                catch (XeroApiException ex) { _logger.LogInformation(ex, "Contact {ContactId} could not be read for the layout; using the document's own copy", contactId); }
                var saved = await _contactVat.GetAsync(config.AppUserCompanyId, FiscalDocumentType.Key(contactId));
                buyer = BuyerResolver.Resolve(contact ?? document.Contact, saved);
            }

            IReadOnlyDictionary<string, LineTax>? taxes = null;
            if (!document.IsNoTax)
            {
                try { taxes = await _taxRates.GetLineTaxesAsync(auth); }
                catch (Exception ex) when (ex is XeroApiException or HttpRequestException or TaskCanceledException) { /* each line's rate is worked out from its own tax */ }
            }

            var fieldsUsed = pick.Package.FieldsUsed ?? new List<string>();
            string? onlineUrl = null;
            if (document is Invoice && fieldsUsed.Contains("OnlineInvoiceUrl", StringComparer.OrdinalIgnoreCase))
            {
                try { onlineUrl = await InvoiceService.GetOnlineUrlAsync(auth, documentId); }
                catch (XeroApiException) { /* no online invoice (online invoicing off): the field just does not print */ }
            }

            var source = new XeroLayoutSource
            {
                Document = document,
                Contact = contact,
                Buyer = buyer,
                TaxByType = taxes,
                Record = record,
                BaseInvoiceNumber = document is CreditNote credit ? CreditNoteOriginalInvoice.Resolve(credit)?.InvoiceNumber : null,
                OnlineInvoiceUrl = onlineUrl
            };
            var bytes = FiscalLayoutRenderer.Render(pick.Package, XeroLayoutMapper.Map(source, fieldsUsed));
            return (bytes, pick.IsStandardDesign ? FiscalPdfKinds.StandardDesign : FiscalPdfKinds.Layout);
        }

        /// <summary>The Fiscalize platform's own PDF of the fiscal receipt, fetched as the company's device.</summary>
        private async Task<byte[]> PlatformPdfAsync(ClientConfig config, FiscalRecord record)
        {
            if (!record.ReceiptNumber.HasValue)
                throw new FiscalizeXero.Api.Exceptions.UserFacingException("the document has no fiscal receipt on the platform");
            var device = await _device.FindDeviceAsync(config.AppUserCompanyId)
                ?? throw new FiscalizeXero.Api.Exceptions.UserFacingException("the company has no device with an API key");
            return record.DocumentType == FiscalDocumentType.CreditNote
                ? await _device.CallAsync(device, auth => FiscalizeCreditNoteService.GetPdfByDocNoAsync(auth, record.DocumentNumber))
                : await _device.CallAsync(device, auth => FiscalizeInvoiceService.GetPdfByDocNoAsync(auth, record.DocumentNumber));
        }

        /// <summary>Warnings that start with this still attached a PDF (the stamp, in place of the chosen one).</summary>
        public const string FallbackPrefix = "Attached Xero's PDF with the fiscal stamp";

        public async Task<string?> AttachStampedPdfAsync(ClientConfig config, XeroAuthModel auth, FiscalRecord record)
        {
            var label = XeroDocumentTypes.Label(record.DocumentType);
            try
            {
                var pdf = await BuildChosenPdfAsync(config, auth, record);

                // One fiscal PDF per document: Xero replaces an attachment of the same name, so a re-fiscalisation (or a change
                // of layout) overwrites ours and leaves the document's other attachments alone.
                var uploaded = await DocumentExtrasService.UploadAttachmentAsync(auth, XeroDocumentTypes.Endpoint(record.DocumentType),
                    Guid.Parse(record.DocumentId), pdf.FileName, pdf.Bytes, "application/pdf", includeOnline: config.AttachmentIncludeOnline);
                if (uploaded == null)
                    return $"Fiscalized, but Xero did not confirm the fiscal PDF attachment on {label} {record.DocumentNumber}. Open the {label} in Fiscalize and choose Attach now.";

                await _counter.CountAsync(config.Id, pdf.Kind, pdf.FallbackReason);
                _logger.LogInformation("Attached fiscal PDF {FileName} ({Kind}) to {Type} {DocumentId}", pdf.FileName, pdf.Kind, label, record.DocumentId);
                return pdf.FallbackReason == null
                    ? null
                    : $"{FallbackPrefix} to {label} {record.DocumentNumber}, because {pdf.FallbackReason}.";
            }
            catch (XeroApiException ex)
            {
                _logger.LogWarning(ex, "Could not attach the fiscal PDF to {Type} {DocumentId}: {Status}", label, record.DocumentId, ex.StatusCode);
                return $"Fiscalized, but the fiscal PDF could not be attached to {label} {record.DocumentNumber} in Xero ({ex.Message}). You can still download it from this page.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not produce the fiscal PDF for {Type} {DocumentId}", label, record.DocumentId);
                return $"Fiscalized, but the fiscal PDF for {label} {record.DocumentNumber} could not be produced. You can retry from this page.";
            }
        }

        private static string Short(Exception ex)
        {
            var message = ex.Message.Trim().TrimEnd('.');
            return message.Length <= 200 ? message : message[..200] + "…";
        }
    }

    public static class FiscalDocumentNaming
    {
        /// <summary>
        /// "INV-1001-fiscal.pdf": the same name every time for a document, so Xero replaces the earlier file; safe on
        /// every file system and recognisable in Xero's attachment list.
        /// </summary>
        public static string FileNameFor(FiscalRecord record)
        {
            // Letters and digits only; any other run becomes one dash, so "1001/A B" -> "1001-A-B" and "../x" -> "x".
            var number = System.Text.RegularExpressions.Regex.Replace(record.DocumentNumber ?? string.Empty, "[^A-Za-z0-9]+", "-").Trim('-');
            if (string.IsNullOrEmpty(number)) number = record.DocumentType == FiscalDocumentType.CreditNote ? "credit-note" : "invoice";
            if (number.Length > 80) number = number[..80].Trim('-'); // Xero invoice numbers can be very long
            return $"{number}-fiscal.pdf";
        }
    }
}
