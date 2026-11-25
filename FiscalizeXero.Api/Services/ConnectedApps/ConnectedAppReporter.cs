using System.Threading.Channels;
using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.DeviceSession;
using FiscalizeXero.Api.Services.Layouts;
using FiscalizeXero.Api.Services.XeroAuth;
using FiscalizeXero.Fiscalize.Services.PluginInstallations;
using FiscalizeXero.Xero.Services;
using Microsoft.EntityFrameworkCore;

namespace FiscalizeXero.Api.Services.ConnectedApps
{
    /// <summary>Asks for one connection to report itself soon (after connecting, a settings change, a layout choice).</summary>
    public interface IConnectedAppReportQueue
    {
        void Enqueue(long clientConfigId);
        IAsyncEnumerable<long> ReadAllAsync(CancellationToken ct);
    }

    public class ConnectedAppReportQueue : IConnectedAppReportQueue
    {
        private readonly Channel<long> _channel = Channel.CreateUnbounded<long>(new UnboundedChannelOptions { SingleReader = true });
        public void Enqueue(long clientConfigId) => _channel.Writer.TryWrite(clientConfigId);
        public IAsyncEnumerable<long> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
    }

    /// <summary>Sends one connection's report. Scoped; used by the background reporter and, on disconnect, by the controller.</summary>
    public interface IConnectedAppReportSender
    {
        /// <summary>Reports one connection. Never throws; returns false when nothing was sent.</summary>
        Task<bool> SendAsync(ClientConfig config, bool disconnected = false, CancellationToken ct = default);
    }

    public class ConnectedAppReportSender : IConnectedAppReportSender
    {
        private readonly DataContext _db;
        private readonly IDeviceCaller _device;
        private readonly IPlatformLayoutService _layouts;
        private readonly IXeroAuthBuilder _xeroAuth;
        private readonly IFiscalPdfCounter _counter;
        private readonly ILogger<ConnectedAppReportSender> _logger;

        public ConnectedAppReportSender(DataContext db, IDeviceCaller device, IPlatformLayoutService layouts, IXeroAuthBuilder xeroAuth,
            IFiscalPdfCounter counter, ILogger<ConnectedAppReportSender> logger)
        {
            _db = db; _device = device; _layouts = layouts; _xeroAuth = xeroAuth; _counter = counter; _logger = logger;
        }

        public async Task<bool> SendAsync(ClientConfig config, bool disconnected = false, CancellationToken ct = default)
        {
            try
            {
                var device = await _device.FindDeviceAsync(config.AppUserCompanyId);
                if (device == null) return false; // no device key yet: nothing to sign the report with
                if (config.PlatformInstallationId == Guid.Empty)
                {
                    config.PlatformInstallationId = Guid.NewGuid();
                    await _db.Configs.Where(c => c.Id == config.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(c => c.PlatformInstallationId, config.PlatformInstallationId), ct);
                }

                if (!disconnected && config.IsLive)
                {
                    var xero = await TryXeroAsync(config);
                    if (xero != null) await RefreshOrganisationAsync(config, xero, ct);
                    // Keep the layouts in use current, so the platform never shows this connection behind on one.
                    if (config.FiscalStampEnabled && FiscalPdfLayouts.Normalise(config.FiscalPdfLayout) == FiscalPdfLayouts.FiscalizeLayout)
                        await _layouts.RefreshAsync(config, xero, ct);
                }

                var activity = await ActivityAsync(config, ct);
                var held = await _layouts.HeldAsync(config.AppUserCompanyId, ct);
                var report = ConnectedAppReport.Build(config, held, activity, disconnected);

                await _device.CallAsync(device, auth => PlatformInstallationClient.ReportAsync(auth, report), ct);

                var p = report.Prints!;
                if (p.WithLayout + p.WithStandard + p.Stamped + p.Fallbacks > 0)
                    await _counter.SubtractAsync(config.Id, p.WithLayout, p.WithStandard, p.Stamped, p.Fallbacks);
                if (!disconnected)
                    await _db.Configs.Where(c => c.Id == config.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(c => c.PlatformReportedAt, DateTime.UtcNow), ct);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Connected apps report for connection {ConfigId} (company {CompanyId}) failed; it is tried again later",
                    config.Id, config.AppUserCompanyId);
                return false;
            }
        }

        private async Task<FiscalizeXero.Xero.XeroAuth?> TryXeroAsync(ClientConfig config)
        {
            try { return await _xeroAuth.BuildAsync(config); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogInformation(ex, "Xero could not be reached for connection {ConfigId} while reporting", config.Id);
                return null;
            }
        }

