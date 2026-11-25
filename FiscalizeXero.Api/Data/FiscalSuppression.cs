using System.ComponentModel.DataAnnotations;

namespace FiscalizeXero.Api.Data
{
    /// <summary>
    /// A document the automatic triggers (webhooks, the sync, failure retries) must leave alone. Written when a user
    /// fiscally cancels an invoice: the cancellation edits the invoice in Xero, which would otherwise come straight
    /// back through a webhook and be sent to ZIMRA again with the same data. Only an explicit user action
    /// (Fiscalize on the document) removes it.
    /// </summary>
    public class FiscalSuppression
    {
        [Key]
        public long Id { get; set; }

        public int AppUserCompanyId { get; set; }

        [StringLength(50)]
        public string DocumentId { get; set; } = string.Empty;

        [StringLength(20)]
        public string DocumentType { get; set; } = string.Empty;

        [StringLength(200)]
        public string Reason { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
