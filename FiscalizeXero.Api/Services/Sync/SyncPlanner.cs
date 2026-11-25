using FiscalizeXero.Api.Data;

namespace FiscalizeXero.Api.Services.Sync
{
    /// <summary>A document the sync found changed in Xero. <see cref="DocumentId"/> is in its stored form.</summary>
    public sealed record SyncChange(string DocumentType, string DocumentId, string? DocumentNumber, DateTime? UpdatedUtc);

    /// <summary>
    /// The decisions behind a sync run, as pure functions so they can be unit tested: when a company is due, which
    /// "modified since" to ask for, and which returned documents still need fiscalising.
    /// </summary>
    public static class SyncPlanner
    {
        /// <summary>Fixed per-company jitter in [0, jitterMinutes) so companies spread out instead of running together.</summary>
        public static TimeSpan Jitter(long clientConfigId, int jitterMinutes)
        {
            if (jitterMinutes <= 0) return TimeSpan.Zero;
            var hash = unchecked((ulong)clientConfigId * 11400714819323198485UL); // Fibonacci hashing: cheap and well spread
            return TimeSpan.FromMinutes(hash % (ulong)jitterMinutes);
        }

        /// <summary>A company is due when it has never been synced, or its interval (plus jitter) has elapsed.</summary>
        public static bool IsDue(DateTime? lastSyncUtc, DateTime nowUtc, long clientConfigId, XeroSyncOptions options)
            => IsDue(lastSyncUtc, null, nowUtc, clientConfigId, options);

        /// <summary>
        /// Due when no run has completed yet, or the interval (plus jitter) has passed since the last completed run;
        /// never while a failed run's backoff (<paramref name="retryAtUtc"/>) is still running.
        /// </summary>
        public static bool IsDue(DateTime? lastRunUtc, DateTime? retryAtUtc, DateTime nowUtc, long clientConfigId, XeroSyncOptions options)
        {
            if (retryAtUtc.HasValue && retryAtUtc.Value > nowUtc) return false;
            if (retryAtUtc.HasValue) return true; // a failed run's backoff has ended: try again now
            var lastSyncUtc = lastRunUtc;
            if (lastSyncUtc == null) return true;
            var interval = TimeSpan.FromHours(options.IntervalHours) + Jitter(clientConfigId, options.JitterMinutes);
            return nowUtc - lastSyncUtc.Value >= interval;
        }

        /// <summary>
        /// The "modified since" for a run: the last successful run minus the overlap, or the initial look-back for a
        /// connection never synced. Xero keeps no limited change window, so there is no floor.
        /// </summary>
        public static DateTime ModifiedSince(DateTime? lastSyncUtc, DateTime nowUtc, XeroSyncOptions options) =>
            lastSyncUtc.HasValue
                ? lastSyncUtc.Value - TimeSpan.FromMinutes(options.OverlapMinutes)
                : nowUtc - TimeSpan.FromMinutes(options.InitialLookbackMinutes);

        /// <summary>Wait after the n-th failed run in a row: 5, 10, 20, 40 minutes... capped at the normal interval.</summary>
        public static TimeSpan Backoff(int consecutiveFailures, XeroSyncOptions options)
        {
            var minutes = 5 * Math.Pow(2, Math.Clamp(consecutiveFailures - 1, 0, 10));
            var cap = Math.Max(5, options.IntervalHours * 60);
            return TimeSpan.FromMinutes(Math.Min(minutes, cap));
        }

        /// <summary>Xero's If-Modified-Since works to the second; rounding down never skips a document.</summary>
        public static DateTime FloorToSecond(DateTime value) =>
            new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, value.Kind);

        /// <summary>
        /// Keeps only the documents worth queuing: the company's auto-fiscalise switch is on, there is no fiscal
        /// record yet, and no webhook or sync event is already waiting for the same document.
        /// </summary>
        public static IReadOnlyList<SyncChange> SelectForFiscalisation(
            IEnumerable<SyncChange> changes,
            ClientConfig config,
            IReadOnlyCollection<FiscalRecord> existingRecords,
            IReadOnlyCollection<string> pendingDocumentIds,
            out int skippedDone, out int skippedPending, out int skippedOff)
        {
            skippedDone = skippedPending = skippedOff = 0;
            // Only a record with a receipt number is finished; one still waiting for ZIMRA is looked at again.
            var done = existingRecords.Where(r => r.ReceiptNumber.HasValue).Select(r => r.DocumentId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var pending = pendingDocumentIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var selected = new List<SyncChange>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var change in changes)
            {
                if (change.DocumentType is not (FiscalDocumentType.Invoice or FiscalDocumentType.CreditNote)) continue;
                if (!seen.Add(change.DocumentId)) continue; // the same document on two pages of one run
                if (!config.AutoFiscalizes(change.DocumentType)) { skippedOff++; continue; }
                if (done.Contains(change.DocumentId)) { skippedDone++; continue; }
                if (pending.Contains(change.DocumentId)) { skippedPending++; continue; }
                selected.Add(change);
            }
            return selected;
        }
    }
}
