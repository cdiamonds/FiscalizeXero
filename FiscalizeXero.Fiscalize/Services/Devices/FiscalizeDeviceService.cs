using FiscalizeXero.Shared.Models.Auths;
using FiscalizeXero.Shared.Models.Fiscalize;

namespace FiscalizeXero.Fiscalize.Services.Devices
{
    /// <summary>
    /// Service for managing devices via the Fiscalize API
    /// </summary>
    public class FiscalizeDeviceService
    {
        private const string Endpoint = "api/Devices";

        /// <summary>
        /// Get all devices for the current user's company
        /// </summary>
        /// <param name="auth">Fiscalize authentication model with API URL and bearer token</param>
        /// <returns>List of devices</returns>
        public static async Task<List<FiscalizeDevice>> GetAllAsync(FiscalizeAuthModel auth)
        {
            return await RestClient.GetAsync<List<FiscalizeDevice>>(auth, $"{Endpoint}/List");
        }

        /// <summary>
        /// Get a specific device by ID
        /// </summary>
        /// <param name="auth">Fiscalize authentication model</param>
        /// <param name="deviceId">Device ID</param>
        /// <returns>Device details</returns>
        public static async Task<FiscalizeDevice> GetByIdAsync(FiscalizeAuthModel auth, long deviceId)
        {
            return await RestClient.GetAsync<FiscalizeDevice>(auth, $"{Endpoint}/{deviceId}");
        }

        /// <summary>
        /// Get the default device for fiscalization
        /// </summary>
        /// <param name="auth">Fiscalize authentication model</param>
        /// <returns>Default device or null if none found</returns>
        public static async Task<FiscalizeDevice?> GetDefaultAsync(FiscalizeAuthModel auth)
        {
            var devices = await GetAllAsync(auth);
            return devices.FirstOrDefault(d => d.IsDefault && d.IsFiscalizationEnabled);
        }
    }
}
