using FiscalizeXero.Shared.Models.Auths;
using FiscalizeXero.Shared.Models.Fiscalize;

namespace FiscalizeXero.Fiscalize.Services.DeviceApiKeys
{
    /// <summary>
    /// Service for managing device API keys via the Fiscalize API
    /// </summary>
    public class FiscalizeDeviceApiKeyService
    {
        private const string Endpoint = "api/deviceapikeys";

        public static async Task<List<FiscalizeDeviceApiKey>> GetByDeviceAsync(FiscalizeAuthModel auth, long deviceId)
        {
            return await RestClient.GetAsync<List<FiscalizeDeviceApiKey>>(auth, $"{Endpoint}/Devices/{deviceId}");
        }

        public static async Task<FiscalizeDeviceApiKey> CreateAsync(FiscalizeAuthModel auth, long deviceId, CreateFiscalizeApiKeyRequest request)
        {
            return await RestClient.PostAsync<CreateFiscalizeApiKeyRequest, FiscalizeDeviceApiKey>(
                auth, $"{Endpoint}/Devices/{deviceId}", request);
        }

        public static async Task<FiscalizeDeviceApiKey> GenerateNewKeyAsync(FiscalizeAuthModel auth, long apiKeyId)
        {
            return await RestClient.PostAsync<object, FiscalizeDeviceApiKey>(
                auth, $"{Endpoint}/{apiKeyId}/GenerateNewKey", new { });
        }

        public static async Task<bool> UpdateStatusAsync(FiscalizeAuthModel auth, long apiKeyId, UpdateFiscalizeApiKeyStatusRequest request)
        {
            return await RestClient.PostAsync<UpdateFiscalizeApiKeyStatusRequest, bool>(
                auth, $"{Endpoint}/{apiKeyId}/Statuses", request);
        }

        public static async Task<bool> UpdatePermissionsAsync(FiscalizeAuthModel auth, long apiKeyId, UpdateFiscalizeApiKeyPermissionsRequest request)
        {
            return await RestClient.PostAsync<UpdateFiscalizeApiKeyPermissionsRequest, bool>(
                auth, $"{Endpoint}/{apiKeyId}/Permissions", request);
        }

        public static async Task<bool> RevokeAsync(FiscalizeAuthModel auth, long apiKeyId)
        {
            return await RestClient.DeleteAsync<bool>(auth, $"{Endpoint}/{apiKeyId}");
        }

        public static async Task<bool> TestAsync(FiscalizeAuthModel auth, long apiKeyId)
        {
            return await RestClient.PostAsync<object, bool>(
                auth, $"{Endpoint}/{apiKeyId}/Test", new { });
        }
    }
}
