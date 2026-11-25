using System.ComponentModel.DataAnnotations;

namespace FiscalizeXero.Api.Data
{
    /// <summary>The values <see cref="ClientConfig.FiscalPdfLayout"/> takes.</summary>
    public static class FiscalPdfLayouts
    {
        /// <summary>Xero's own PDF (the organisation's branding theme) with the ZIMRA stamp drawn on it.</summary>
        public const string XeroStamp = "XeroStamp";
        /// <summary>The company's layout for Xero designed on the Fiscalize platform, else Fiscalize's standard design.</summary>
        public const string FiscalizeLayout = "FiscalizeLayout";
        /// <summary>The Fiscalize platform's own standard invoice PDF for the fiscal receipt.</summary>
        public const string FiscalizeStandard = "FiscalizeStandard";

        public static readonly string[] All = { XeroStamp, FiscalizeLayout, FiscalizeStandard };

        public static string Normalise(string? value) =>
            All.FirstOrDefault(a => string.Equals(a, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? XeroStamp;

        public static string Label(string? value) => Normalise(value) switch
        {
            FiscalizeLayout => "Fiscalize layout",
            FiscalizeStandard => "Fiscalize standard invoice",
            _ => "Xero PDF with fiscal stamp"
        };
    }

    public static class LayoutPapers
    {
        public static readonly string[] All = { "A4", "80mm", "58mm" };
        public static string Normalise(string? value) =>
            All.FirstOrDefault(p => string.Equals(p, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "A4";
    }

    /// <summary>
    /// The last good layout package the platform served for a company, document type and paper, so a fiscal PDF never waits
    /// on the platform and a restart does not download every layout again. Checked against the platform (with its hash, so a
    /// current copy costs one small request) when it is older than a few minutes. Nothing about any document is stored.
    /// </summary>
    public class LayoutPackageCache
    {
        [Key]
        public long Id { get; set; }

        /// <summary>The company's anchor row (layouts belong to the Fiscalize company).</summary>
        public int AppUserCompanyId { get; set; }

        /// <summary>"Invoice" or "CreditNote", as the platform names them.</summary>
        [StringLength(20)]
        public string DocumentType { get; set; } = "Invoice";

        [StringLength(10)]
        public string PaperSize { get; set; } = "A4";

        public long PluginLayoutId { get; set; }
        /// <summary>The company's layout version; 0 for the Fiscalize standard design.</summary>
        public int Version { get; set; }
        [StringLength(100)]
        public string Hash { get; set; } = "";
        public bool IsStandardDesign { get; set; }

        /// <summary>The package JSON as the platform sent it (design, logo, letterhead, checksum). Verified again on every use.</summary>
        public string PackageJson { get; set; } = "";

        public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
