using FiscalizeXero.Xero.Converters;
using System.Text.Json.Serialization;

namespace FiscalizeXero.Xero.Models
{
    /// <summary>Paging details Xero returns with a paged list.</summary>
    public class XeroPagination
    {
        [JsonPropertyName("page")] public int Page { get; set; }
        [JsonPropertyName("pageSize")] public int PageSize { get; set; }
        [JsonPropertyName("pageCount")] public int PageCount { get; set; }
        [JsonPropertyName("itemCount")] public int ItemCount { get; set; }
    }

    /// <summary>One page of a Xero list, with the totals Xero reported for the whole list.</summary>
    public class XeroPage<T>
    {
        public List<T> Items { get; set; } = new();
        public int Page { get; set; } = 1;
        public int PageSize { get; set; }
        public int PageCount { get; set; }
        public int ItemCount { get; set; }
    }

    /// <summary>
    /// An organisation the signed-in Xero user has connected to this app (<c>GET https://api.xero.com/connections</c>).
    /// <see cref="Id"/> is the connection, used to disconnect; <see cref="TenantId"/> is the organisation, sent on
    /// every API call.
    /// </summary>
    public class XeroConnection
    {
        [JsonPropertyName("id")] public Guid Id { get; set; }
        [JsonPropertyName("authEventId")] public Guid? AuthEventId { get; set; }
        [JsonPropertyName("tenantId")] public Guid TenantId { get; set; }
        /// <summary>"ORGANISATION" for an accounting organisation; "PRACTICE" and others are not usable here.</summary>
        [JsonPropertyName("tenantType")] public string? TenantType { get; set; }
        [JsonPropertyName("tenantName")] public string? TenantName { get; set; }
        [JsonPropertyName("createdDateUtc")] public DateTime? CreatedDateUtc { get; set; }
        [JsonPropertyName("updatedDateUtc")] public DateTime? UpdatedDateUtc { get; set; }

        [JsonIgnore]
        public bool IsOrganisation => string.Equals(TenantType, "ORGANISATION", StringComparison.OrdinalIgnoreCase);
    }

    public class Organisation
    {
        public Guid OrganisationID { get; set; }
        public string? Name { get; set; }
        public string? LegalName { get; set; }
        /// <summary>Short code used in Xero's own web addresses, e.g. <c>!23eYt</c>.</summary>
        public string? ShortCode { get; set; }
        public string? BaseCurrency { get; set; }
        public string? CountryCode { get; set; }
        public string? TaxNumber { get; set; }
        public string? RegistrationNumber { get; set; }
        public bool IsDemoCompany { get; set; }
        public string? OrganisationStatus { get; set; }
        public string? Timezone { get; set; }
        /// <summary>The default for new sales documents: "INCLUSIVE", "EXCLUSIVE" or "NONE".</summary>
        public string? DefaultSalesTax { get; set; }
        /// <summary>The subscription: "DEMO", "TRIAL", "STARTER", "STANDARD", "PREMIUM", ... (shown on Connected apps for support).</summary>
        public string? Class { get; set; }
        /// <summary>"BUSINESS" or "PARTNER".</summary>
        public string? Edition { get; set; }
    }

    /// <summary>A tracking category ("Region", "Department"): Xero's nearest thing to a custom field on a document line.</summary>
    public class TrackingCategory
    {
        public Guid TrackingCategoryID { get; set; }
        public string? Name { get; set; }
        /// <summary>"ACTIVE", "ARCHIVED" or "DELETED".</summary>
        public string? Status { get; set; }
    }

    public class TrackingCategoriesEnvelope
    {
        public List<TrackingCategory>? TrackingCategories { get; set; }
    }

    public class OrganisationsEnvelope
    {
        public List<Organisation>? Organisations { get; set; }
    }

    /// <summary>A line in a document's "History and notes". Only <see cref="Details"/> is sent when adding a note.</summary>
    public class HistoryRecord
    {
        public string? Details { get; set; }
        public string? Changes { get; set; }
        public string? User { get; set; }
        [JsonConverter(typeof(XeroDateConverter))]
        public DateTime? DateUTC { get; set; }
    }

    public class HistoryRecordsEnvelope
    {
        public List<HistoryRecord>? HistoryRecords { get; set; }
    }

    public class Attachment
    {
        public Guid AttachmentID { get; set; }
        public string? FileName { get; set; }
        public string? Url { get; set; }
        public string? MimeType { get; set; }
        public long? ContentLength { get; set; }
        /// <summary>Whether the customer sees the file on the online invoice.</summary>
        public bool? IncludeOnline { get; set; }
    }

    public class AttachmentsEnvelope
    {
        public List<Attachment>? Attachments { get; set; }
    }

    public class TaxRate
    {
        public string? Name { get; set; }
        /// <summary>The code used on lines, e.g. "OUTPUT", "NONE" or "TAX001" for a custom rate.</summary>
        public string? TaxType { get; set; }
        /// <summary>"ACTIVE", "DELETED" or "ARCHIVED".</summary>
        public string? Status { get; set; }
        /// <summary>The rate in force, all components combined, as a percentage.</summary>
        public decimal? EffectiveRate { get; set; }
        public decimal? DisplayTaxRate { get; set; }
        public string? ReportTaxType { get; set; }
        public bool? CanApplyToRevenue { get; set; }
        public List<TaxComponent>? TaxComponents { get; set; }

        [JsonIgnore]
        public bool IsActive => string.Equals(Status, "ACTIVE", StringComparison.OrdinalIgnoreCase);
    }

    public class TaxComponent
    {
        public string? Name { get; set; }
        public decimal? Rate { get; set; }
        public bool? IsCompound { get; set; }
    }

    public class TaxRatesEnvelope
    {
        public List<TaxRate>? TaxRates { get; set; }
    }

    public class Item
    {
        public Guid ItemID { get; set; }
        public string? Code { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public bool? IsSold { get; set; }
        public bool? IsPurchased { get; set; }
        public bool? IsTrackedAsInventory { get; set; }
        public ItemPriceDetails? SalesDetails { get; set; }
        [JsonConverter(typeof(XeroDateConverter))]
        public DateTime? UpdatedDateUTC { get; set; }
    }

    public class ItemPriceDetails
    {
        public decimal? UnitPrice { get; set; }
        public string? AccountCode { get; set; }
        public string? TaxType { get; set; }
    }

    public class ItemsEnvelope
    {
        public List<Item>? Items { get; set; }
    }
}
