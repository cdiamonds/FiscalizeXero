namespace FiscalizeXero.Fiscalize.Models.PluginInstallations
{
    /// <summary>
    /// What the app reports about one connected Xero organisation to the platform's Connected apps
    /// (<c>PUT api/plugin-installations/devices/current</c>). Names, settings and counts only: never document or customer data.
    /// See Fiscalyze docs/PLUGIN_INSTALLATIONS.md.
    /// </summary>
    public class PluginInstallationReportModel
    {
        public Guid InstallationId { get; set; }
        public string PluginType { get; set; } = "";
        public string PluginVersion { get; set; } = "";
        public string? MachineName { get; set; }
        public string? OsVersion { get; set; }
        public string? ErpName { get; set; }
        public string? ErpVersion { get; set; }
        public string? ErpCompany { get; set; }
        public string? PaperSize { get; set; }
        public bool UsePlatformLayouts { get; set; }
        /// <summary>"cloud": this app runs on Fiscalize's servers, one report per connected organisation.</summary>
        public string Hosting { get; set; } = "cloud";
        /// <summary>"connected", "attention" or "disconnected".</summary>
        public string? ConnectionState { get; set; }
        public string? ConnectionMessage { get; set; }
        public List<PluginInstallationDetailModel> Details { get; set; } = new();
        public List<PluginInstallationLayoutModel> Layouts { get; set; } = new();
        /// <summary>Fiscal PDFs attached since the last successful report.</summary>
        public PluginInstallationPrintsModel? Prints { get; set; }
    }

    public class PluginInstallationDetailModel
    {
        public string Label { get; set; } = "";
        public string? Value { get; set; }
        /// <summary>"good", "warn" or null.</summary>
        public string? Tone { get; set; }

        public PluginInstallationDetailModel() { }
        public PluginInstallationDetailModel(string label, string? value, string? tone = null) { Label = label; Value = value; Tone = tone; }
    }

    public class PluginInstallationLayoutModel
    {
        /// <summary>"Invoice" or "CreditNote".</summary>
        public string DocumentType { get; set; } = "";
        public string PaperSize { get; set; } = "";
        /// <summary>0 for the Fiscalize standard design, otherwise the company's layout version.</summary>
        public int Version { get; set; }
        public string? Hash { get; set; }
    }

    public class PluginInstallationPrintsModel
    {
        public int WithLayout { get; set; }
        public int WithStandard { get; set; }
        public int Stamped { get; set; }
        public int Fallbacks { get; set; }
        public DateTime? LastFallbackAt { get; set; }
        public string? LastFallbackReason { get; set; }
    }

    public class PluginInstallationReceiptModel
    {
        public Guid InstallationId { get; set; }
        public DateTime FirstSeenAt { get; set; }
        public DateTime LastSeenAt { get; set; }
    }
}
