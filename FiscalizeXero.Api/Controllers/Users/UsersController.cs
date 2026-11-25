using FiscalizeXero.Api.Services.AppUserCompanies;
using FiscalizeXero.Api.Services.AppUsers;
using FiscalizeXero.Api.Services.Auths;
using FiscalizeXero.Api.Services.ClientConfigs;
using FiscalizeXero.Api.Services.CurrentUser;
using FiscalizeXero.Api.Services.XeroAuth;
using FiscalizeXero.Api.Services.FiscalizeSession;
using FiscalizeXero.Shared.Models;
using FiscalizeXero.Shared.Models.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Net;

namespace FiscalizeXero.Api.Controllers.Users
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private readonly ICurrentUserService _currentUserService;
        private readonly IClientConfigService _clientConfigService;
        private readonly IAuthService _authService;
        private readonly IAppUserService _appUserService;
        private readonly IAppUserCompanyService _appUserCompanyService;
        private readonly XeroOptions _xero;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IXeroAuthService _xeroAuthService;
        private readonly IFiscalizeSessionService _fiscalizeSession;

        public UsersController(
            ICurrentUserService currentUserService,
            IClientConfigService clientConfigService,
            IAuthService authService,
            IAppUserService appUserService,
            IAppUserCompanyService appUserCompanyService,
            IOptions<XeroOptions> xeroOptions,
            IHttpClientFactory httpClientFactory,
            IXeroAuthService xeroAuthService,
            IFiscalizeSessionService fiscalizeSession)
        {
            _xeroAuthService = xeroAuthService;
            _fiscalizeSession = fiscalizeSession;
            _currentUserService = currentUserService;
            _clientConfigService = clientConfigService;
            _authService = authService;
            _appUserService = appUserService;
            _appUserCompanyService = appUserCompanyService;
            _xero = xeroOptions.Value;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Get all companies associated with the current user
        /// </summary>
        [HttpGet("me/companies")]
        [ProducesResponseType(typeof(IEnumerable<UserCompanyModel>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<IEnumerable<UserCompanyModel>>> GetMyCompanies()
        {
            var user = await _currentUserService.GetCurrentUserAsync();
            if (user == null)
                return Unauthorized();

            var result = new List<UserCompanyModel>();
            foreach (var company in user.Companies)
            {
                // One connection per Fiscalize company; "shared" just means a colleague set it up.
                var clientConfig = await _clientConfigService.GetForCompanyAsync(company.CompanyId);
                var isUsingSharedConnection = clientConfig != null && clientConfig.AppUserCompanyId != company.Id;

                var isSetupComplete = clientConfig != null && !string.IsNullOrEmpty(clientConfig.RefreshToken);
                var scopeOutdated = isSetupComplete && clientConfig!.GrantedScopeVersion < _xero.ScopeVersion;

                // A connection whose access token has lapsed and cannot be refreshed (refresh token revoked or expired,
                // or no Xero app credentials to refresh with) is dead until the user re-authorises. Checking here,
                // on the company list, is what turns that into a visible "Re-authorize" instead of failing pages.
                string? connectionProblem = null;
                if (isSetupComplete &&
                    (clientConfig!.TokenExpiresAt == null || clientConfig.TokenExpiresAt.Value <= DateTime.UtcNow.AddMinutes(5)))
                {
                    var accessToken = await _xeroAuthService.GetValidAccessTokenAsync(clientConfig);
                    if (string.IsNullOrEmpty(accessToken))
                        connectionProblem = "The Xero connection has expired and could not be refreshed. Reconnect Xero to keep fiscalizing.";
                }
                // Xero refused the refresh token (invalid_grant: the user removed the app inside Xero, or the token sat
                // unused for 60 days) and the tokens were cleared, but the organisation is still known. That includes a
                // refresh that failed just above.
                if (clientConfig?.TenantId != null && string.IsNullOrEmpty(clientConfig.RefreshToken))
                {
                    isSetupComplete = false;
                    connectionProblem = "Xero has disconnected Fiscalize, or the authorisation expired. Reconnect Xero to keep fiscalizing.";
                }
                var needsReauth = scopeOutdated || connectionProblem != null;

                result.Add(new UserCompanyModel
                {
                    Id = company.Id,
                    CompanyId = company.CompanyId,
                    CompanyName = company.CompanyName ?? string.Empty,
                    IsActive = user.ActiveCompanyId == company.Id,
                    IsSetupComplete = isSetupComplete,
                    IsUsingSharedConnection = isUsingSharedConnection,
                    XeroTenantId = clientConfig?.TenantId,
                    XeroTenantName = clientConfig?.TenantName,
                    XeroUrl = clientConfig?.TenantId != null ? XeroOptions.XeroWebUrl : null,
                    NeedsReauth = needsReauth,
                    ConnectionProblem = connectionProblem ?? (scopeOutdated ? "Xero needs new permissions for Fiscalize. Re-authorize Xero to continue." : null)
                });
            }

            return Ok(result);
        }

        /// <summary>
        /// Proxy: fetch the company list from Fiscalize using the stored Fiscalize token
        /// </summary>
        [HttpGet("me/fiscalize-companies")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult> GetMyFiscalizeCompanies(
            [FromQuery] string search = "",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 100)
        {
            var user = await _currentUserService.GetCurrentUserAsync();
            if (user == null)
                return Unauthorized();

            // 424, not 401: a lapsed Fiscalize platform session must not sign the user out of this app (the browser
            // treats a 401 from this app as "your session is over"). The company switcher keeps
            // working from the companies this app already knows and shows why the full list is unavailable.
            const int FailedDependency = 424;
            var expired = new ErrorModel { Code = FailedDependency, Message = "Your Fiscalize sign-in has expired. Sign in again to see every company on your account." };
            // The list belongs to the user, so their own sign-in is enough (it's all a user has before choosing a
            // company); otherwise the active company's session. A lapsed token is refreshed silently.
            var auth = await _fiscalizeSession.TryGetUserAuthAsync(user)
                ?? (user.ActiveCompany != null ? await _fiscalizeSession.TryGetAuthForCompanyAsync(user.ActiveCompany) : null);
            if (auth == null) return StatusCode(FailedDependency, expired);

            var url = $"/api/CompanyUsers/MyCompanies?Search={Uri.EscapeDataString(search)}&Page={page}&PageSize={pageSize}";
            var response = await CallPlatformAsync(url, auth.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // The token looked valid but the platform disagrees (revoked, or the clocks differ): refresh once and retry.
                auth = user.ActiveCompany != null ? await _fiscalizeSession.TryRefreshAsync(user.ActiveCompany) : null;
                if (auth == null) return StatusCode(FailedDependency, expired);
                response = await CallPlatformAsync(url, auth.Token);
            }
            var content = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return StatusCode(FailedDependency, expired);
            if (!response.IsSuccessStatusCode)
                return StatusCode(StatusCodes.Status502BadGateway, new ErrorModel { Code = 502, Message = $"The Fiscalize platform could not list your companies ({(int)response.StatusCode})." });
            return Content(content, "application/json");
        }

        private Task<HttpResponseMessage> CallPlatformAsync(string url, string token)
        {
            var client = _httpClientFactory.CreateClient("Fiscalize");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            return client.GetAsync(url);
        }

        /// <summary>
        /// Get the current user's preferences
        /// </summary>
        [HttpGet("me/preferences")]
        [ProducesResponseType(typeof(UserPreferenceModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<UserPreferenceModel>> GetMyPreferences()
        {
            var user = await _currentUserService.GetCurrentUserAsync();
            if (user == null)
                return Unauthorized();

            return Ok(new UserPreferenceModel { Theme = user.Theme ?? "system" });
        }

        /// <summary>
        /// Update the current user's preferences
        /// </summary>
        [HttpPut("me/preferences")]
        [ProducesResponseType(typeof(UserPreferenceModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<UserPreferenceModel>> UpdateMyPreferences([FromBody] UserPreferenceModel request)
        {
            var user = await _currentUserService.GetCurrentUserAsync();
            if (user == null)
                return Unauthorized();

            var validThemes = new[] { "light", "dark", "system" };
            user.Theme = validThemes.Contains(request.Theme) ? request.Theme : "system";
            await _appUserService.UpdateAsync(user);

            return Ok(new UserPreferenceModel { Theme = user.Theme });
        }

        /// <summary>
        /// Disconnect (remove) a company from the current user's account
        /// </summary>
        [HttpDelete("me/companies/{companyId}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult> DisconnectCompany(long companyId)
        {
            var user = await _currentUserService.GetCurrentUserAsync();
            if (user == null)
                return Unauthorized();

            var company = user.Companies.FirstOrDefault(c => c.CompanyId == companyId);
            if (company == null)
                return NotFound(new ErrorModel { Code = 404, Message = "Company not found" });

            if (user.ActiveCompanyId == company.Id)
                return BadRequest(new ErrorModel
                {
                    Code = 400,
                    Message = "Cannot disconnect the active company. Switch to another company first."
                });

            if (user.Companies.Count <= 1)
                return BadRequest(new ErrorModel
                {
                    Code = 400,
                    Message = "Cannot disconnect the only company on your account."
                });

            // The company's shared Xero connection, devices and fiscal records may be stored under this user's
            // row; removing it would delete them for every colleague (the delete cascades), so refuse.
            var companyConfig = await _clientConfigService.GetForCompanyAsync(company.CompanyId);
            if (companyConfig?.AppUserCompanyId == company.Id && await _appUserCompanyService.CountMembersAsync(company.CompanyId) > 1)
                return Conflict(new ErrorModel
                {
                    Code = 409,
                    Message = "Colleagues use this company's Xero connection, devices and fiscal records, which are kept under your account. They stay connected while you remain a member."
                });

            // Cascades to this row's connection, devices and records.
            await _appUserCompanyService.DeleteAsync(company.Id);

            return NoContent();
        }

        /// <summary>
        /// Switch the active company for the current user and receive a new token
        /// </summary>
        [HttpPost("me/companies/{companyId}/switch")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorModel), StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult> SwitchCompany(long companyId)
        {
            var response = await _authService.SwitchCompanyAsync(User, companyId);

            if (response.Status != HttpStatusCode.OK || response.Data == null)
            {
                if (response.Status == HttpStatusCode.NotFound)
                    return NotFound(new ErrorModel { Code = 404, Message = response.Message ?? "Company not found" });
                // The user's platform sign-in has timed out: the browser asks them to sign in again.
                if (response.Status == HttpStatusCode.Unauthorized)
                    return StatusCode(401, new { code = 401, message = FiscalizeXero.Api.Services.FiscalizeSession.FiscalizeSessionService.TimedOutMessage, fiscalizeSessionExpired = true });

                return StatusCode((int)response.Status, new ErrorModel { Code = (int)response.Status, Message = response.Message ?? "An error occurred" });
            }

            return Ok(response.Data);
        }
    }
}
