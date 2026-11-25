using System.Text.Json.Serialization;
using FiscalizeXero.Shared.Converters;

namespace FiscalizeXero.Shared.Models.Fiscalize
{
    /// <summary>
    /// Device model from Fiscalize API
    /// </summary>
    public class FiscalizeDevice
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("companyId")]
        public long CompanyId { get; set; }

        [JsonPropertyName("deviceID")]
        public int DeviceID { get; set; }

        [JsonPropertyName("activationKey")]
        public string? ActivationKey { get; set; }

        [JsonPropertyName("tradeName")]
        public string? TradeName { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("branchEmail")]
        public string? BranchEmail { get; set; }

        [JsonPropertyName("phoneNumber")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("modelName")]
        public string? ModelName { get; set; }

        [JsonPropertyName("modelVersion")]
        public string? ModelVersion { get; set; }

        [JsonPropertyName("serialNumber")]
        public string? SerialNumber { get; set; }

        [JsonPropertyName("supplier")]
        public string? Supplier { get; set; }

        [JsonPropertyName("status")]
        [JsonConverter(typeof(NumberToStringConverter))]
        public string? Status { get; set; }

        [JsonPropertyName("platform")]
        [JsonConverter(typeof(NumberToStringConverter))]
        public string? Platform { get; set; }

        [JsonPropertyName("region")]
        [JsonConverter(typeof(NumberToStringConverter))]
        public string? Region { get; set; }

        [JsonPropertyName("station")]
        [JsonConverter(typeof(NumberToStringConverter))]
        public string? Station { get; set; }

        [JsonPropertyName("isDefualt")]
        public bool IsDefault { get; set; }

        [JsonPropertyName("shortName")]
        public string? ShortName { get; set; }

        [JsonPropertyName("isFiscalizationEnabled")]
        public bool IsFiscalizationEnabled { get; set; }

        [JsonPropertyName("billedUntilDate")]
        public DateTime? BilledUntilDate { get; set; }

        [JsonPropertyName("dateCreated")]
        public DateTime DateCreated { get; set; }

        [JsonPropertyName("dateUpdated")]
        public DateTime? DateUpdated { get; set; }

        [JsonPropertyName("address")]
        public FiscalizeDeviceAddress? Address { get; set; }

        [JsonPropertyName("apiKeys")]
        public List<FiscalizeDeviceApiKey>? ApiKeys { get; set; }
    }

    /// <summary>
    /// Device address
    /// </summary>
    public class FiscalizeDeviceAddress
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("deviceId")]
        public long DeviceId { get; set; }

        [JsonPropertyName("houseNumber")]
        public string? HouseNumber { get; set; }

        [JsonPropertyName("street")]
        public string? Street { get; set; }

        [JsonPropertyName("surburb")]
        public string? Suburb { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("province")]
        public string? Province { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }
    }

    /// <summary>
    /// Device API key
    /// </summary>
    public class FiscalizeDeviceApiKey
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("deviceId")]
        public long DeviceId { get; set; }

        [JsonPropertyName("assignedUserId")]
        public long AssignedUserId { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("apiKey")]
        public string? ApiKey { get; set; }

        [JsonPropertyName("apiSecret")]
        public string? ApiSecret { get; set; }

        [JsonPropertyName("status")]
        [JsonConverter(typeof(NumberToStringConverter))]
        public string? Status { get; set; }

        [JsonPropertyName("permissions")]
        [JsonConverter(typeof(NumberToStringConverter))]
        public string? Permissions { get; set; }

        [JsonPropertyName("createdById")]
        public long CreatedById { get; set; }

        [JsonPropertyName("dateCreated")]
        public DateTime DateCreated { get; set; }

        [JsonPropertyName("dateUpdated")]
        public DateTime? DateUpdated { get; set; }
    }
}
