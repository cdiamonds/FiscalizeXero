namespace FiscalizeXero.Shared.Models.ClientConfigs.Manage
{
    public class FiscalizationSettingsModel
    {
        public bool AutoFiscalizeInvoices { get; set; } = true;
        public bool AutoFiscalizeCreditNotes { get; set; } = true;

        /// <summary>Add a note with the fiscal result to the document's "History and notes" in Xero.</summary>
        public bool WritebackHistoryNote { get; set; } = true;
        /// <summary>Attach the stamped fiscal PDF to the Xero document.</summary>
        public bool FiscalStampEnabled { get; set; } = true;
        /// <summary>Show the attached fiscal PDF to the customer on Xero's online invoice.</summary>
        public bool AttachmentIncludeOnline { get; set; } = true;
        /// <summary>Append a short fiscal marker to the document's Reference in Xero.</summary>
        public bool WritebackReference { get; set; } = false;

        /// <summary>
        /// The PDF attached in Xero: "XeroStamp" (Xero's own PDF with the fiscal stamp), "FiscalizeLayout" (the company's layout
        /// designed on the Fiscalize platform, else Fiscalize's standard design) or "FiscalizeStandard" (the platform's standard invoice).
        /// </summary>
        public string FiscalPdfLayout { get; set; } = "XeroStamp";
        /// <summary>Paper for the Fiscalize layout: "A4", "80mm" or "58mm".</summary>
        public string LayoutPaperSize { get; set; } = "A4";
        /// <summary>Where the company designs its layouts on the Fiscalize platform.</summary>
        public string? LayoutDesignerUrl { get; set; }

        public string FiscalStampPosition { get; set; } = "bottom-right";
        public int FiscalStampMarginTop { get; set; } = 15;
        public int FiscalStampMarginBottom { get; set; } = 15;
        public int FiscalStampMarginLeft { get; set; } = 15;
        public int FiscalStampMarginRight { get; set; } = 15;
        /// <summary>The HS code used for items that have none, and for lines with no item code.</summary>
        public string? DefaultHsCode { get; set; }
        /// <summary>Strict (default) or Lenient buyer-detail checks for customers with a TIN.</summary>
        public FiscalizeXero.Shared.Models.Fiscalize.FiscalizationValidationMode FiscalizationValidationMode { get; set; }
            = FiscalizeXero.Shared.Models.Fiscalize.FiscalizationValidationMode.Strict;
    }
}
