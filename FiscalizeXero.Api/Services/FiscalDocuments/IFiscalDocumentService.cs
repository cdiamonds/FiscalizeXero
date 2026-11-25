using FiscalizeXero.Api.Data;
using XeroAuthModel = FiscalizeXero.Xero.XeroAuth;

namespace FiscalizeXero.Api.Services.FiscalDocuments
{
    public sealed record StampedPdf(byte[] Bytes, string FileName);

    /// <summary>What a fiscal PDF was made with, for counting and for the document page.</summary>
    public static class FiscalPdfKinds
    {
        /// <summary>Xero's own PDF with the ZIMRA stamp.</summary>
        public const string Stamp = "stamp";
        /// <summary>The company's own layout for Xero, designed on the platform.</summary>
        public const string Layout = "layout";
        /// <summary>Fiscalize's standard design (the company has no live layout of its own).</summary>
        public const string StandardDesign = "standardDesign";
        /// <summary>The Fiscalize platform's own standard invoice PDF.</summary>
        public const string Platform = "platform";
    }

    /// <summary>A fiscal PDF and how it was made. <see cref="FallbackReason"/> is set when the company's choice could not be used and the stamp was.</summary>
    public sealed record FiscalPdf(byte[] Bytes, string FileName, string Kind, string? FallbackReason = null);

    /// <summary>The document has no fiscal record yet, so there is nothing to stamp.</summary>
    public sealed class NotFiscalizedException : Exception
    {
        public NotFiscalizedException(string message) : base(message) { }
    }

    /// <summary>
    /// The customer-facing fiscal document. Three kinds, chosen per company (<see cref="ClientConfig.FiscalPdfLayout"/>):
    /// Xero's own invoice or credit note PDF with the ZIMRA stamp drawn on it; the company's Fiscalize layout for Xero (or
    /// Fiscalize's standard design when it has none live); or the Fiscalize platform's standard invoice. Whatever is chosen,
    /// a layout can never stop the fiscal PDF: when it cannot be made, the stamped Xero PDF is used instead.
    /// </summary>
    public interface IFiscalDocumentService
    {
        /// <summary>Xero's PDF with the fiscal stamp, for a document of the signed-in user's active company.</summary>
        Task<StampedPdf> GetStampedPdfForCurrentUserAsync(string documentType, Guid documentId);

        /// <summary>The document drawn with the company's Fiscalize layout (or the standard design) on the given paper. No fallback: a failure is reported.</summary>
        Task<StampedPdf> GetLayoutPdfForCurrentUserAsync(string documentType, Guid documentId, string? paperSize);

        /// <summary>Fetches Xero's PDF for the record's document and stamps it with the record's fiscal data.</summary>
        Task<StampedPdf> BuildStampedPdfAsync(ClientConfig config, XeroAuthModel auth, FiscalRecord record);

        /// <summary>The PDF the company chose to attach, falling back to the stamped Xero PDF. Throws only when even the stamp cannot be made.</summary>
        Task<FiscalPdf> BuildChosenPdfAsync(ClientConfig config, XeroAuthModel auth, FiscalRecord record);

        /// <summary>
        /// Attaches the chosen fiscal PDF to the Xero document under a fixed file name per document, so a re-fiscalisation
        /// replaces the earlier file. Never throws: returns a warning for the caller to surface, or null.
        /// </summary>
        Task<string?> AttachStampedPdfAsync(ClientConfig config, XeroAuthModel auth, FiscalRecord record);

        /// <summary>Whether the fiscal PDF is currently attached to the document in Xero (signed-in user's company).</summary>
        Task<FiscalizeXero.Shared.Models.Fiscalize.FiscalAttachmentStatusModel> GetAttachmentStatusForCurrentUserAsync(string documentType, Guid documentId);

        /// <summary>Attaches (or re-attaches) the fiscal PDF now, regardless of the setting, and reports the outcome.</summary>
        Task<FiscalizeXero.Shared.Models.Fiscalize.FiscalAttachmentStatusModel> AttachNowForCurrentUserAsync(string documentType, Guid documentId);
    }
}
