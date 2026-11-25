using System.Diagnostics;
using System.Threading.Channels;
using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Repositories;
using FiscalizeXero.Api.Services.Documents;
using FiscalizeXero.Api.Services.FiscalDocuments;
using FiscalizeXero.Api.Services.Fiscalization;
using FiscalizeXero.Api.Services.Webhooks;
using FiscalizeXero.Api.Services.XeroAuth;
using FiscalizeXero.Xero.Exceptions;
using FiscalizeXero.Xero.Models;
using FiscalizeXero.Xero.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using XeroAuthModel = FiscalizeXero.Xero.XeroAuth;

namespace FiscalizeXero.Api.Services.Sync
{
    /// <summary>Lets request handlers ask for an immediate sync of one connection (first connect, reconnect).</summary>
    public interface IXeroSyncRequests
    {
        void RequestSync(long clientConfigId);
        ValueTask<bool> WaitForRequestAsync(CancellationToken ct);
        bool TryRead(out long clientConfigId);
    }

    public sealed class XeroSyncRequests : IXeroSyncRequests
    {
        private readonly Channel<long> _channel = Channel.CreateUnbounded<long>(new UnboundedChannelOptions { SingleReader = true });
        public void RequestSync(long clientConfigId) => _channel.Writer.TryWrite(clientConfigId);
        public ValueTask<bool> WaitForRequestAsync(CancellationToken ct) => _channel.Reader.WaitToReadAsync(ct);
        public bool TryRead(out long clientConfigId) => _channel.Reader.TryRead(out clientConfigId);
    }

    public static class XeroSyncRegistration
    {
        /// <summary>Registers the backstop sync worker and its options.</summary>
        public static IServiceCollection AddXeroBackgroundSync(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<XeroSyncOptions>(configuration.GetSection(XeroSyncOptions.SectionName));
            services.TryAddSingleton<IXeroSyncRequests, XeroSyncRequests>();
            services.AddHostedService<XeroSyncService>();
            return services;
        }
    }

    /// <summary>
    /// The backstop behind webhooks. A few times a day per connected organisation
    /// (<see cref="XeroSyncOptions.IntervalHours"/>, jittered per company), and immediately after a (re)connection,
    /// this worker lists the approved sales invoices and customer credit notes modified since its last successful
    /// run, and queues what still needs fiscalising through the same idempotent pipeline the webhooks use
    /// (<see cref="WebhookEvent"/> rows + <see cref="WebhookProcessingService"/>).
    ///
    /// Usually one call per document type per run, so API usage per customer is a handful of requests a day plus
    /// whatever the customer does in the app.
    /// </summary>
    public class XeroSyncService : BackgroundService
    {
        private const int PageSize = 100;

        private readonly IServiceProvider _services;
        private readonly XeroSyncOptions _options;
        private readonly IXeroSyncRequests _requests;
        private readonly IWebhookQueue _queue;
        private readonly ILogger<XeroSyncService> _logger;

        public XeroSyncService(
            IServiceProvider services,
            IOptions<XeroSyncOptions> options,
            IXeroSyncRequests requests,
            IWebhookQueue queue,
            ILogger<XeroSyncService> logger)
        {
            _services = services;
            _options = options.Value;
            _requests = requests;
            _queue = queue;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogWarning("Xero backstop sync disabled by configuration (Xero:Sync:Enabled=false); only webhooks and user actions will fiscalise.");
                return;
            }

            _logger.LogInformation("Xero backstop sync enabled: every {Hours}h per company (+ up to {Jitter}m jitter), checking every {Tick}s",
                _options.IntervalHours, _options.JitterMinutes, _options.TickSeconds);

