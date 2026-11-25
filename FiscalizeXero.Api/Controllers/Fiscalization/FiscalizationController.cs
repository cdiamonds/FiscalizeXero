using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Filters;
using FiscalizeXero.Api.Repositories;
using FiscalizeXero.Api.Services.CurrentUser;
using FiscalizeXero.Api.Services.FiscalDocuments;
using FiscalizeXero.Api.Services.Fiscalization;
using FiscalizeXero.Fiscalize.Exceptions;
using FiscalizeXero.Shared.Models;
using FiscalizeXero.Shared.Models.Fiscalize;
using FiscalizeXero.Xero.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FiscalizeXero.Api.Controllers.Fiscalization
{
    /// <summary>
    /// Fiscalisation of Xero sales invoices (<c>invoices/{id}</c>) and customer credit notes (<c>creditnotes/{id}</c>).
    /// Ids are Xero's GUIDs.
    /// </summary>
    [Route("api/fiscalization")]
    [ApiController]
    [Authorize]
    [CompanyScoped]
    public class FiscalizationController : ControllerBase
    {
        private readonly IFiscalizationService _fiscalizationService;
        private readonly IFiscalDocumentService _documents;
        private readonly IFiscalRecordRepository _records;
        private readonly ICurrentUserService _currentUser;
        private readonly IFiscalFailureService _failures;
        private readonly ILogger<FiscalizationController> _logger;

        public FiscalizationController(
            IFiscalizationService fiscalizationService,
            IFiscalDocumentService documents,
            IFiscalRecordRepository records,
            ICurrentUserService currentUser,
            IFiscalFailureService failures,
            ILogger<FiscalizationController> logger)
        {
            _failures = failures;
            _fiscalizationService = fiscalizationService;
            _documents = documents;
            _records = records;
            _currentUser = currentUser;
            _logger = logger;
        }

        // ===== Reading fiscal state (no call to Xero) =====

        /// <summary>The stored fiscal record for an invoice, or 404 when it has not been fiscalized.</summary>
        [HttpGet("invoices/{id:guid}/record")]
        [ProducesResponseType(typeof(FiscalizeResultModel), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        public Task<ActionResult> GetInvoiceRecord(Guid id) => RecordAsync(FiscalDocumentType.Invoice, id);

        /// <summary>The stored fiscal record for a credit note, or 404 when it has not been fiscalized.</summary>
        [HttpGet("creditnotes/{id:guid}/record")]
        [ProducesResponseType(typeof(FiscalizeResultModel), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        public Task<ActionResult> GetCreditNoteRecord(Guid id) => RecordAsync(FiscalDocumentType.CreditNote, id);

        /// <summary>
        /// Fiscal records for several documents at once (for list pages). <paramref name="type"/> is "invoices" or
        /// "creditnotes"; <paramref name="ids"/> is comma-separated Xero ids. Documents without a record are simply
        /// absent from the answer.
        /// </summary>
        [HttpGet("records")]
        [ProducesResponseType(typeof(List<FiscalizeResultModel>), StatusCodes.Status200OK)]
        public async Task<ActionResult> GetRecords([FromQuery] string type, [FromQuery] string ids)
        {
            var docType = FiscalDocumentType.FromRoute(type);
            var idList = ParseIds(ids);
            if (idList.Count == 0) return Ok(new List<FiscalizeResultModel>());

            var config = await _currentUser.GetCurrentClientConfigAsync();
            if (config == null) return Ok(new List<FiscalizeResultModel>());

            var records = await _records.GetManyAsync(config.AppUserCompanyId, idList);
            return Ok(records.Where(r => r.DocumentType == docType).Select(FiscalRecordMapping.ToResult).ToList());
        }

        /// <summary>
        /// The QR code for a fiscal verification link, as a PNG data URI. Drawn by the API with the same library as the
        /// stamped PDF, so the screen never depends on an outside QR service (blocked by ad blockers, firewalls or outages).
        /// </summary>
        [HttpGet("qr")]
        [ProducesResponseType(typeof(QrImageModel), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status400BadRequest)]
        public ActionResult<QrImageModel> GetQr([FromQuery] string? data)
        {
            if (!FiscalizeXero.Api.Services.PdfStamp.QrImage.IsEncodable(data))
                return BadRequest(new ErrorModel { Code = 400, Message = "A fiscal verification link (http or https, up to 2048 characters) is required." });
            // The same link always gives the same image, so the browser may keep it for a day.
            Response.Headers.CacheControl = "private, max-age=86400";
            return Ok(new QrImageModel { DataUri = FiscalizeXero.Api.Services.PdfStamp.QrImage.PngDataUri(data!) });
        }

        /// <summary>
        /// The latest failed fiscalisation for several documents at once (for list pages). Documents that never
        /// failed, or have fiscalised since, are absent from the answer.
        /// </summary>
        [HttpGet("failures")]
        [ProducesResponseType(typeof(List<FiscalFailureModel>), StatusCodes.Status200OK)]
        public async Task<ActionResult> GetFailures([FromQuery] string type, [FromQuery] string ids)
        {
            var idList = ParseIds(ids);
            if (idList.Count == 0) return Ok(new List<FiscalFailureModel>());
            var config = await _currentUser.GetCurrentClientConfigAsync();
            if (config == null) return Ok(new List<FiscalFailureModel>());
            return Ok(await _failures.GetManyAsync(config.AppUserCompanyId, FiscalDocumentType.FromRoute(type), idList));
        }

        /// <summary>Why one document last failed to fiscalise; 204 when it hasn't failed (or has fiscalised since).</summary>
        [HttpGet("{documentType:regex(^invoices$|^creditnotes$)}/{id:guid}/failure")]
        [ProducesResponseType(typeof(FiscalFailureModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult> GetFailure(string documentType, Guid id)
        {
            var config = await _currentUser.GetCurrentClientConfigAsync();
            if (config == null) return NoContent();
            var failure = await _failures.GetAsync(config.AppUserCompanyId, FiscalDocumentType.FromRoute(documentType), FiscalDocumentType.Key(id));
            return failure == null ? NoContent() : Ok(failure);
        }

        /// <summary>Up to 200 Xero ids out of a comma-separated list, in their stored form; anything that is not a GUID is dropped.</summary>
        internal static List<string> ParseIds(string? ids) =>
            (ids ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(FiscalDocumentType.Key).Where(k => k != null).Cast<string>().Distinct().Take(200).ToList();

        private async Task<ActionResult> RecordAsync(string documentType, Guid id)
        {
            var config = await _currentUser.GetCurrentClientConfigAsync();
            if (config == null)
                return NotFound(new ErrorModel { Code = 404, Message = "Xero is not connected for this company. Connect Xero first." });
            var record = await _records.GetAsync(config.AppUserCompanyId, FiscalDocumentType.Key(id));
            if (record == null || record.DocumentType != documentType)
                return NotFound(new ErrorModel { Code = 404, Message = "This document has not been fiscalized." });
            return Ok(FiscalRecordMapping.ToResult(record));
        }

        // ===== Invoices =====

        /// <summary>Fiscalises an approved sales invoice.</summary>
        [HttpPost("invoices/{id:guid}")]
        [ProducesResponseType(typeof(FiscalizeResultModel), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status400BadRequest)]
        public Task<ActionResult> FiscalizeInvoiceAsync(Guid id)
            => GuardAsync(async () => Ok(await _fiscalizationService.FiscalizeInvoiceAsync(id)), $"fiscalising invoice {id}");

        /// <summary>Resubmits a fiscalized invoice: credits the original FDMS record and issues a fresh one.</summary>
        [HttpPost("invoices/{id:guid}/refiscalize")]
        [ProducesResponseType(typeof(FiscalizeResultModel), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status400BadRequest)]
        public Task<ActionResult> RefiscalizeInvoiceAsync(Guid id)
            => GuardAsync(async () => Ok(await _fiscalizationService.RefiscalizeInvoiceAsync(id)), $"re-fiscalising invoice {id}");

        /// <summary>Applies corrected buyer details (or removes the buyer) and re-fiscalizes the invoice.</summary>
        [HttpPut("invoices/{id:guid}/fiscal-info")]
        [ProducesResponseType(typeof(FiscalizeResultModel), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status400BadRequest)]
        public Task<ActionResult> UpdateInvoiceFiscalInfoAsync(Guid id, [FromBody] UpdateInvoiceFiscalInfoRequest request)
            => GuardAsync(async () => Ok(await _fiscalizationService.UpdateInvoiceFiscalInfoAsync(id, request)), $"updating fiscal details of invoice {id}");

        /// <summary>Reverses a fiscalized invoice at FDMS and removes its fiscal record here.</summary>
        [HttpPost("invoices/{id:guid}/fiscal-cancel")]
        [ProducesResponseType(typeof(FiscalCancelResultModel), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status400BadRequest)]
        public Task<ActionResult> FiscalCancelInvoiceAsync(Guid id)
            => GuardAsync(async () => Ok(await _fiscalizationService.FiscalCancelInvoiceAsync(id)), $"fiscally cancelling invoice {id}");

        /// <summary>Xero's invoice PDF with the ZIMRA fiscal stamp (QR code, receipt and verification details).</summary>
        [HttpGet("invoices/{id:guid}/pdf")]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        public Task<ActionResult> GetInvoiceStampedPdf(Guid id) => StampedPdfAsync(FiscalDocumentType.Invoice, id);

        /// <summary>The Fiscalize platform's own PDF of the fiscal invoice.</summary>
        [HttpGet("invoices/{id:guid}/fiscalize-pdf")]
        [Produces("application/pdf")]
        public Task<ActionResult> InvoiceFiscalizePdfAsync(Guid id)
            => GuardAsync(async () => File(await _fiscalizationService.GetInvoiceFiscalizePdfAsync(id), "application/pdf", $"invoice-{id}-fiscalize.pdf"), $"downloading the Fiscalize PDF of invoice {id}");

        // ===== Credit notes =====

        /// <summary>
        /// Fiscalises an approved customer credit note. ZIMRA credits a specific receipt, so the original invoice is
        /// needed: the <c>baseDocNo</c> chosen by the user, else the invoice the credit is allocated to in Xero.
        /// </summary>
        [HttpPost("creditnotes/{id:guid}")]
        [ProducesResponseType(typeof(FiscalizeResultModel), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status400BadRequest)]
        public Task<ActionResult> FiscalizeCreditNoteAsync(Guid id, [FromBody] FiscalizeCreditNoteRequest? request = null)
            => GuardAsync(async () => Ok(await _fiscalizationService.FiscalizeCreditNoteAsync(id, request?.BaseDocNo)), $"fiscalising credit note {id}");

        /// <summary>Resubmits a fiscalized credit note: credits the original fiscal credit note and issues a fresh one.</summary>
        [HttpPost("creditnotes/{id:guid}/refiscalize")]
        [ProducesResponseType(typeof(FiscalizeResultModel), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status400BadRequest)]
        public Task<ActionResult> RefiscalizeCreditNoteAsync(Guid id, [FromBody] FiscalizeCreditNoteRequest? request = null)
            => GuardAsync(async () => Ok(await _fiscalizationService.RefiscalizeCreditNoteAsync(id, request?.BaseDocNo)), $"re-fiscalising credit note {id}");

        /// <summary>Xero's credit note PDF with the ZIMRA fiscal stamp.</summary>
        [HttpGet("creditnotes/{id:guid}/pdf")]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        public Task<ActionResult> GetCreditNoteStampedPdf(Guid id) => StampedPdfAsync(FiscalDocumentType.CreditNote, id);

        /// <summary>The Fiscalize platform's own PDF of the fiscal credit note.</summary>
        [HttpGet("creditnotes/{id:guid}/fiscalize-pdf")]
        [Produces("application/pdf")]
        public Task<ActionResult> CreditNoteFiscalizePdfAsync(Guid id)
            => GuardAsync(async () => File(await _fiscalizationService.GetCreditNoteFiscalizePdfAsync(id), "application/pdf", $"creditnote-{id}-fiscalize.pdf"), $"downloading the Fiscalize PDF of credit note {id}");

        /// <summary>The invoice a credit note reverses, as far as Xero's allocations say, plus the fiscalised invoices to choose from.</summary>
        [HttpGet("creditnotes/{id:guid}/base-document")]
        [ProducesResponseType(typeof(CreditNoteBaseDocumentModel), StatusCodes.Status200OK)]
        public Task<ActionResult> CreditNoteBaseDocumentAsync(Guid id)
            => GuardAsync(async () => Ok(await _fiscalizationService.GetCreditNoteBaseDocumentAsync(id)), $"resolving the original invoice of credit note {id}");

        // ===== Stamped PDF attachment (either type) =====

        /// <summary>Whether the stamped fiscal PDF is attached to the Xero document.</summary>
        [HttpGet("{documentType:regex(^invoices$|^creditnotes$)}/{id:guid}/attachment")]
        [ProducesResponseType(typeof(FiscalAttachmentStatusModel), StatusCodes.Status200OK)]
        public Task<ActionResult> AttachmentStatusAsync(string documentType, Guid id)
            => GuardAsync(async () => Ok(await _documents.GetAttachmentStatusForCurrentUserAsync(FiscalDocumentType.FromRoute(documentType), id)), $"checking the fiscal PDF attachment of {documentType} {id}");

        /// <summary>Attaches (or re-attaches) the stamped fiscal PDF to the Xero document now.</summary>
        [HttpPost("{documentType:regex(^invoices$|^creditnotes$)}/{id:guid}/attachment")]
        [ProducesResponseType(typeof(FiscalAttachmentStatusModel), StatusCodes.Status200OK)]
        public Task<ActionResult> AttachNowAsync(string documentType, Guid id)
            => GuardAsync(async () => Ok(await _documents.AttachNowForCurrentUserAsync(FiscalDocumentType.FromRoute(documentType), id)), $"attaching the fiscal PDF to {documentType} {id}");

        /// <summary>
        /// The document drawn with the company's Fiscalize layout for Xero (Fiscalize's standard design when it has none live),
        /// on the company's paper or the one asked for.
        /// </summary>
        [HttpGet("{documentType:regex(^invoices$|^creditnotes$)}/{id:guid}/layout-pdf")]
        [Produces("application/pdf")]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status400BadRequest)]
        public Task<ActionResult> LayoutPdfAsync(string documentType, Guid id, [FromQuery] string? paper = null)
            => GuardAsync(async () =>
            {
                var pdf = await _documents.GetLayoutPdfForCurrentUserAsync(FiscalDocumentType.FromRoute(documentType), id, paper);
                return File(pdf.Bytes, "application/pdf", pdf.FileName);
            }, $"drawing the Fiscalize layout of {documentType} {id}");

        private Task<ActionResult> StampedPdfAsync(string documentType, Guid id)
            => GuardAsync(async () =>
            {
                var pdf = await _documents.GetStampedPdfForCurrentUserAsync(documentType, id);
                return File(pdf.Bytes, "application/pdf", pdf.FileName);
            }, $"producing the stamped PDF of {XeroDocumentTypes.Label(documentType)} {id}");

        /// <summary>The platform's validation answer lists the failing fields; show them, not just the title.</summary>
        private static ErrorModel PlatformError(FiscalizeApiException ex)
        {
            var details = ex.Errors.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct().ToList();
            var message = details.Count > 0
                ? $"Fiscalize platform: {ex.Message} {string.Join(" ", details.Select(d => d.TrimEnd('.') + "."))}"
                : $"Fiscalize platform: {ex.Message}";
            return new ErrorModel { Code = (int)ex.StatusCode, Message = message, Errors = details.Count > 0 ? details : null };
        }

        private async Task<ActionResult> GuardAsync(Func<Task<ActionResult>> action, string what)
        {
            try
            {
                return await action();
            }
            catch (NotFiscalizedException ex)
            {
                return NotFound(new ErrorModel { Code = 404, Message = ex.Message });
            }
            catch (FiscalizeXero.Api.Exceptions.UserFacingException ex)
            {
                // Includes DocumentNotFiscalisableException (a draft, a purchase bill, a voided document) and buyer problems.
                _logger.LogWarning(ex, "Bad request {What}", what);
                return BadRequest(new ErrorModel { Code = 400, Message = ex.Message, Errors = (ex as BuyerDetailsException)?.Details.ToList() });
            }
            catch (InvalidOperationException ex)
            {
                // Framework or database text: logged in full, never shown.
                _logger.LogError(ex, "Unexpected error {What}", what);
                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorModel { Code = 500, Message = "Something went wrong on our side. Try again; if it keeps happening, contact support." });
            }
            catch (XeroApiException ex)
            {
                _logger.LogWarning(ex, "Xero API error {What}", what);
                return StatusCode(UpstreamStatus(ex.StatusCode), XeroApiExceptionFilter.ToError(ex));
            }
            catch (FiscalizeApiException ex)
            {
                _logger.LogWarning(ex, "Fiscalize platform error {What}", what);
                return StatusCode(UpstreamStatus(ex.StatusCode), PlatformError(ex));
            }
        }

        /// <summary>
        /// A 401/403 from Xero or the Fiscalize platform (e.g. a device API key refused) is not this app's sign-in
        /// failing, so it goes out as 424 Failed Dependency: the browser shows the error instead of signing the user out.
        /// </summary>
        internal static int UpstreamStatus(System.Net.HttpStatusCode status) => XeroApiExceptionFilter.UpstreamStatus(status);
    }
}
