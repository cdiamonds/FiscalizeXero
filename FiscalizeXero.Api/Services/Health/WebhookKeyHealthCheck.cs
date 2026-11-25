using System.Text.Json;
using FiscalizeXero.Api.Services.XeroAuth;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace FiscalizeXero.Api.Services.Health
{
    /// <summary>
    /// Reports whether <c>Xero:WebhookKey</c> is set, never its value. Without it every Xero webhook delivery is
    /// answered 401 (Xero then switches the webhook off) and only the backstop sync and user actions fiscalise,
    /// which is easy to miss because nothing else fails. Degraded (still HTTP 200) when missing, so uptime monitors
    /// keep passing but the JSON body says <c>"webhookKeyConfigured": false</c>.
    /// </summary>
    public sealed class WebhookKeyHealthCheck : IHealthCheck
    {
        public const string Name = "webhook-key";
        public const string DataKey = "webhookKeyConfigured";

        public const string MissingWarning =
            "Webhook key not configured: Xero webhooks will be rejected with 401 and Xero will not enable the webhook. " +
            "Set Xero:WebhookKey (environment variable Xero__WebhookKey or appsettings.Production.json on the server).";

        private readonly IOptions<XeroOptions> _config;
        public WebhookKeyHealthCheck(IOptions<XeroOptions> config) => _config = config;

        public static bool IsConfigured(string? key) => !string.IsNullOrWhiteSpace(key) && !key.StartsWith('<');

        /// <summary>The startup warning to log when the key is missing, or null when it is set.</summary>
        public static string? StartupWarning(string? key) => IsConfigured(key) ? null : MissingWarning;

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var configured = IsConfigured(_config.Value.WebhookKey);
            var data = new Dictionary<string, object> { [DataKey] = configured };
            return Task.FromResult(configured
                ? HealthCheckResult.Healthy("Webhook key configured", data)
                : HealthCheckResult.Degraded(MissingWarning, data: data));
        }

        /// <summary>
        /// Health response body: overall status, the webhook-key flag at the top level, and one line per check.
        /// Descriptions are generic by design; no configuration value is ever written.
        /// </summary>
        public static HealthCheckOptions ResponseOptions() => new()
        {
            ResponseWriter = async (ctx, report) =>
            {
                ctx.Response.ContentType = "application/json";
                var configured = report.Entries.TryGetValue(Name, out var entry) &&
                                 entry.Data.TryGetValue(DataKey, out var v) && v is true;
                var duplicates = report.Entries.TryGetValue(CompanyConnections.CompanyConnectionsHealthCheck.Name, out var cc) &&
                                 cc.Data.TryGetValue(CompanyConnections.CompanyConnectionsHealthCheck.DataKey, out var n) && n is int count
                    ? count : (int?)null;
                var contested = report.Entries.TryGetValue(CompanyConnections.CompanyConnectionsHealthCheck.Name, out var ce) &&
                                ce.Data.TryGetValue(CompanyConnections.CompanyConnectionsHealthCheck.ContestedKey, out var m) && m is int shared
                    ? shared : (int?)null;
                var body = new
                {
                    status = report.Status.ToString(),
                    webhookKeyConfigured = configured,
                    companiesWithDuplicateConnections = duplicates,
                    xeroOrganisationsSharedBetweenCompanies = contested,
                    checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() })
                };
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(body));
            }
        };
    }
}
