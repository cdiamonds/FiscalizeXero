namespace FiscalizeXero.Shared.Models.Fiscalize
{
    /// <summary>
    /// Result of a fiscalization attempt that controllers/UI render. Captures both the persisted
    /// FiscalRecord-style fields and any warnings from the writeback step (e.g. the Xero note could not be added
    /// but fiscalization itself succeeded).
    /// </summary>
    public class FiscalizeResultModel
    {
        /// <summary>The Xero document id (a GUID, lower case).</summary>
        public string DocumentId { get; set; } = string.Empty;
        public string DocumentNumber { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string? VerificationCode { get; set; }
        public string? QrCodeUrl { get; set; }
        public long? ReceiptNumber { get; set; }
        public long? GlobalNumber { get; set; }
        public int DeviceId { get; set; }
        public int? FiscalDayNumber { get; set; }
        public string ValidationError { get; set; } = string.Empty;
        public DateTime FiscalizedAt { get; set; }
        public string? WritebackWarning { get; set; }
        /// <summary>True when the platform reported the document was already fiscalized; the stored data was refreshed from it.</summary>
        public bool AlreadyFiscalized { get; set; }
        /// <summary>Set when the stamped PDF could not be attached to the Xero document; fiscalization itself succeeded.</summary>
        public string? AttachmentWarning { get; set; }
        /// <summary>The stamped PDF is being attached in the background; check the attachment status to see it land.</summary>
        public bool AttachmentQueued { get; set; }
    }
}
