namespace FiscalizeXero.Shared.Models.ClientConfigs.Manage
{
    public class UpdateFiscalizationSettingsModel
    {
        public bool AutoFiscalizeInvoices { get; set; }
        public bool AutoFiscalizeCreditNotes { get; set; }

        /// <summary>Add a note with the fiscal result to the document's "History and notes" in Xero. Left out: the current setting is kept.</summary>
        public bool? WritebackHistoryNote { get; set; }
        /// <summary>Attach the stamped fiscal PDF to the Xero document.</summary>
        public bool FiscalStampEnabled { get; set; }
        /// <summary>Show the attached fiscal PDF to the customer on Xero's online invoice. Left out: the current setting is kept.</summary>
        public bool? AttachmentIncludeOnline { get; set; }
        /// <summary>Append a short fiscal marker to the document's Reference in Xero. Left out: the current setting is kept.</summary>
        public bool? WritebackReference { get; set; }

        /// <summary>"XeroStamp", "FiscalizeLayout" or "FiscalizeStandard". Left out: the current setting is kept.</summary>
        public string? FiscalPdfLayout { get; set; }
        /// <summary>"A4", "80mm" or "58mm". Left out: the current setting is kept.</summary>
        public string? LayoutPaperSize { get; set; }

        public string FiscalStampPosition { get; set; } = "bottom-right";
        public int FiscalStampMarginTop { get; set; } = 15;
        public int FiscalStampMarginBottom { get; set; } = 15;
        public int FiscalStampMarginLeft { get; set; } = 15;
        public int FiscalStampMarginRight { get; set; } = 15;
        /// <summary>The HS code used for items that have none. Null or empty goes back to the system default.</summary>
        public string? DefaultHsCode { get; set; }
        /// <summary>Strict or Lenient buyer-detail checks for customers with a TIN. Left out: the current setting is kept.</summary>
        public FiscalizeXero.Shared.Models.Fiscalize.FiscalizationValidationMode? FiscalizationValidationMode { get; set; }
    }
}
