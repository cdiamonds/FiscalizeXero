using System.Globalization;
using System.Reflection;
using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.Layouts;
using FiscalizeXero.Fiscalize.Models.PluginInstallations;
using FiscalizeXero.Shared.Models.Fiscalize;

namespace FiscalizeXero.Api.Services.ConnectedApps
{
    /// <summary>Activity figures the report summarises (counts and times only).</summary>
    public sealed record ConnectedAppActivity(DateTime? LastWebhookAt, int DocumentsWaiting, int FiscalisedLast30Days);

    /// <summary>
    /// What one Xero connection tells the platform's Connected apps page about itself: the organisation, the health of the
    /// connection and the company's settings. Names, settings and counts only, never document or customer data. Pure, so
    /// the exact report is unit tested (and its JSON pinned as a fixture in the platform's tests).
    /// </summary>
    public static class ConnectedAppReport
    {
        public const string PluginType = XeroLayoutMapper.PluginType;

        public static string AppVersion { get; } =
            (typeof(ConnectedAppReport).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
             ?? typeof(ConnectedAppReport).Assembly.GetName().Version?.ToString() ?? "1.0.0").Split('+')[0];

        /// <summary>"connected", "attention" or "disconnected", with one sentence for the company.</summary>
        public static (string State, string Message) Connection(ClientConfig config, bool disconnected = false)
        {
            if (disconnected || config.TenantId == null)
                return ("disconnected", "Xero was disconnected from this Fiscalize company. Connect it again in the Fiscalize Xero app to resume.");
            if (!config.IsLive)
                return ("attention", "Xero needs to be reconnected: its sign-in has expired or was withdrawn. Open the Fiscalize Xero app and choose Reconnect.");
            if (!string.IsNullOrWhiteSpace(config.LastSyncError))
                return ("attention", Clip($"The last check for new Xero documents failed: {config.LastSyncError.Trim()}", 300));
            return ("connected", $"Fiscalising approved sales invoices and credit notes from {config.TenantName ?? "the Xero organisation"}.");
        }

        public static PluginInstallationReportModel Build(ClientConfig config, IEnumerable<LayoutPackageCache> held, ConnectedAppActivity activity, bool disconnected = false)
        {
            var (state, message) = Connection(config, disconnected);
            var choice = FiscalPdfLayouts.Normalise(config.FiscalPdfLayout);
            var paper = LayoutPapers.Normalise(config.LayoutPaperSize);

            var details = new List<PluginInstallationDetailModel>
            {
                new("Organisation", config.TenantName),
                new("Organisation ID", config.TenantId?.ToString("D")),
            };
            if (!string.IsNullOrWhiteSpace(config.OrganisationShortCode)) details.Add(new("Short code", config.OrganisationShortCode));
            if (!string.IsNullOrWhiteSpace(config.OrganisationClass) || config.OrganisationIsDemo)
                details.Add(new("Xero plan", config.OrganisationIsDemo ? "Demo company" : Title(config.OrganisationClass)));
            if (!string.IsNullOrWhiteSpace(config.OrganisationBaseCurrency)) details.Add(new("Base currency", config.OrganisationBaseCurrency));

            details.Add(new("Automatic fiscalisation", Auto(config), config.AutoFiscalizeInvoices || config.AutoFiscalizeCreditNotes ? null : "warn"));
            details.Add(new("PDF attached in Xero", config.FiscalStampEnabled
                ? FiscalPdfLayouts.Label(choice) + (choice == FiscalPdfLayouts.FiscalizeLayout ? $" ({paper})" : "")
                : "Not attached"));
            if (config.FiscalStampEnabled) details.Add(new("Shown on the online invoice", config.AttachmentIncludeOnline ? "Yes" : "No"));
            details.Add(new("Written back to Xero", WrittenBack(config)));
            details.Add(new("Buyer checks", config.FiscalizationValidationMode == FiscalizationValidationMode.Lenient ? "Lenient" : "Strict"));
            details.Add(new("Last check for new documents", When(config.LastSyncRunAt) ?? "Not yet",
                string.IsNullOrWhiteSpace(config.LastSyncError) ? null : "warn"));
            details.Add(new("Last Xero webhook", When(activity.LastWebhookAt) ?? "None received yet"));
            details.Add(new("Fiscalised in the last 30 days", activity.FiscalisedLast30Days.ToString("N0", CultureInfo.InvariantCulture)));
            details.Add(new("Documents waiting for a fix", activity.DocumentsWaiting.ToString("N0", CultureInfo.InvariantCulture),
                activity.DocumentsWaiting > 0 ? "warn" : "good"));

            var report = new PluginInstallationReportModel
            {
                InstallationId = config.PlatformInstallationId,
                PluginType = PluginType,
                PluginVersion = AppVersion,
                Hosting = "cloud",
                ErpName = "Xero",
                ErpVersion = config.OrganisationIsDemo ? "Demo company" : Title(config.OrganisationClass),
                ErpCompany = config.TenantName,
                PaperSize = paper,
                UsePlatformLayouts = config.FiscalStampEnabled && choice == FiscalPdfLayouts.FiscalizeLayout,
                ConnectionState = state,
                ConnectionMessage = message,
                Details = details,
                Layouts = held
                    .Where(l => string.Equals(l.PaperSize, paper, StringComparison.OrdinalIgnoreCase))
                    .Select(l => new PluginInstallationLayoutModel { DocumentType = l.DocumentType, PaperSize = l.PaperSize, Version = l.Version, Hash = l.Hash })
                    .ToList(),
                Prints = new PluginInstallationPrintsModel
                {
                    WithLayout = Math.Max(0, config.PendingPdfsWithLayout),
                    WithStandard = Math.Max(0, config.PendingPdfsWithStandard),
                    Stamped = Math.Max(0, config.PendingPdfsStamped),
                    Fallbacks = Math.Max(0, config.PendingPdfFallbacks),
                    LastFallbackAt = config.PendingPdfFallbacks > 0 ? config.LastPdfFallbackAt : null,
                    LastFallbackReason = config.PendingPdfFallbacks > 0 ? config.LastPdfFallbackReason : null
                }
            };
            if (!report.UsePlatformLayouts) report.Layouts.Clear();
            return report;
        }

        private static string Auto(ClientConfig c) => (c.AutoFiscalizeInvoices, c.AutoFiscalizeCreditNotes) switch
        {
            (true, true) => "Invoices and credit notes",
            (true, false) => "Invoices only",
            (false, true) => "Credit notes only",
            _ => "Off (fiscalised by hand)"
        };

        private static string WrittenBack(ClientConfig c)
        {
            var parts = new List<string>();
            if (c.WritebackHistoryNote) parts.Add("history note");
            if (c.FiscalStampEnabled) parts.Add("fiscal PDF");
            if (c.WritebackReference) parts.Add("Reference marker");
            return parts.Count == 0 ? "Nothing" : char.ToUpperInvariant(parts[0][0]) + string.Join(", ", parts)[1..];
        }

        private static string? When(DateTime? utc) =>
            utc?.ToString("d MMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);

        private static string? Title(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Trim().ToLowerInvariant());

        private static string Clip(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
    }
}
