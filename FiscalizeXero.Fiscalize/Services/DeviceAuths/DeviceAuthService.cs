using FiscalizeXero.Shared.Models.Auths;
using FiscalizeXero.Shared.Models.Fiscalize;

namespace FiscalizeXero.Fiscalize.Services.DeviceAuths
{
    public static class DeviceAuthService
    {
        private const string LoginEndpoint = "api/DeviceAuths/Login";
        private const string RefreshEndpoint = "api/DeviceAuths/RefreshToken";

        public static async Task<DeviceLoginResponse> LoginAsync(string apiUrl, DeviceLoginRequest request)
        {
            // Pass empty token — login endpoint does not require authentication
            var auth = new FiscalizeAuthModel { ApiUrl = apiUrl, Token = "" };
            return await RestClient.PostAsync<DeviceLoginRequest, DeviceLoginResponse>(auth, LoginEndpoint, request);
        }

        public static async Task<DeviceLoginResponse> RefreshAsync(string apiUrl, DeviceRefreshRequest request)
        {
            var auth = new FiscalizeAuthModel { ApiUrl = apiUrl, Token = "" };
            return await RestClient.PostAsync<DeviceRefreshRequest, DeviceLoginResponse>(auth, RefreshEndpoint, request);
        }
    }
}
