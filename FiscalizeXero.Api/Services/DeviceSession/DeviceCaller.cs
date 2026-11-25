using System.Net;
using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.DeviceConfig;
using FiscalizeXero.Fiscalize.Exceptions;
using FiscalizeXero.Shared.Models.Auths;

namespace FiscalizeXero.Api.Services.DeviceSession
{
    /// <summary>
    /// Calls the Fiscalize platform as a company's default ZIMRA device, for the best-effort work around fiscalisation
    /// (layouts, Connected apps reports). Logs in with the device key and re-logs in once when the platform answers 401.
    ///
    /// It deliberately never rotates the device's API key: that recovery belongs to fiscalisation
    /// (FiscalizationService.CallAsDeviceAsync), which runs at the next document anyway. A failure here is logged by the
    /// caller and tried again later; it never stops a document.
    /// </summary>
    public interface IDeviceCaller
    {
        /// <summary>The company's default device with an API key, or null when it has none yet.</summary>
        Task<AppUserDeviceConfig?> FindDeviceAsync(int appUserCompanyId);

        Task<T> CallAsync<T>(AppUserDeviceConfig device, Func<FiscalizeAuthModel, Task<T>> call, CancellationToken ct = default);
    }

    public class DeviceCaller : IDeviceCaller
    {
        private readonly IDeviceSessionService _sessions;
        private readonly IDeviceConfigService _devices;
        private readonly ILogger<DeviceCaller> _logger;

        public DeviceCaller(IDeviceSessionService sessions, IDeviceConfigService devices, ILogger<DeviceCaller> logger)
        {
            _sessions = sessions;
            _devices = devices;
            _logger = logger;
        }

        public async Task<AppUserDeviceConfig?> FindDeviceAsync(int appUserCompanyId)
        {
            var device = await _devices.GetDefaultAsync(appUserCompanyId);
            return device == null || string.IsNullOrEmpty(device.ApiKey) || string.IsNullOrEmpty(device.ApiSecret) ? null : device;
        }

        public async Task<T> CallAsync<T>(AppUserDeviceConfig device, Func<FiscalizeAuthModel, Task<T>> call, CancellationToken ct = default)
        {
            var auth = await _sessions.GetAuthAsync(device, ct);
            try
            {
                return await call(auth);
            }
            catch (FiscalizeApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogInformation("Platform refused device #{DeviceNumber} session (401); signing in again once", device.DeviceNumber);
                auth = await _sessions.ReloginAsync(device, ct);
                return await call(auth);
            }
        }
    }
}
