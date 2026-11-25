using FiscalizeXero.Api.Data;
using FiscalizeXero.Xero.Exceptions;
using FiscalizeXero.Xero.Models;
using FiscalizeXero.Xero.Services;
using System.Net;
using XeroAuthModel = FiscalizeXero.Xero.XeroAuth;

namespace FiscalizeXero.Api.Services.FiscalDocuments
{
    /// <summary>
    /// The one place that knows which Xero resource backs each <see cref="FiscalDocumentType"/>: the name used in
    /// the delivery log, the wording for messages, the endpoint for history notes and attachments, and the read,
    /// PDF and Reference calls.
    /// </summary>
    public static class XeroDocumentTypes
    {
        /// <summary>The name stored on <see cref="WebhookEvent.EntityName"/>.</summary>
        public static string EntityName(string documentType) => documentType == FiscalDocumentType.CreditNote ? "CreditNote" : "Invoice";

        /// <summary>The <see cref="FiscalDocumentType"/> for a stored entity name, or null when it is not one we fiscalise.</summary>
        public static string? FromEntityName(string? entityName)
        {
            if (string.Equals(entityName, "Invoice", StringComparison.OrdinalIgnoreCase)) return FiscalDocumentType.Invoice;
            if (string.Equals(entityName, "CreditNote", StringComparison.OrdinalIgnoreCase)) return FiscalDocumentType.CreditNote;
            return null;
        }

        /// <summary>Lower-case wording for messages: "invoice" or "credit note".</summary>
        public static string Label(string documentType) => documentType == FiscalDocumentType.CreditNote ? "credit note" : "invoice";

        /// <summary>Capitalised wording for the start of a sentence.</summary>
        public static string Title(string documentType) => documentType == FiscalDocumentType.CreditNote ? "Credit note" : "Invoice";

        /// <summary>Xero's endpoint for the type: "Invoices" or "CreditNotes" (history notes and attachments hang off it).</summary>
        public static string Endpoint(string documentType) =>
            documentType == FiscalDocumentType.CreditNote ? DocumentExtrasService.CreditNotes : DocumentExtrasService.Invoices;

        /// <summary>The document's PDF as Xero renders it, in the organisation's own branding theme.</summary>
        public static Task<byte[]> GetPdfAsync(XeroAuthModel auth, string documentType, Guid id) =>
            documentType == FiscalDocumentType.CreditNote ? CreditNoteService.GetPdfAsync(auth, id) : InvoiceService.GetPdfAsync(auth, id);

        /// <summary>Reads the document, or null when Xero has none with that id (Xero answers 404).</summary>
        public static async Task<SalesDocument?> GetAsync(XeroAuthModel auth, string documentType, Guid id)
        {
            try
            {
                return documentType == FiscalDocumentType.CreditNote
                    ? await CreditNoteService.GetByIdAsync(auth, id)
                    : await InvoiceService.GetByIdAsync(auth, id);
            }
            catch (XeroApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        /// <summary>Sets the document's Reference and nothing else.</summary>
        public static Task UpdateReferenceAsync(XeroAuthModel auth, SalesDocument document, string reference) =>
            document is CreditNote
                ? CreditNoteService.UpdateReferenceAsync(auth, document.DocumentId, reference)
                : InvoiceService.UpdateReferenceAsync(auth, document.DocumentId, reference);

        /// <summary>The fiscal document type of a loaded Xero document.</summary>
        public static string TypeOf(SalesDocument document) => document is CreditNote ? FiscalDocumentType.CreditNote : FiscalDocumentType.Invoice;

        /// <summary>The number printed on the document, or its id when it has none.</summary>
        public static string NumberOf(SalesDocument document) =>
            string.IsNullOrWhiteSpace(document.DocumentNumber) ? FiscalDocumentType.Key(document.DocumentId) : document.DocumentNumber!.Trim();
    }
}
