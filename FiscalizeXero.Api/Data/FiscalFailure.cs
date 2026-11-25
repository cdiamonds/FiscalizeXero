using System.ComponentModel.DataAnnotations;

namespace FiscalizeXero.Api.Data
{
    /// <summary>
    /// The latest failed attempt to fiscalise one Xero document, whatever started it (a Xero webhook, a
    /// reconciliation run or someone clicking Fiscalize). One row per document; a later success deletes it. This is
    /// what the "Failed" pill on the document lists and the error panel on the document page show.
    /// </summary>
    public class FiscalFailure
    {
        [Key]
        public long Id { get; set; }

        public int AppUserCompanyId { get; set; }

        [StringLength(50)]
        public string DocumentId { get; set; } = string.Empty;

        /// <summary>One of <see cref="FiscalDocumentType"/>.</summary>
        [StringLength(20)]
        public string DocumentType { get; set; } = string.Empty;

        /// <summary>One of <see cref="FiscalFailureCategory"/>: what kind of problem it was, so the UI can say how to fix it.</summary>
        [StringLength(30)]
        public string Category { get; set; } = FiscalFailureCategory.Other;

        /// <summary>The full, readable error.</summary>
        [StringLength(2000)]
        public string Message { get; set; } = string.Empty;

        /// <summary>The individual problems (missing fields, platform validation errors), newline-separated.</summary>
        [StringLength(4000)]
        public string? Details { get; set; }

        /// <summary>"Automatic" (webhook or reconciliation) or "Manual" (someone clicked Fiscalize).</summary>
        [StringLength(20)]
        public string Source { get; set; } = FiscalFailureSource.Automatic;

        /// <summary>Failed attempts since the last success.</summary>
        public int Attempts { get; set; } = 1;

        public DateTime FirstFailedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastFailedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the retry worker should try this document again (UTC); null when it shouldn't (it needs a person, or
        /// the attempts are used up). Set from the kind of failure by <c>FailureRetryPolicy</c>.
        /// </summary>
        public DateTime? NextRetryAt { get; set; }
    }

    public static class FiscalFailureSource
    {
        public const string Automatic = "Automatic";
        public const string Manual = "Manual";
    }

    public static class FiscalFailureCategory
    {
        /// <summary>Customer TIN/VAT invalid, or buyer details missing in Strict mode.</summary>
        public const string Buyer = "Buyer";
        /// <summary>An item has no HS code and there is no default.</summary>
        public const string HsCode = "HsCode";
        /// <summary>No ZIMRA device, or the device has no API key.</summary>
        public const string Device = "Device";
        /// <summary>A credit note's original invoice is missing or not fiscalised.</summary>
        public const string OriginalSale = "OriginalSale";
        /// <summary>The Fiscalize platform or FDMS rejected the document.</summary>
        public const string Platform = "Platform";
        /// <summary>Xero could not be read (token, permissions, rate limit, outage).</summary>
        public const string Xero = "Xero";
        /// <summary>A network timeout or outage; usually clears on the next try.</summary>
        public const string Network = "Network";
        public const string Other = "Other";
    }
}
