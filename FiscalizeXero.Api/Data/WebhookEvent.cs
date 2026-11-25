using System.ComponentModel.DataAnnotations;

namespace FiscalizeXero.Api.Data
{
    /// <summary>
    /// One document change: delivered by a Xero webhook, or found by the backstop sync. Rows are written as soon as
    /// the POST is verified (so Xero gets its 200 within the 5-second limit) and processed afterwards by the
    /// background worker. They double as the delivery log shown on the Webhooks page.
    /// </summary>
    public class WebhookEvent
    {
        [Key]
        public long Id { get; set; }

        /// <summary>The Xero organisation the change belongs to.</summary>
        public Guid TenantId { get; set; }

        /// <summary>"Invoice" or "CreditNote". Xero's INVOICE category also covers purchase bills, told apart when the document is read.</summary>
        [StringLength(50)]
        public string EntityName { get; set; } = string.Empty;

        /// <summary>Xero's id for the document (a GUID, lower case).</summary>
        [StringLength(50)]
        public string EntityId { get; set; } = string.Empty;

        /// <summary>"Create" or "Update".</summary>
        [StringLength(20)]
        public string Operation { get; set; } = string.Empty;

        /// <summary>When Xero says the change happened, as delivered.</summary>
        [StringLength(40)]
        public string? LastUpdated { get; set; }

        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ProcessedAt { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = WebhookEventStatus.Pending;

        /// <summary>Outcome: receipt number, skip reason or error message.</summary>
        [StringLength(1000)]
        public string? Detail { get; set; }

        /// <summary>Human-readable document number once the document has been looked up.</summary>
        [StringLength(100)]
        public string? DocumentNumber { get; set; }

        /// <summary>Company that owned the connection when the event was processed.</summary>
        public int? AppUserCompanyId { get; set; }

        /// <summary>Where the change came from: "Webhook" (Xero delivery) or "Sync" (backstop run).</summary>
        [StringLength(20)]
        public string Source { get; set; } = WebhookEventSource.Webhook;
    }

    public static class WebhookEventSource
    {
        public const string Webhook = "Webhook";
        public const string Sync = "Sync";
        /// <summary>The failure retry worker trying a document again.</summary>
        public const string Retry = "Retry";
    }

    public static class WebhookEventStatus
    {
        public const string Pending = "Pending";
        public const string Processed = "Processed";
        public const string Skipped = "Skipped";
        public const string Failed = "Failed";
    }
}
