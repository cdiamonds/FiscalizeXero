using System.Text.Json.Serialization;

namespace FiscalizeXero.Shared.Models.Fiscalize
{
    public class CreateFiscalizeApiKeyRequest
    {
        [JsonPropertyName("assignedUserEmail")]
        public string? AssignedUserEmail { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("permissions")]
        public string Permissions { get; set; } = "ReadWrite";
    }

    public class UpdateFiscalizeApiKeyStatusRequest
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
    }

    public class UpdateFiscalizeApiKeyPermissionsRequest
    {
        [JsonPropertyName("permissions")]
        public string Permissions { get; set; } = string.Empty;
    }

    public class SaveDeviceConfigModel
    {
        public long DeviceId { get; set; }
        public string? Platform { get; set; }
        public int DeviceNumber { get; set; }
        public long? ApiKeyId { get; set; }
        public string? ApiKey { get; set; }
        public string? ApiSecret { get; set; }
        public string? ApiKeyDescription { get; set; }
    }

    public class DeviceConfigModel
    {
        public int Id { get; set; }
        public long DeviceId { get; set; }
        public string? Platform { get; set; }
        public int DeviceNumber { get; set; }
        public long? ApiKeyId { get; set; }
        public string? ApiKeyDescription { get; set; }
        public bool HasApiKey => !string.IsNullOrEmpty(ApiKey);
        public string? ApiKey { get; set; }
        public bool IsDefault { get; set; }
        public DateTime DateConfigured { get; set; }
        public DateTime? DateUpdated { get; set; }
        /// <summary>Set when the configuration was saved but the Fiscalize platform rejected the key on a test login.</summary>
        public string? Warning { get; set; }
    }
}
