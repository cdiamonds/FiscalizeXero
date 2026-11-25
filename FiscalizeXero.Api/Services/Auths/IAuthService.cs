using FiscalizeXero.Fiscalize.Models.Auths;
using FiscalizeXero.Shared;
using System.Security.Claims;

namespace FiscalizeXero.Api.Services.Auths
{
    public interface IAuthService
    {
        Task<ServiceResponse<LoginResponseModel>> LoginAsync(ClaimsPrincipal claim, LoginModel login);
        Task<ServiceResponse<LoginResponseModel>> SwitchCompanyAsync(ClaimsPrincipal claim, long companyId);
        Task<ServiceResponse<LoginResponseModel>> ExchangeLaunchTokenAsync(string launchToken);
        Task<ServiceResponse<string>> OpenFiscalizeAsync(ClaimsPrincipal claim);

        /// <summary>
        /// Called from the OAuth callback: pairs the Xero organisation with the Fiscalize company on the platform
        /// (provider "Xero", external identifier the tenant id), using the company's own platform session. Idempotent
        /// at the platform via UNIQUE(Provider, ExternalIdentifier): the same company again is success; a different
        /// company is 409, and the caller then undoes the connection.
        /// </summary>
        Task<ServiceResponse<bool>> EnsureExternalIdentityAsync(int appUserCompanyId, string tenantId, string? displayName);
    }
}
