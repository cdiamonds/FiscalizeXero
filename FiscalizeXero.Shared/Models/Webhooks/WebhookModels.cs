namespace FiscalizeXero.Shared.Models.Webhooks
{
    /// <summary>Everything the Webhooks page needs to guide setup and show what has arrived.</summary>
    public class WebhookStatusModel
    {
        /// <summary>True when the server holds Xero's webhook key and can accept deliveries.</summary>
        public bool ListenerConfigured { get; set; }

        /// <summary>The exact URL to paste into the Xero developer portal as the webhook's delivery URL.</summary>
        public string CallbackUrl { get; set; } = string.Empty;

        /// <summary>Event categories to tick in the portal.</summary>
        public List<string> EventCategories { get; set; } = new() { "INVOICE", "CREDITNOTE" };

        public bool Connected { get; set; }
        public Guid? TenantId { get; set; }
        public string? TenantName { get; set; }

        public bool AutoFiscalizeInvoices { get; set; }
        public bool AutoFiscalizeCreditNotes { get; set; }

        /// <summary>The backstop sync behind webhooks.</summary>
        public bool SyncEnabled { get; set; }
        public double SyncIntervalHours { get; set; }
        /// <summary>Start of the last successful sync for the connected organisation (UTC), or null before the first run.</summary>
        public DateTime? LastSyncAt { get; set; }
        /// <summary>Why the last periodic check failed; null when it last succeeded.</summary>
        public string? LastSyncError { get; set; }
        /// <summary>When a failed check will be tried again (UTC).</summary>
        public DateTime? SyncRetryAt { get; set; }

        public DateTime? LastReceivedAt { get; set; }
        public WebhookCountsModel Last24Hours { get; set; } = new();
        public List<WebhookEventModel> Recent { get; set; } = new();
    }

    public class WebhookCountsModel
    {
        public int Received { get; set; }
        public int Processed { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public int Pending { get; set; }
    }

    public class WebhookEventModel
    {
        public long Id { get; set; }
        /// <summary>"Invoice" or "CreditNote".</summary>
        public string EntityName { get; set; } = string.Empty;
        /// <summary>The Xero document id (a GUID).</summary>
        public string EntityId { get; set; } = string.Empty;
        public string? DocumentNumber { get; set; }
        public string Operation { get; set; } = string.Empty;
        /// <summary>"Webhook" or "Sync".</summary>
        public string Source { get; set; } = "Webhook";
        public string Status { get; set; } = string.Empty;
        public string? Detail { get; set; }
        public DateTime ReceivedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
    }
}
