namespace FiscalizeXero.Api.Services.Sync
{
    /// <summary>
    /// Settings for the backstop sync (<c>Xero:Sync</c> section). Webhooks are the primary trigger; the sync asks
    /// Xero a few times a day per organisation for documents changed since its last run, to pick up anything a
    /// webhook missed. Xero has no change-data-capture feed, so this is a "modified since" listing.
    /// </summary>
    public class XeroSyncOptions
    {
        public const string SectionName = "Xero:Sync";

        /// <summary>Master switch for the sync worker.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>How often each connected organisation is synced.</summary>
        public double IntervalHours { get; set; } = 6;

        /// <summary>
        /// Per-company offset added to the interval (0..this many minutes, fixed per company) so every company does
        /// not run at the same moment after a restart.
        /// </summary>
        public int JitterMinutes { get; set; } = 45;

        /// <summary>How often the worker wakes to look for companies that are due.</summary>
        public int TickSeconds { get; set; } = 300;

        /// <summary>
        /// Look-back for a connection that has never been synced (first connection, or reconnection): long enough to
        /// cover the documents approved during the connection itself, short enough not to fiscalise history.
        /// </summary>
        public int InitialLookbackMinutes { get; set; } = 60;

        /// <summary>Each run starts this long before the last successful run so adjacent runs overlap; dedup drops the repeats.</summary>
        public int OverlapMinutes { get; set; } = 10;

        /// <summary>Pause between companies within one tick, to spread load.</summary>
        public int DelayBetweenCompaniesSeconds { get; set; } = 2;

        /// <summary>
        /// Most pages (of 100 documents) one run reads per document type. Xero allows an organisation as few as
        /// 1,000 calls a day, so a run never reads without limit; what is left is picked up by the next run.
        /// </summary>
        public int MaxPagesPerRun { get; set; } = 10;
    }
}
