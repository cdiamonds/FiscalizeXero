namespace FiscalizeXero.Shared.Models.TaxDetails
{
    public class ItemHsCodeModel
    {
        /// <summary>The Xero item code: what a line carries, and what the HS code is saved against.</summary>
        public string ItemCode { get; set; } = string.Empty;
        /// <summary>The Xero ItemID (a GUID), or null when Xero has no item with this code any more.</summary>
        public string? ItemId { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public bool? IsSold { get; set; }
        /// <summary>The code that will be used: what is saved in Fiscalize for the item code, else the default.</summary>
        public string? HsCode { get; set; }
        /// <summary>"Saved" (kept in Fiscalize for this item code) or "Default" (the company's or the system's default HS code).</summary>
        public string? HsCodeSource { get; set; }
        public string? Warning { get; set; }
    }

    public class SaveItemHsCodeModel
    {
        /// <summary>The Xero item code the HS code is for.</summary>
        public string ItemCode { get; set; } = string.Empty;
        /// <summary>The HS code (6 to 10 digits; dots, dashes and spaces are removed). Null or empty goes back to the default.</summary>
        public string? HsCode { get; set; }
    }
}
