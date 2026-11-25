namespace FiscalizeXero.Shared.Models.Dashboard
{
    /// <summary>Everything the dashboard shows, read from this service's own database (no Xero call).</summary>
    public class DashboardSummaryModel
    {
        public DashboardConnectionModel Connection { get; set; } = new();
        public DashboardDeviceModel? Device { get; set; }
        public DashboardCountsModel Counts { get; set; } = new();

        /// <summary>Documents fiscalised per day for the last 14 days in Zimbabwe time, oldest first.</summary>
        public List<DashboardDayModel> Daily { get; set; } = new();

        /// <summary>Documents that failed to fiscalise and have not fiscalised since, newest first (at most 5).</summary>
        public List<DashboardFailureModel> Failures { get; set; } = new();
        public int FailureCount { get; set; }

        /// <summary>The latest fiscal receipts, newest first (at most 8).</summary>
        public List<DashboardReceiptModel> Recent { get; set; } = new();
    }

    public class DashboardConnectionModel
    {
        public bool Connected { get; set; }
        public string? TenantName { get; set; }
        public bool AutoFiscalizeInvoices { get; set; }
        public bool AutoFiscalizeCreditNotes { get; set; }
        public bool SyncEnabled { get; set; }
        public double SyncIntervalHours { get; set; }
        /// <summary>When the periodic safety-net check last completed (UTC).</summary>
        public DateTime? LastSyncAt { get; set; }
        /// <summary>Why the last check failed; null when it last succeeded.</summary>
        public string? LastSyncError { get; set; }
        /// <summary>The server can verify Xero webhook deliveries (Xero:WebhookKey is set).</summary>
        public bool WebhooksConfigured { get; set; }
        public DateTime? LastWebhookAt { get; set; }
    }

    public class DashboardDeviceModel
    {
        public int DeviceNumber { get; set; }
        public long DeviceId { get; set; }
        /// <summary>"test" when the latest receipt verifies on ZIMRA's test system, "production", or null before any receipt.</summary>
        public string? Environment { get; set; }
    }

    public class DashboardCountsModel
    {
        public int Today { get; set; }
        public int Last7Days { get; set; }
        public int ThisMonth { get; set; }
        public int InvoicesThisMonth { get; set; }
        public int CreditNotesThisMonth { get; set; }
        public int Total { get; set; }
    }

    public class DashboardDayModel
    {
        /// <summary>yyyy-MM-dd, Zimbabwe time.</summary>
        public string Date { get; set; } = string.Empty;
        public int Invoices { get; set; }
        public int CreditNotes { get; set; }
    }

    public class DashboardFailureModel
    {
        public string DocumentId { get; set; } = string.Empty;
        /// <summary>"invoice" or "creditnote".</summary>
        public string DocumentType { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int Attempts { get; set; }
        public DateTime LastFailedAt { get; set; }
    }

    public class DashboardReceiptModel
    {
        public string DocumentId { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string DocumentNumber { get; set; } = string.Empty;
        public long? ReceiptNumber { get; set; }
        public string? VerificationCode { get; set; }
        public DateTime FiscalizedAt { get; set; }
    }
}
