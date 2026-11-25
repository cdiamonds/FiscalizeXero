using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.FiscalDocuments;
using FiscalizeXero.Api.Services.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace FiscalizeXero.Api.Services.Fiscalization
{
    /// <summary>
    /// Tries failed documents again when their <see cref="FiscalFailure.NextRetryAt"/> comes round, through the same
    /// queue and pipeline as webhooks and the sync (a <see cref="WebhookEvent"/> with source Retry), so a document
    /// that failed because Xero or the platform was briefly unavailable fiscalises without anyone clicking.
    /// Only while that document type's auto-fiscalise switch is on; never for a fiscally cancelled document.
    /// </summary>
    public sealed class FailureRetryService : BackgroundService
    {
        private static readonly TimeSpan Tick = TimeSpan.FromMinutes(2);
        /// <summary>Each retry costs a few Xero calls; an organisation may have as few as 1,000 a day.</summary>
        internal const int MaxPerCompanyPerTick = 20;

        private readonly IServiceProvider _services;
        private readonly IWebhookQueue _queue;
        private readonly ILogger<FailureRetryService> _logger;

        public FailureRetryService(IServiceProvider services, IWebhookQueue queue, ILogger<FailureRetryService> logger)
        {
            _services = services;
            _queue = queue;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); } catch { return; }
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await RunOnceAsync(stoppingToken); await PurgeOldEventsAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex) { _logger.LogError(ex, "Failure retry pass threw an unexpected exception"); }
                try { await Task.Delay(Tick, stoppingToken); } catch { return; }
            }
        }

        private DateTime _lastPurge = DateTime.MinValue;

        /// <summary>
        /// Once a day, deletes delivery log rows older than 90 days. Every Xero change for every organisation adds one,
        /// and nothing reads them after the Webhooks page's recent list. Pending rows are never deleted.
        /// </summary>
        private async Task PurgeOldEventsAsync(CancellationToken ct)
        {
            if (DateTime.UtcNow - _lastPurge < TimeSpan.FromDays(1)) return;
            _lastPurge = DateTime.UtcNow;
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var cutoff = DateTime.UtcNow.AddDays(-90);
            var deleted = await db.WebhookEvents
                .Where(e => e.ReceivedAt < cutoff && e.Status != WebhookEventStatus.Pending)
                .ExecuteDeleteAsync(ct);
            if (deleted > 0) _logger.LogInformation("Deleted {Count} delivery log rows older than 90 days", deleted);
        }

        internal async Task<int> RunOnceAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var now = DateTime.UtcNow;

            var due = await db.FiscalFailures
                .Where(f => f.NextRetryAt != null && f.NextRetryAt <= now)
                .OrderBy(f => f.NextRetryAt)
                .Take(200)
                .ToListAsync(ct);
            if (due.Count == 0) return 0;

            var events = new List<WebhookEvent>();
            foreach (var group in due.GroupBy(f => f.AppUserCompanyId))
            {
                var companyId = group.Key;
                var config = await db.Configs.AsNoTracking()
                    .Where(c => c.AppUserCompanyId == companyId && c.TenantId != null && c.RefreshToken != null
                                && !TenantGuards.ContestedTenants(db).Contains(c.TenantId))
                    .Select(c => new { c.TenantId, c.AutoFiscalizeInvoices, c.AutoFiscalizeCreditNotes })
                    .FirstOrDefaultAsync(ct);
                if (config == null)
                {
                    // Not connected right now: look again in an hour rather than every pass.
                    foreach (var f in group) f.NextRetryAt = now.AddHours(1);
                    continue;
                }

                var ids = group.Select(f => f.DocumentId).ToList();
                var fiscalised = (await db.FiscalRecords.AsNoTracking()
                    .Where(r => r.AppUserCompanyId == companyId && r.ReceiptNumber != null && ids.Contains(r.DocumentId))
                    .Select(r => r.DocumentId).ToListAsync(ct)).ToHashSet();
                var suppressed = (await FiscalSuppressions.ForAsync(db, companyId, ids, ct)).ToHashSet();
                var tenant = config.TenantId!.Value;
                var pending = (await db.WebhookEvents.AsNoTracking()
                    .Where(e => e.TenantId == tenant && e.Status == WebhookEventStatus.Pending && ids.Contains(e.EntityId))
                    .Select(e => e.EntityId).ToListAsync(ct)).ToHashSet();

                var queued = 0;
                foreach (var failure in group)
                {
                    var auto = failure.DocumentType == FiscalDocumentType.CreditNote ? config.AutoFiscalizeCreditNotes : config.AutoFiscalizeInvoices;
                    if (fiscalised.Contains(failure.DocumentId)) { db.FiscalFailures.Remove(failure); continue; }
                    if (!auto || suppressed.Contains(failure.DocumentId)) { failure.NextRetryAt = null; continue; }
                    if (pending.Contains(failure.DocumentId)) { failure.NextRetryAt = null; continue; } // already on its way
                    if (queued >= MaxPerCompanyPerTick) continue; // stays due for the next pass

                    var number = await db.WebhookEvents.AsNoTracking()
                        .Where(e => e.TenantId == tenant && e.EntityId == failure.DocumentId && e.DocumentNumber != null)
                        .OrderByDescending(e => e.Id).Select(e => e.DocumentNumber).FirstOrDefaultAsync(ct);
                    events.Add(new WebhookEvent
                    {
                        TenantId = tenant,
                        EntityName = XeroDocumentTypes.EntityName(failure.DocumentType),
                        EntityId = failure.DocumentId,
                        DocumentNumber = number,
                        Operation = "Update",
                        Source = WebhookEventSource.Retry,
                        AppUserCompanyId = companyId,
                    });
                    // Cleared now; a failed retry sets the next time from the policy.
                    failure.NextRetryAt = null;
                    queued++;
                }
            }

            db.WebhookEvents.AddRange(events);
            await db.SaveChangesAsync(ct);
            foreach (var e in events) _queue.Enqueue(e.Id);
            if (events.Count > 0)
                _logger.LogInformation("Retrying {Count} failed document(s)", events.Count);
            return events.Count;
        }
    }
}
