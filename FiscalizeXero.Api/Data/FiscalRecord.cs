using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FiscalizeXero.Api.Data
{
    public class FiscalRecord
    {
        [Key]
        public long Id { get; set; }

        [ForeignKey("AppUserCompanyId")]
        public AppUserCompany AppUserCompany { get; set; } = null!;
        public int AppUserCompanyId { get; set; }

        /// <summary>Xero's id for the invoice or credit note: a GUID, stored as a lower-case string (<see cref="FiscalDocumentType.Key(Guid)"/>).</summary>
        [MaxLength(50)]
        public string DocumentId { get; set; } = string.Empty;

        /// <summary>One of <see cref="FiscalDocumentType"/>.</summary>
        public string DocumentType { get; set; } = string.Empty;

        /// <summary>Human-readable document number (e.g. INV-0001)</summary>
        public string DocumentNumber { get; set; } = string.Empty;

        public string? VerificationCode { get; set; }
        public string? QrCodeUrl { get; set; }
        public long? ReceiptNumber { get; set; }
        public long? GlobalNumber { get; set; }
        public int DeviceId { get; set; }
        public int? FiscalDayNumber { get; set; }

        /// <summary>"", "Gray", "Yellow", or "Red". Only "Red" means the receipt was rejected.</summary>
        public string ValidationError { get; set; } = string.Empty;

        public DateTime FiscalizedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>The two Xero documents that are fiscalised: sales invoices (ACCREC) and customer credit notes (ACCRECCREDIT).</summary>
    public static class FiscalDocumentType
    {
        public const string Invoice = "invoice";
        public const string CreditNote = "creditnote";

        /// <summary>The route segment the API uses for this type ("invoices", "creditnotes").</summary>
        public static string RouteSegment(string documentType) => documentType == CreditNote ? "creditnotes" : "invoices";

        /// <summary>The document type a route segment or query value names; anything unrecognised is an invoice.</summary>
        public static string FromRoute(string? route)
        {
            bool Is(params string[] names) => names.Any(n => string.Equals(route, n, StringComparison.OrdinalIgnoreCase));
            return Is("creditnotes", CreditNote, "credit-notes") ? CreditNote : Invoice;
        }

        /// <summary>A Xero document id as it is stored and compared everywhere: the GUID in lower case with dashes.</summary>
        public static string Key(Guid documentId) => documentId.ToString("D");

        /// <summary>The stored form of an id given as text, or null when it is not a GUID.</summary>
        public static string? Key(string? documentId) =>
            Guid.TryParse(documentId, out var id) && id != Guid.Empty ? Key(id) : null;
    }
}
