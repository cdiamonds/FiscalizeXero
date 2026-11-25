using FiscalizeXero.Fiscalize.Models.AppLaunchers;
using FiscalizeXero.Shared.Models.Auths;

namespace FiscalizeXero.Fiscalize.Services.AppLaunchers
{
    public class AppLauncherService
    {
        // appType 1 is inherited from the product this app was copied from. The Fiscalize platform does not know a Xero
        // app type yet; confirm the number with the platform before relying on "Open Fiscalize".
        public static async Task<LaunchTokenValidationResult> ValidateLaunchTokenAsync(FiscalizeAuthModel auth, string token)
        {
            return await RestClient.PostAsync<ValidateLaunchTokenRequest, LaunchTokenValidationResult>(
                auth,
                "api/AppLaunchers/Validate",
                new ValidateLaunchTokenRequest { Token = token }
            );
        }

        public static async Task<AppLaunchModel> GenerateReverseTokenAsync(FiscalizeAuthModel auth, int appType = 1)
        {
            return await RestClient.PostAsync<object, AppLaunchModel>(
                auth,
                $"api/AppLaunchers/ReverseToken/{appType}",
                new { }
            );
        }
    }
}