            // Let migrations and startup finish before touching the database or Xero.
            try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); } catch { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await DrainRequestsAsync(stoppingToken);
                    await RunDueAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Xero sync tick threw an unexpected exception");
                }

                // Sleep until the next tick, or sooner when a connection asks for a catch-up.
                try
                {
                    var wait = _requests.WaitForRequestAsync(stoppingToken).AsTask();
                    await Task.WhenAny(wait, Task.Delay(TimeSpan.FromSeconds(_options.TickSeconds), stoppingToken));
                }
                catch (OperationCanceledException) { return; }
            }
        }

        private async Task DrainRequestsAsync(CancellationToken ct)
        {
            var ids = new HashSet<long>();
            while (_requests.TryRead(out var id)) ids.Add(id);
            if (ids.Count == 0) return;

            List<long> eligible;
            using (var scope = _services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DataContext>();
                eligible = await EligibleConfigs(db).Where(c => ids.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct);
            }
            _logger.LogInformation("Xero sync catch-up requested for {Requested} connection(s); {Eligible} eligible", ids.Count, eligible.Count);
            foreach (var id in eligible)
            {
                if (ct.IsCancellationRequested) return;
                await SyncOneAsync(id, "catch-up", ignoreBackoff: true, ct);
            }
        }

        private async Task RunDueAsync(CancellationToken ct)
        {
            List<long> due;
            using (var scope = _services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DataContext>();
                var now = DateTime.UtcNow;
                due = (await EligibleConfigs(db).Select(c => new { c.Id, c.LastSyncRunAt, c.SyncRetryAt }).ToListAsync(ct))
                    .Where(c => SyncPlanner.IsDue(c.LastSyncRunAt, c.SyncRetryAt, now, c.Id, _options))
                    .Select(c => c.Id)
                    .ToList();
            }
            if (due.Count == 0) return;

            _logger.LogInformation("Xero sync tick: {Count} connection(s) due", due.Count);
            foreach (var id in due)
            {
                if (ct.IsCancellationRequested) return;
                await SyncOneAsync(id, "scheduled", ignoreBackoff: false, ct);
                try { await Task.Delay(TimeSpan.FromSeconds(_options.DelayBetweenCompaniesSeconds), ct); } catch { return; }
            }
        }

        /// <summary>Connected companies with at least one auto-fiscalise switch on. The anchor row holds the tokens.</summary>
        internal static IQueryable<ClientConfig> EligibleConfigs(DataContext db) =>
            db.Configs
                .Where(c => c.TenantId != null
                            && c.RefreshToken != null
                            && (c.AutoFiscalizeInvoices || c.AutoFiscalizeCreditNotes)
                            // Never fiscalise a Xero organisation that more than one Fiscalize company is connected to.
                            && !TenantGuards.ContestedTenants(db).Contains(c.TenantId));

        /// <summary>Consecutive failed runs per connection, for the backoff. In memory: a restart simply starts over.</summary>
        private readonly Dictionary<long, int> _failures = new();

        /// <summary>
        /// One company's run, in its own scope with the row read fresh, so a settings change or a token rotation made
        /// meanwhile is neither missed nor overwritten, and one company's failure cannot leave tracked changes behind
        /// for the next.
        /// </summary>
        private async Task SyncOneAsync(long configId, string reason, bool ignoreBackoff, CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<DataContext>();
            var configs = sp.GetRequiredService<Services.ClientConfigs.IClientConfigService>();

            var config = await EligibleConfigs(db).Include(c => c.AppUserCompany).ThenInclude(a => a.DeviceConfigs)
                .FirstOrDefaultAsync(c => c.Id == configId, ct);
            if (config == null) return; // disconnected or switched off meanwhile
            if (!ignoreBackoff && config.SyncRetryAt > DateTime.UtcNow) return;

            var fiscalRecords = sp.GetRequiredService<IFiscalRecordRepository>();
            var authBuilder = sp.GetRequiredService<IXeroAuthBuilder>();
            var tenant = config.TenantId!.Value;
            var runStarted = DateTime.UtcNow;
            var sw = Stopwatch.StartNew();

            // The same device rule fiscalisation itself uses: the default device, else the first.
            var devices = config.AppUserCompany?.DeviceConfigs ?? new List<AppUserDeviceConfig>();
            var device = devices.FirstOrDefault(d => d.IsDefault) ?? devices.FirstOrDefault();
            if (device == null || string.IsNullOrEmpty(device.ApiKey) || string.IsNullOrEmpty(device.ApiSecret))
            {
                // The cursor stays where it is, so nothing approved meanwhile is lost; saving the device asks for a run.
                _logger.LogDebug("Xero sync ({Reason}) for tenant {TenantId}: no ZIMRA device with an API key; waiting", reason, tenant);
                return;
            }

            var auth = await authBuilder.BuildAsync(config);
            if (auth == null)
            {
                _logger.LogInformation("Xero sync ({Reason}) for tenant {TenantId}: connection not usable (needs reconnecting); skipping", reason, tenant);
                return;
            }

            var since = SyncPlanner.ModifiedSince(config.LastSyncAt, runStarted, _options);

            try
            {
                var changes = new List<SyncChange>();
                DateTime? resumeFrom = null;

                // A type whose auto-fiscalise switch is off is not even listed: it would only be skipped afterwards.
                if (config.AutoFiscalizeInvoices)
                    resumeFrom = Earliest(resumeFrom, await ListAsync(auth, FiscalDocumentType.Invoice, since, changes, tenant));
                if (config.AutoFiscalizeCreditNotes)
                    resumeFrom = Earliest(resumeFrom, await ListAsync(auth, FiscalDocumentType.CreditNote, since, changes, tenant));

                var ids = changes.Select(c => c.DocumentId).Distinct().ToList();
                var existing = ids.Count == 0 ? new List<FiscalRecord>() : await fiscalRecords.GetManyAsync(config.AppUserCompanyId, ids);
                var pending = ids.Count == 0
                    ? new List<string>()
                    : await db.WebhookEvents
                        .Where(e => e.TenantId == tenant && e.Status == WebhookEventStatus.Pending && ids.Contains(e.EntityId))
                        .Select(e => e.EntityId)
                        .ToListAsync(ct);
                var suppressed = ids.Count == 0 ? new List<string>() : await FiscalSuppressions.ForAsync(db, config.AppUserCompanyId, ids, ct);

                var selected = SyncPlanner.SelectForFiscalisation(changes, config, existing, pending.Concat(suppressed).ToList(),
                    out var skippedDone, out var skippedPending, out var skippedOff);

                var events = selected.Select(c => new WebhookEvent
                {
                    TenantId = tenant,
                    EntityName = XeroDocumentTypes.EntityName(c.DocumentType),
                    EntityId = c.DocumentId,
                    DocumentNumber = c.DocumentNumber is { Length: > 100 } n ? n[..100] : c.DocumentNumber,
                    Operation = "Update", // a "modified since" listing does not tell a new document from a changed one; both fiscalise
                    LastUpdated = c.UpdatedUtc?.ToString("yyyy-MM-ddTHH:mm:ss.fff"),
                    Source = WebhookEventSource.Sync,
                    AppUserCompanyId = config.AppUserCompanyId
                }).ToList();

                if (events.Count > 0)
                {
                    db.WebhookEvents.AddRange(events);
                    await db.SaveChangesAsync(ct);
                }

                if (resumeFrom.HasValue)
                {
                    // Cut short: the next run starts exactly where this one stopped (the overlap is added back so it
                    // isn't subtracted twice) and follows straight away rather than in six hours.
                    await configs.SaveSyncStateAsync(config.Id, resumeFrom.Value + TimeSpan.FromMinutes(_options.OverlapMinutes), null, null, null);
                    _requests.RequestSync(config.Id);
                }
                else
                {
                    await configs.SaveSyncStateAsync(config.Id, runStarted, DateTime.UtcNow, null, null);
                }
                _failures.Remove(config.Id);
                foreach (var evt in events) _queue.Enqueue(evt.Id);

                _logger.LogInformation(
                    "Xero sync ({Reason}) for tenant {TenantId}: since {Since:u}, {Changed} approved document(s) changed, {Queued} queued, skipped {Done} fiscalised / {Pending} pending or cancelled / {Off} switched off, {Ms}ms",
                    reason, tenant, since, changes.Count, events.Count, skippedDone, skippedPending, skippedOff, sw.ElapsedMilliseconds);
            }
            catch (XeroApiException ex) when (ex.IsDailyLimit)
            {
                // The cursor stays, so the first run after Xero's midnight-UTC reset re-covers this window; nothing
                // asks Xero again before then.
                var reset = DateTime.UtcNow.Date.AddDays(1).AddMinutes(5);
                await SaveFailureAsync(configs, config.Id, reset, "The organisation's daily allowance of Xero calls is used up. The check resumes after Xero resets it at midnight UTC.");
                _logger.LogWarning("Xero sync ({Reason}) for tenant {TenantId}: the organisation's daily limit of Xero calls is used up; next try {Retry:u}", reason, tenant, reset);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                var failures = _failures[config.Id] = _failures.GetValueOrDefault(config.Id) + 1;
                var retry = DateTime.UtcNow + SyncPlanner.Backoff(failures, _options);
                var message = ex is XeroApiException x ? $"Xero: {x.Message}" : ex.Message;
                await SaveFailureAsync(configs, config.Id, retry, message);
                // ToString() of a XeroApiException carries status, error type and correlation id.
                _logger.LogWarning(ex, "Xero sync ({Reason}) for tenant {TenantId} failed after {Ms}ms (failure {Count} in a row; next try {Retry:u}): {Message}",
                    reason, tenant, sw.ElapsedMilliseconds, failures, retry, ex.Message);
            }
        }

        private async Task SaveFailureAsync(Services.ClientConfigs.IClientConfigService configs, long id, DateTime retryAt, string error)
        {
            try { await configs.SaveSyncStateAsync(id, null, null, retryAt, error); }
            catch (Exception ex) { _logger.LogError(ex, "Could not record the failed Xero sync for ClientConfig {ConfigId}", id); }
        }

        /// <summary>
        /// Lists one document type modified since <paramref name="since"/>, approved statuses only, oldest change
        /// first. Pages by key, not by page number: each request asks again from the latest modification time seen,
        /// so a document edited mid-run (which moves to the end of the order) shifts nothing out of sight. Returns
        /// null when everything was read; when the page limit cut the listing short, the modification time of the last
        /// document read, for the next run to continue from.
        /// </summary>
        private async Task<DateTime?> ListAsync(XeroAuthModel auth, string documentType, DateTime since, List<SyncChange> changes, Guid tenant)
        {
            DateTime? lastSeen = null;
            var seen = new HashSet<Guid>();
            var cursor = since;
            var page = 1;
            for (var request = 1; ; request++)
            {
                var query = new DocumentQuery
                {
                    Page = page,
                    PageSize = PageSize,
                    Statuses = DocumentLister.ApprovedStatuses.ToArray(),
                    ModifiedSince = cursor,
                    Order = "UpdatedDateUTC ASC"
                };

                IReadOnlyList<SalesDocument> documents = documentType == FiscalDocumentType.CreditNote
                    ? (await CreditNoteService.ListAsync(auth, query)).Items
                    : (await InvoiceService.ListAsync(auth, query)).Items;

                DateTime? pageLatest = null;
                foreach (var document in documents)
                {
                    if (document.UpdatedDateUTC.HasValue && (pageLatest == null || document.UpdatedDateUTC > pageLatest)) pageLatest = document.UpdatedDateUTC;
                    if (document.DocumentId == Guid.Empty || !seen.Add(document.DocumentId)) continue;
                    // Belt and braces: Xero was asked for approved sales documents only, but nothing else may get through.
                    var isSale = document is Invoice { IsSale: true } or CreditNote { IsCustomerCredit: true };
                    if (!isSale || !document.IsApproved) continue;
                    changes.Add(new SyncChange(documentType, FiscalDocumentType.Key(document.DocumentId), document.DocumentNumber, document.UpdatedDateUTC));
                }
                if (pageLatest.HasValue && (lastSeen == null || pageLatest > lastSeen)) lastSeen = pageLatest;

                if (documents.Count < PageSize) return null;
                if (request >= Math.Max(1, _options.MaxPagesPerRun))
                {
                    _logger.LogWarning("Xero sync for tenant {TenantId}: more than {Count} {Type} documents changed since {Since:u}; continuing from {Resume:u}",
                        tenant, request * PageSize, documentType, since, lastSeen);
                    return lastSeen;
                }

                // If-Modified-Since works to the second. Move the cursor up to the newest second on this page and start
                // again at page 1 (documents already seen are skipped); only a full page within one second needs page+1.
                var next = pageLatest.HasValue ? SyncPlanner.FloorToSecond(pageLatest.Value) : cursor;
                if (next > cursor) { cursor = next; page = 1; }
                else page++;
            }
        }

        private static DateTime? Earliest(DateTime? a, DateTime? b) => a == null ? b : b == null ? a : a < b ? a : b;
    }
}
