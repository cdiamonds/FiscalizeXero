using FiscalizeXero.Api.Data;
using FiscalizeXero.Shared.Models.Auths;

namespace FiscalizeXero.Api.Services.DeviceSession
{
    public interface IDeviceSessionService
    {
        Task<FiscalizeAuthModel> GetAuthAsync(AppUserDeviceConfig deviceConfig, CancellationToken ct = default);
        Task<FiscalizeAuthModel> ReloginAsync(AppUserDeviceConfig deviceConfig, CancellationToken ct = default);
        void InvalidateSession(int deviceConfigId);
    }
}
