namespace FiscalizeXero.Shared.Models.Fiscalize
{
    /// <summary>The latest failed attempt to fiscalise a document, for the "Failed" pill and the document page.</summary>
    public class FiscalFailureModel
    {
        /// <summary>The Xero document id (a GUID, lower case).</summary>
        public string DocumentId { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        /// <summary>Buyer, HsCode, Device, OriginalSale, Platform, Xero, Network or Other.</summary>
        public string Category { get; set; } = "Other";
        public string Message { get; set; } = string.Empty;
        public List<string> Details { get; set; } = new();
        /// <summary>"Automatic" (webhook or reconciliation) or "Manual".</summary>
        public string Source { get; set; } = "Automatic";
        public int Attempts { get; set; }
        public DateTime FirstFailedAt { get; set; }
        public DateTime LastFailedAt { get; set; }
    }
}
