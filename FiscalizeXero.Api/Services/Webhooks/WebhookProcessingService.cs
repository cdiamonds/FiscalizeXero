using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Repositories;
using FiscalizeXero.Api.Services.FiscalDocuments;
using FiscalizeXero.Api.Services.Fiscalization;
using Microsoft.EntityFrameworkCore;

namespace FiscalizeXero.Api.Services.Webhooks
{
    /// <summary>
    /// Drains the queue of document changes (Xero webhook deliveries and backstop sync finds) and fiscalises each
    /// invoice or credit note through the same code path the controllers use (device authentication, buyer and
    /// HS-code checks, write-back to Xero).
    /// Pending rows left over from a restart are re-queued on startup so nothing is lost.
    /// </summary>
    public class WebhookProcessingService : BackgroundService
    {
        private static readonly string[] FiscalisingOperations = { "Create", "Update" };

        /// <summary>How often to look for Pending rows that are no longer in the in-memory queue (e.g. a save failed mid-way).</summary>
        private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);
        /// <summary>A Pending row younger than this is assumed to be in the queue already.</summary>
        private static readonly TimeSpan SweepMinAge = TimeSpan.FromMinutes(2);

        private readonly IServiceProvider _services;
        private readonly IWebhookQueue _queue;
        private readonly ILogger<WebhookProcessingService> _logger;

