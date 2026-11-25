using FiscalizeXero.Api.Data;
using FiscalizeXero.Shared.Models.Fiscalize;

namespace FiscalizeXero.Api.Services.DeviceConfig
{
    public interface IDeviceConfigService
    {
        Task<List<AppUserDeviceConfig>> GetAllAsync(int appUserCompanyId);
        Task<AppUserDeviceConfig?> GetDefaultAsync(int appUserCompanyId);
        /// <returns>The saved configuration and, when the platform rejected the key on the test login, why.</returns>
        Task<(AppUserDeviceConfig Config, string? Warning)> SaveAsync(int appUserCompanyId, SaveDeviceConfigModel model);
        Task<AppUserDeviceConfig?> SetDefaultAsync(int appUserCompanyId, int configId);
        Task<bool> DeleteAsync(int appUserCompanyId, int configId);
        Task UpdateCredentialsAsync(int configId, long? apiKeyId, string? apiKey, string? apiSecret);
    }
}
