using FiscalizeXero.Api.Data;
using FiscalizeXero.Shared.Models.Fiscalize;

namespace FiscalizeXero.Api.Services.Fiscalization
{
    /// <summary>
    /// Fiscalises Xero sales invoices and customer credit notes with ZIMRA through the Fiscalize platform. Document
    /// ids are Xero's GUIDs.
    /// </summary>
    public interface IFiscalizationService
    {
        // Controller convenience overloads — resolve everything from the current user's claims.
        Task<FiscalizeResultModel> FiscalizeInvoiceAsync(Guid invoiceId);
        /// <summary>
        /// <paramref name="baseDocNo"/> is the number of the fiscalised invoice the credit note reverses, when the user
        /// chose one; otherwise it comes from the credit note's allocations in Xero.
        /// </summary>
        Task<FiscalizeResultModel> FiscalizeCreditNoteAsync(Guid creditNoteId, string? baseDocNo = null);
        /// <summary>The invoice a credit note reverses as far as Xero's allocations say, plus the fiscalised invoices to choose from.</summary>
        Task<CreditNoteBaseDocumentModel> GetCreditNoteBaseDocumentAsync(Guid creditNoteId);

        // Explicit actions on an already-fiscalized invoice (current user).
        Task<FiscalizeResultModel> RefiscalizeInvoiceAsync(Guid invoiceId);
        Task<FiscalizeResultModel> UpdateInvoiceFiscalInfoAsync(Guid invoiceId, UpdateInvoiceFiscalInfoRequest update);
        Task<FiscalCancelResultModel> FiscalCancelInvoiceAsync(Guid invoiceId);

        Task<FiscalizeResultModel> RefiscalizeCreditNoteAsync(Guid creditNoteId, string? baseDocNo = null);

        // The Fiscalize platform's own PDF rendering of a fiscalized document.
        Task<byte[]> GetInvoiceFiscalizePdfAsync(Guid invoiceId);
        Task<byte[]> GetCreditNoteFiscalizePdfAsync(Guid creditNoteId);

        /// <summary>
        /// For the webhook worker: the caller supplies the connection and its company, so nothing depends on a
        /// signed-in user. Dispatches on a <see cref="FiscalDocumentType"/>. Throws
        /// <see cref="DocumentNotFiscalisableException"/> for a document that is not an approved sale.
        /// </summary>
        Task<FiscalizeResultModel> FiscalizeForConfigAsync(ClientConfig config, AppUserCompany appUserCompany, string documentType, Guid documentId);
    }
}