        public WebhookProcessingService(IServiceProvider services, IWebhookQueue queue, ILogger<WebhookProcessingService> logger)
        {
            _services = services;
            _queue = queue;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Let migrations and startup finish before touching the database.
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); } catch { return; }

            await RequeuePendingAsync(stoppingToken, minAge: TimeSpan.Zero);
            _ = SweepLoopAsync(stoppingToken);

            await foreach (var id in _queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessAsync(id, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Webhook event {EventId} threw an unexpected exception", id);
                }
            }
        }

        private async Task SweepLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(SweepInterval, ct); } catch { return; }
                await RequeuePendingAsync(ct, SweepMinAge);
            }
        }

        /// <summary>
        /// Queues every Pending row older than <paramref name="minAge"/>. Queuing an id twice is harmless: ProcessAsync
        /// re-reads the row and does nothing unless it is still Pending.
        /// </summary>
        private async Task RequeuePendingAsync(CancellationToken ct, TimeSpan minAge)
        {
            try
            {
                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DataContext>();
                var cutoff = DateTime.UtcNow - minAge;
                var pending = await db.WebhookEvents
                    .Where(e => e.Status == WebhookEventStatus.Pending && e.ReceivedAt <= cutoff)
                    .OrderBy(e => e.Id)
                    .Select(e => e.Id)
                    .ToListAsync(ct);

                foreach (var id in pending) _queue.Enqueue(id);
                if (pending.Count > 0)
                    _logger.LogInformation("Re-queued {Count} pending webhook events", pending.Count);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Could not re-queue pending webhook events");
            }
        }

        internal async Task ProcessAsync(long id, CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var fiscalRecords = scope.ServiceProvider.GetRequiredService<IFiscalRecordRepository>();
            var fiscalization = scope.ServiceProvider.GetRequiredService<IFiscalizationService>();

            var evt = await db.WebhookEvents.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (evt == null || evt.Status != WebhookEventStatus.Pending) return;

            try
            {
                if (!FiscalisingOperations.Contains(evt.Operation, StringComparer.OrdinalIgnoreCase))
                {
                    Finish(evt, WebhookEventStatus.Skipped, $"{evt.Operation} events are not fiscalised.");
                    return;
                }

                // One connection per company: the anchor row is the one that holds the Xero tokens.
                var config = await db.Configs
                    .Where(c => c.TenantId == evt.TenantId && c.RefreshToken != null)
                    .OrderByDescending(c => c.IsCompanyWide)
                    .Include(c => c.AppUserCompany).ThenInclude(a => a.DeviceConfigs)
                    .FirstOrDefaultAsync(ct);

                if (config == null)
                {
                    Finish(evt, WebhookEventStatus.Skipped, "No connected company for this Xero organisation.");
                    return;
                }
                // Never guess between Fiscalize companies: one company's invoice must not go to another's ZIMRA device.
                if (await TenantGuards.ContestedTenants(db).ContainsAsync((Guid?)evt.TenantId, ct))
                {
                    Finish(evt, WebhookEventStatus.Skipped, "This Xero organisation is connected to more than one Fiscalize company; not fiscalised until one disconnects.");
                    return;
                }
                evt.AppUserCompanyId = config.AppUserCompanyId;

                var documentType = XeroDocumentTypes.FromEntityName(evt.EntityName);
                if (documentType == null || !Guid.TryParse(evt.EntityId, out var documentId))
                {
                    Finish(evt, WebhookEventStatus.Skipped, $"{evt.EntityName} changes are not fiscalised.");
                    return;
                }
                if (!config.AutoFiscalizes(documentType))
                {
                    Finish(evt, WebhookEventStatus.Skipped, $"Auto-fiscalise {XeroDocumentTypes.Label(documentType)}s is switched off.");
                    return;
                }

                // Checked before Xero is asked for anything: changes to a document that is already fiscalised (a
                // payment, our own history note or attachment) cost no Xero call at all.
                var key = FiscalDocumentType.Key(documentId);
                var existing = await fiscalRecords.GetAsync(config.AppUserCompanyId, key);
                if (existing is { ReceiptNumber: not null })
                {
                    evt.DocumentNumber = Cap(existing.DocumentNumber);
                    Finish(evt, WebhookEventStatus.Skipped, $"Already fiscalised (receipt {existing.ReceiptNumber}).");
                    return;
                }
                // Fiscally cancelled by a person: only a person sends it to ZIMRA again.
                if (await FiscalSuppressions.IsSuppressedAsync(db, config.AppUserCompanyId, key, ct))
                {
                    Finish(evt, WebhookEventStatus.Skipped, "Fiscally cancelled; fiscalise it from the document page to send it to ZIMRA again.");
                    return;
                }

                // A document that failed before (a credit note approved before it was allocated to an invoice, a
                // customer with missing buyer details) has no fiscal record, so every later change tries it again.
                var result = await fiscalization.FiscalizeForConfigAsync(config, config.AppUserCompany, documentType, documentId);

                evt.DocumentNumber = Cap(result.DocumentNumber);
                // A refused write-back (Xero locks some documents) doesn't undo the receipt, but it must be visible.
                Finish(evt, WebhookEventStatus.Processed, string.IsNullOrEmpty(result.WritebackWarning)
                    ? $"Fiscalised, receipt {result.ReceiptNumber}."
                    : $"Fiscalised, receipt {result.ReceiptNumber}. {result.WritebackWarning}");
                _logger.LogInformation("{Source} fiscalised {Entity} {DocNo} for tenant {TenantId} (receipt {Receipt})",
                    evt.Source, evt.EntityName, result.DocumentNumber, evt.TenantId, result.ReceiptNumber);
            }
            catch (DocumentNotFiscalisableException ex)
            {
                // Not a sale (a draft, a purchase bill, a voided or deleted document): skipped with the reason, never failed.
                Finish(evt, WebhookEventStatus.Skipped, ex.Message);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Includes HttpClient timeouts (TaskCanceledException) that are not our own shutdown.
                _logger.LogWarning(ex, "Automatic fiscalisation failed for {Entity} {EntityId} on tenant {TenantId}",
                    evt.EntityName, evt.EntityId, evt.TenantId);
                // Same wording as the document's own "Failed" pill, including the platform's itemised validation errors
                // and, for a Xero error, the correlation id Xero support needs to trace the call.
                var detail = FiscalizeXero.Api.Services.Fiscalization.FiscalFailureService.Classify(ex).Message;
                Finish(evt, WebhookEventStatus.Failed, Truncate(detail, 1000));
            }
            finally
            {
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }

        private static void Finish(WebhookEvent evt, string status, string detail)
        {
            evt.Status = status;
            evt.Detail = Truncate(detail, 1000);
            evt.ProcessedAt = DateTime.UtcNow;
        }

        private static string? Cap(string? value) => value is { Length: > 100 } ? value[..100] : value;

        private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
    }
}
