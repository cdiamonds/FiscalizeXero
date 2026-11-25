using FiscalizeXero.Fiscalize.Models.PluginInstallations;
using FiscalizeXero.Shared.Models.Auths;

namespace FiscalizeXero.Fiscalize.Services.PluginInstallations
{
    /// <summary>The platform's Connected apps report for a device key.</summary>
    public static class PlatformInstallationClient
    {
        public const string ReportEndpoint = "api/plugin-installations/devices/current";

        public static Task<PluginInstallationReceiptModel> ReportAsync(FiscalizeAuthModel auth, PluginInstallationReportModel report) =>
            RestClient.PutAsync<PluginInstallationReportModel, PluginInstallationReceiptModel>(auth, ReportEndpoint, report);
    }
}
