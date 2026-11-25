using FiscalizeXero.Fiscalize.Models.Auths;
using FiscalizeXero.Shared.Models.Auths;

namespace FiscalizeXero.Fiscalize.Services.Auths
{
    public class LoginService
    {
        static string _endPoint = "api/Auths/Login";

        public static async Task<LoginResponseModel> LoginAsync(FiscalizeAuthModel auth, LoginModel login)
        {
            var response = await RestClient.PostAsync<LoginModel, LoginResponseModel>(auth, _endPoint, login);
            return response;
        }

        public static async Task<LoginResponseModel> SwitchCompanyAsync(FiscalizeAuthModel auth, long companyId)
        {
            return await RestClient.GetAsync<LoginResponseModel>(auth, $"api/Auths/Companies/{companyId}");
        }

        public static async Task<LoginResponseModel> RefreshTokenAsync(string apiUrl, string refreshToken)
        {
            var auth = new FiscalizeAuthModel { ApiUrl = apiUrl, Token = null! };
            return await RestClient.PostAsync<object, LoginResponseModel>(
                auth,
                "api/Auths/RefreshToken",
                new { refreshToken }
            );
        }
    }
}
