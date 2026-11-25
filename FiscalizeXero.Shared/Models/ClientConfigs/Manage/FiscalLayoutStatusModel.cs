namespace FiscalizeXero.Shared.Models.ClientConfigs.Manage
{
    /// <summary>The Fiscalize layout this company would use for one document type, as the platform last served it.</summary>
    public class FiscalLayoutStatusModel
    {
        /// <summary>"Invoice" or "CreditNote".</summary>
        public string DocumentType { get; set; } = "Invoice";
        public string PaperSize { get; set; } = "A4";
        /// <summary>False when the platform has nothing for it yet (or could not be reached and nothing is held).</summary>
        public bool Available { get; set; }
        /// <summary>True for Fiscalize's standard design (the company has no layout of its own live).</summary>
        public bool IsStandardDesign { get; set; }
        /// <summary>The company's layout version; 0 for the standard design.</summary>
        public int Version { get; set; }
        public DateTime? CheckedAt { get; set; }
    }
}
