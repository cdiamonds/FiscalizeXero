using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace FiscalizeXero.Api.Data
{
    /// <summary>
    /// A Fiscalize company's connection to one Xero organisation, with that company's fiscalisation settings.
    /// One row per company (see <see cref="TenantGuards"/>); it is stored under the company's anchor
    /// <see cref="AppUserCompany"/> row.
    /// </summary>
    public class ClientConfig
    {
        [Key]
        public long Id { get; set; }

        [ForeignKey("AppUserCompanyId")]
        [JsonIgnore] // the owning colleague's row (email, Fiscalize session): never sent to the browser with the config
        public AppUserCompany AppUserCompany { get; set; } = null!;
        public int AppUserCompanyId { get; set; }

        /// <summary>The Xero organisation (tenant) this company is connected to. Kept when the tokens are lost, so the UI can say "reconnect".</summary>
        public Guid? TenantId { get; set; }

        /// <summary>The organisation's name as Xero reported it when it was connected.</summary>
        [StringLength(200, ErrorMessage = "The property {0} should have {1} maximum characters")]
        public string? TenantName { get; set; }

        /// <summary>
        /// Xero's id for the connection between this app and the organisation. Disconnecting deletes exactly this
        /// connection; the refresh token is never revoked, because that would remove every organisation the same Xero
        /// user connected.
        /// </summary>
        public Guid? XeroConnectionId { get; set; }

        [JsonIgnore] // never sent to the browser (GET ClientConfigs/current returns this entity)
        public string? RefreshToken { get; set; } // encrypted at rest; unbounded because ciphertext is longer than plaintext

        [JsonIgnore] // never sent to the browser (GET ClientConfigs/current returns this entity)
        public string? AccessToken { get; set; } // encrypted at rest; unbounded because ciphertext is longer than plaintext

        public DateTime? TokenExpiresAt { get; set; }

        public bool IsCompanyWide { get; set; }

        /// <summary>The value sent as OAuth <c>state</c> for the consent in progress; the callback finds the row by it.</summary>
        [Required(ErrorMessage = "{0} is required")]
        [JsonIgnore] // an unfinished consent could be completed by whoever knows it
        public Guid State { get; set; }

        public int GrantedScopeVersion { get; set; } = 0;

        /// <summary>
        /// The backstop sync's cursor (UTC): every approved document modified before this has been looked at. Null
        /// only before the first run of a brand-new connection. Kept across a reconnection to the same organisation,
        /// so documents approved while the connection was down are still found.
        /// </summary>
        public DateTime? LastSyncAt { get; set; }

        /// <summary>When the last backstop sync run finished successfully (UTC); what the screens show as "last check".</summary>
        public DateTime? LastSyncRunAt { get; set; }

        /// <summary>
        /// The Fiscalize user (platform user id) who completed the Xero consent for this connection. That person manages
        /// it: disconnecting, fiscalisation settings, device keys. Anyone else in the company takes over by reconnecting
        /// with their own Xero login (Xero's consent proves they have access to the organisation). Null for
        /// connections made before this rule: then every member may manage it.
        /// </summary>
        public long? ConnectedByUserId { get; set; }

        /// <summary>Their name as it was when they connected, to say who to ask.</summary>
        [System.ComponentModel.DataAnnotations.StringLength(200)]
        public string? ConnectedByName { get; set; }

        /// <summary>After a failed run, no new run before this time (UTC): exponential backoff, or Xero's daily reset.</summary>
        public DateTime? SyncRetryAt { get; set; }

        /// <summary>Why the last run failed, for the Webhooks page; null after a successful run.</summary>
        [System.ComponentModel.DataAnnotations.StringLength(500)]
        public string? LastSyncError { get; set; }

        public bool AutoFiscalizeInvoices { get; set; } = true;
        public bool AutoFiscalizeCreditNotes { get; set; } = true;

        /// <summary>The auto-fiscalise switch for a <see cref="FiscalDocumentType"/>.</summary>
        public bool AutoFiscalizes(string documentType) => documentType switch
        {
            FiscalDocumentType.CreditNote => AutoFiscalizeCreditNotes,
            _ => AutoFiscalizeInvoices
        };

        /// <summary>Add a note with the fiscal result to the document's "History and notes" in Xero (on by default).</summary>
        public bool WritebackHistoryNote { get; set; } = true;

        /// <summary>Attach the stamped fiscal PDF to the Xero document after each fiscalisation (on by default).</summary>
        public bool FiscalStampEnabled { get; set; } = true;

        /// <summary>Show the attached fiscal PDF to the customer on Xero's online invoice (on by default).</summary>
        public bool AttachmentIncludeOnline { get; set; } = true;

        /// <summary>Append a short fiscal marker to the document's Reference in Xero (off by default).</summary>
        public bool WritebackReference { get; set; } = false;

        [StringLength(50)]
        public string FiscalStampPosition { get; set; } = "bottom-right";

        public int FiscalStampMarginTop { get; set; } = 10;
        public int FiscalStampMarginBottom { get; set; } = 10;
        public int FiscalStampMarginLeft { get; set; } = 10;
        public int FiscalStampMarginRight { get; set; } = 10;

        /// <summary>
        /// Which fiscal PDF is attached to the Xero document (<see cref="FiscalPdfLayouts"/>): Xero's own PDF with the fiscal
        /// stamp (the default), the company's Fiscalize layout for Xero (else Fiscalize's standard design), or the Fiscalize
        /// platform's standard invoice. Anything other than the stamp falls back to the stamp when it cannot be made.
        /// </summary>
        [StringLength(20)]
        public string FiscalPdfLayout { get; set; } = FiscalPdfLayouts.XeroStamp;

        /// <summary>Paper for the Fiscalize layout: "A4", "80mm" or "58mm".</summary>
        [StringLength(10)]
        public string LayoutPaperSize { get; set; } = "A4";

        /// <summary>This connection's id on the platform's Connected apps page. Generated once and never changed.</summary>
        public Guid PlatformInstallationId { get; set; } = Guid.NewGuid();

        /// <summary>When the connection last reported itself to Connected apps (UTC).</summary>
        public DateTime? PlatformReportedAt { get; set; }

        // Fiscal PDFs attached since the last successful Connected apps report (added atomically, subtracted once reported).
        public int PendingPdfsWithLayout { get; set; }
        public int PendingPdfsWithStandard { get; set; }
        public int PendingPdfsStamped { get; set; }
        public int PendingPdfFallbacks { get; set; }
        public DateTime? LastPdfFallbackAt { get; set; }
        [StringLength(500)]
        public string? LastPdfFallbackReason { get; set; }

        // The organisation as Xero last described it (read at most once a day), for Connected apps.
        [StringLength(30)]
        public string? OrganisationShortCode { get; set; }
        [StringLength(10)]
        public string? OrganisationBaseCurrency { get; set; }
        [StringLength(30)]
        public string? OrganisationClass { get; set; }
        public bool OrganisationIsDemo { get; set; }
        public DateTime? OrganisationCheckedAt { get; set; }

        /// <summary>
        /// How buyer details are checked for customers with a TIN: Strict (the default) stops and lists what is
        /// missing; Lenient fills placeholders. Invalid TIN/VAT numbers are blocked either way.
        /// </summary>
        public FiscalizeXero.Shared.Models.Fiscalize.FiscalizationValidationMode FiscalizationValidationMode { get; set; }
            = FiscalizeXero.Shared.Models.Fiscalize.FiscalizationValidationMode.Strict;

        /// <summary>
        /// HS code used for any line whose item has no code of its own, and for lines with no item code at all.
        /// Null or empty means the system default (<see cref="SystemDefaultHsCode"/>) applies, so fiscalisation is never blocked.
        /// </summary>
        [StringLength(20)]
        public string? DefaultHsCode { get; set; } = SystemDefaultHsCode;

        /// <summary>The HS code every company starts with, the same one the Zoho Books integration falls back to.</summary>
        public const string SystemDefaultHsCode = "04021099";

        /// <summary>The HS code in force for items with none of their own: the company's, else the system default.</summary>
        public string EffectiveDefaultHsCode => string.IsNullOrWhiteSpace(DefaultHsCode) ? SystemDefaultHsCode : DefaultHsCode.Trim();

        /// <summary>True while the connection holds tokens that can still be refreshed.</summary>
        [JsonIgnore]
        public bool IsLive => TenantId.HasValue && !string.IsNullOrEmpty(RefreshToken);
    }
}