        /// <summary>The organisation's short code, plan and currency, read at most once a day (one Xero call).</summary>
        private async Task RefreshOrganisationAsync(ClientConfig config, FiscalizeXero.Xero.XeroAuth xero, CancellationToken ct)
        {
            if (config.OrganisationCheckedAt is { } at && DateTime.UtcNow - at < TimeSpan.FromDays(1)) return;
            try
            {
                var org = await OrganisationService.GetAsync(xero);
                if (org == null) return;
                config.OrganisationShortCode = Clip(org.ShortCode, 30);
                config.OrganisationBaseCurrency = Clip(org.BaseCurrency, 10);
                config.OrganisationClass = Clip(org.Class, 30);
                config.OrganisationIsDemo = org.IsDemoCompany;
                config.OrganisationCheckedAt = DateTime.UtcNow;
                await _db.Configs.Where(c => c.Id == config.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.OrganisationShortCode, config.OrganisationShortCode)
                    .SetProperty(c => c.OrganisationBaseCurrency, config.OrganisationBaseCurrency)
                    .SetProperty(c => c.OrganisationClass, config.OrganisationClass)
                    .SetProperty(c => c.OrganisationIsDemo, config.OrganisationIsDemo)
                    .SetProperty(c => c.OrganisationCheckedAt, config.OrganisationCheckedAt), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogInformation(ex, "Could not read the Xero organisation for connection {ConfigId}", config.Id);
            }
        }

        private async Task<ConnectedAppActivity> ActivityAsync(ClientConfig config, CancellationToken ct)
        {
            var since = DateTime.UtcNow.AddDays(-30);
            var companyId = config.AppUserCompanyId;
            DateTime? lastWebhook = config.TenantId is { } tenant
                ? await _db.WebhookEvents.AsNoTracking().Where(e => e.TenantId == tenant && e.Source == WebhookEventSource.Webhook)
                    .MaxAsync(e => (DateTime?)e.ReceivedAt, ct)
                : null;
            var waiting = await _db.FiscalFailures.AsNoTracking().CountAsync(f => f.AppUserCompanyId == companyId, ct);
            var fiscalised = await _db.FiscalRecords.AsNoTracking().CountAsync(r => r.AppUserCompanyId == companyId && r.FiscalizedAt >= since, ct);
            return new ConnectedAppActivity(lastWebhook, waiting, fiscalised);
        }

        private static string? Clip(string? value, int max) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : value.Trim()[..max];
    }

    /// <summary>
    /// Keeps every Xero connection on the platform's Connected apps page: each one reports every
    /// <c>Fiscalize:ConnectedApps:IntervalMinutes</c> (30) and soon after it is connected or its settings change. A report that
    /// fails is simply tried at the next round; reporting never holds up fiscalisation.
    /// </summary>
    public class ConnectedAppReporter : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly IConnectedAppReportQueue _queue;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ConnectedAppReporter> _logger;

        public ConnectedAppReporter(IServiceScopeFactory scopes, IConnectedAppReportQueue queue, IConfiguration configuration, ILogger<ConnectedAppReporter> logger)
        {
            _scopes = scopes; _queue = queue; _configuration = configuration; _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue("Fiscalize:ConnectedApps:Enabled", true))
            {
                _logger.LogInformation("Connected apps reporting is switched off (Fiscalize:ConnectedApps:Enabled)");
                return;
            }
            var interval = TimeSpan.FromMinutes(Math.Clamp(_configuration.GetValue("Fiscalize:ConnectedApps:IntervalMinutes", 30), 5, 24 * 60));

            var onDemand = Task.Run(() => DrainQueueAsync(stoppingToken), stoppingToken);
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); } catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                await ReportDueAsync(interval, stoppingToken);
                try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); } catch (OperationCanceledException) { break; }
            }
            await onDemand;
        }

        private async Task ReportDueAsync(TimeSpan interval, CancellationToken ct)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DataContext>();
                var due = DateTime.UtcNow - interval;
                var ids = await db.Configs.AsNoTracking()
                    .Where(c => c.TenantId != null && (c.PlatformReportedAt == null || c.PlatformReportedAt < due))
                    .OrderBy(c => c.PlatformReportedAt).Select(c => c.Id).Take(200).ToListAsync(ct);
                foreach (var id in ids)
                {
                    if (ct.IsCancellationRequested) break;
                    await ReportOneAsync(id, ct);
                    await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connected apps reporting round failed");
            }
        }

        private async Task DrainQueueAsync(CancellationToken ct)
        {
            try
            {
                await foreach (var id in _queue.ReadAllAsync(ct))
                {
                    // A burst of settings saves becomes one report.
                    try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch (OperationCanceledException) { return; }
                    await ReportOneAsync(id, ct);
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task ReportOneAsync(long configId, CancellationToken ct)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DataContext>();
                var config = await db.Configs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == configId, ct);
                if (config == null || config.TenantId == null) return;
                await scope.ServiceProvider.GetRequiredService<IConnectedAppReportSender>().SendAsync(config, ct: ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Connected apps report for connection {ConfigId} failed", configId);
            }
        }
    }
}
