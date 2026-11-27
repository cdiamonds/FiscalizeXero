using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.AppUserCompanies;
using FiscalizeXero.Api.Services.AppUsers;
using FiscalizeXero.Api.Services.ClientConfigs;
using FiscalizeXero.Fiscalize.Exceptions;
using FiscalizeXero.Fiscalize.Models.Auths;
using FiscalizeXero.Fiscalize.Services.Auths;
using FiscalizeXero.Shared;
using FiscalizeXero.Shared.Models.Auths;
using FiscalizeXero.Shared.Models.Settings;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;

namespace FiscalizeXero.Api.Services.Auths
{
    public class AuthService : IAuthService
    {
        private readonly IConfiguration _configuration;
        private readonly IAppUserService _appUserService;
        private readonly IAppUserCompanyService _appUserCompanyService;
        private readonly IClientConfigService _clientConfigService;
        private readonly ILogger<AuthService> _logger;
        private readonly FiscalizeXero.Api.Services.FiscalizeSession.IFiscalizeSessionService _fiscalizeSession;
        private readonly FiscalizeXero.Api.Services.Membership.ICompanyMembershipVerifier? _membership;
        private readonly VC vc = new VC();
        private readonly FiscalizeXero.Api.Security.ISessionRevocation? _revocation;

        public AuthService(
            IConfiguration configuration,
            IAppUserService appUserService,
            IAppUserCompanyService appUserCompanyService,
            IClientConfigService clientConfigService,
            ILogger<AuthService> logger,
            FiscalizeXero.Api.Services.FiscalizeSession.IFiscalizeSessionService fiscalizeSession,
            FiscalizeXero.Api.Services.Membership.ICompanyMembershipVerifier? membership = null,
            FiscalizeXero.Api.Security.ISessionRevocation? revocation = null)
        {
            _revocation = revocation;
            _membership = membership;
            _configuration = configuration;
            _appUserService = appUserService;
            _appUserCompanyService = appUserCompanyService;
            _clientConfigService = clientConfigService;
            _logger = logger;
            _fiscalizeSession = fiscalizeSession;
        }

        AuthModel GetAuth()
        {
            var config = _configuration.GetSection("Fiscalize").Get<FiscalizeConfigModel>();
            if (config == null)
                throw new Exception("No fiscalize auth configuration found!");
            {
                return new AuthModel
                {
                    Fiscalize = new FiscalizeAuthModel
                    {
                        ApiUrl = config.ApiUrl,
                    }
                };
            }
        }

        public async Task<ServiceResponse<LoginResponseModel>> LoginAsync(ClaimsPrincipal claim, LoginModel login)
        {
            try
            {
                var authResp = GetAuth();
                var loginRes = await LoginService.LoginAsync(GetAuth().Fiscalize, login);

                // The platform doesn't always name a company on a direct sign-in, even for someone with only one. Then
                // open that company exactly as a launch from Fiscalize does, so both ways in end up in the same place.
                // With several companies the user picks one next (/select-company).
                var companyLogin = loginRes.ActiveCompany == null ? await OpenOnlyCompanyAsync(authResp.Fiscalize, loginRes) ?? loginRes : loginRes;

                // Create or get AppUser from database
                var appUser = await _appUserService.GetOrCreateAppUserAsync(companyLogin, login.Email);
                RecordGranted(appUser, companyLogin);

                // Keep the user's own platform session even when no company was chosen at sign-in: it lists their
                // companies and opens a session on the one they pick, and it renews company sessions from earlier visits.
                if (!string.IsNullOrEmpty(loginRes.Token))
                {
                    appUser.FiscalizeToken = loginRes.Token;
                    appUser.FiscalizeTokenExpiresAt = FiscalizeXero.Api.Services.FiscalizeSession.PlatformSessions.Expiry(loginRes.TokeExpiry);
                    if (!string.IsNullOrEmpty(loginRes.RefreshToken)) appUser.FiscalizeRefreshToken = loginRes.RefreshToken;
                    await _appUserService.UpdateAsync(appUser);
                }

                // Report the connected Xero organisation if an active company exists. Without a company from the platform the
                // user chooses one next, so an earlier visit's company isn't reported as the one they're signed in to.
                LoginResponseXeroOrgModel? xeroOrg = null;
                string? xeroUrl = null;
                if (companyLogin.ActiveCompany != null && appUser.ActiveCompany != null)
                {
                    var clientConfig = await GetEffectiveClientConfigAsync(appUser.ActiveCompany);
                    if (clientConfig?.TenantId != null)
                    {
                        xeroOrg = new LoginResponseXeroOrgModel
                        {
                            Id = clientConfig.TenantId.Value,
                            Name = clientConfig.TenantName,
                            IsSetupComplete = !string.IsNullOrEmpty(clientConfig.RefreshToken)
                        };
                        xeroUrl = XeroAuth.XeroOptions.XeroWebUrlFor(clientConfig.OrganisationShortCode);
                    }
                }

                return await IdentityLoginAsync(companyLogin, xeroOrg, appUser.Email, xeroUrl);
            }
            catch (FiscalizeApiException ex)
            {
                _logger.LogWarning("Fiscalize API login failed. Status: {StatusCode}, Message: {Message}",
                    ex.StatusCode, ex.Message);

                return new ServiceResponse<LoginResponseModel>
                {
                    Status = ex.StatusCode,
                    Message = ex.Message,
                    Data = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during login");

                return new ServiceResponse<LoginResponseModel>
                {
                    Status = HttpStatusCode.InternalServerError,
                    Message = "An unexpected error occurred during login",
                    Data = null
                };
            }
        }

        /// <summary>The platform just granted a session on the user's active company: count it as confirmed access.</summary>
        private void RecordGranted(AppUser appUser, LoginResponseModel granted)
        {
            if (granted.ActiveCompany != null && appUser.ActiveCompany != null && appUser.ActiveCompany.CompanyId == granted.ActiveCompany.Id
                && !string.IsNullOrEmpty(granted.Token))
                _membership?.RecordGranted(appUser.ActiveCompany);
        }

        /// <summary>
        /// After a direct sign-in that named no company: when the user has exactly one company on the platform, a session
        /// on it (as "Switch to" opens), otherwise null so the user chooses. Never fails the sign-in.
        /// </summary>
        private async Task<LoginResponseModel?> OpenOnlyCompanyAsync(FiscalizeAuthModel platform, LoginResponseModel signIn)
        {
            if (string.IsNullOrEmpty(signIn.Token)) return null;
            try
            {
                var auth = new FiscalizeAuthModel { ApiUrl = platform.ApiUrl, Token = signIn.Token };
                var companies = await CompanyUsersService.MyCompaniesAsync(auth, 1, 2);
                if (companies.Count != 1)
                {
                    _logger.LogInformation("Direct sign-in of user {UserId} named no company; they have {Count} on the platform, so they choose", signIn.UserId, companies.Count);
                    return null;
                }
                var opened = await LoginService.SwitchCompanyAsync(auth, companies[0].Id);
                if (opened?.ActiveCompany?.Id != companies[0].Id || string.IsNullOrEmpty(opened.Token))
                {
                    _logger.LogWarning("Direct sign-in of user {UserId}: opening their only company {CompanyId} answered company {Answered}; they choose instead",
                        signIn.UserId, companies[0].Id, opened?.ActiveCompany?.Id);
                    return null;
                }
                _logger.LogInformation("Direct sign-in of user {UserId} named no company; opened their only company {CompanyId}", signIn.UserId, companies[0].Id);
                if (opened.UserId == 0) opened.UserId = signIn.UserId;
                if (string.IsNullOrEmpty(opened.FullName)) opened.FullName = signIn.FullName;
                opened.Email ??= signIn.Email;
                opened.EmailConfirmed = opened.EmailConfirmed || signIn.EmailConfirmed;
                return opened;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Direct sign-in of user {UserId}: couldn't open their only company; they choose instead", signIn.UserId);
                return null;
            }
        }

        public async Task<ServiceResponse<LoginResponseModel>> SwitchCompanyAsync(ClaimsPrincipal claim, long companyId)
        {
            try
            {
                var userIdStr = claim.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!long.TryParse(userIdStr, out var userId))
                    return new ServiceResponse<LoginResponseModel> { Status = HttpStatusCode.Unauthorized, Message = "Invalid token" };

                var appUser = await _appUserService.GetByUserIdAsync(userId);
                if (appUser == null)
                    return new ServiceResponse<LoginResponseModel> { Status = HttpStatusCode.NotFound, Message = "User not found" };

                // Ask the platform with the freshest session we hold: the user's own sign-in first, else the current
                // company's session (renewed if it has lapsed). Never a raw token from an earlier visit.
                var fiscalizeAuth = await _fiscalizeSession.TryGetUserAuthAsync(appUser)
                    ?? (appUser.ActiveCompany != null ? await _fiscalizeSession.TryGetAuthForCompanyAsync(appUser.ActiveCompany) : null);
                if (fiscalizeAuth == null)
                    return new ServiceResponse<LoginResponseModel> { Status = HttpStatusCode.Unauthorized, Message = "Your Fiscalize sign-in has expired. Sign in again to choose a company." };

                LoginResponseModel fiscalizeResponse;
                try
                {
                    fiscalizeResponse = await LoginService.SwitchCompanyAsync(fiscalizeAuth, companyId);
                }
                catch (FiscalizeApiException ex)
                {
                    _logger.LogWarning("Fiscalize company switch failed. Status: {StatusCode}, Message: {Message}", ex.StatusCode, ex.Message);
                    return new ServiceResponse<LoginResponseModel> { Status = ex.StatusCode, Message = ex.Message };
                }

                // Only the company the platform just confirmed, matched by its platform id (never a local row id), and
                // only if the platform answered with the company that was asked for.
                var confirmedId = fiscalizeResponse.ActiveCompany?.Id;
                if (confirmedId == null || confirmedId.Value != companyId)
                {
                    _logger.LogWarning("Fiscalize company switch to {Requested} answered with company {Confirmed}; refused", companyId, confirmedId);
                    return new ServiceResponse<LoginResponseModel> { Status = HttpStatusCode.Forbidden, Message = "Fiscalize did not confirm access to that company." };
                }
                var targetCompany = appUser.Companies.FirstOrDefault(c => c.CompanyId == confirmedId.Value)
                    // Not in the local DB yet: create the membership now that the platform confirmed access.
                    ?? await _appUserCompanyService.GetOrCreateAsync(appUser.Id, confirmedId.Value, fiscalizeResponse.ActiveCompany?.Name);

                // Update local active company and persist the new Fiscalize token on the target company record. Keep the
                // company's saved refresh token when the platform sends none, so the session can still be renewed.
                targetCompany.FiscalizeToken = fiscalizeResponse.Token;
                targetCompany.FiscalizeTokenExpiresAt = FiscalizeXero.Api.Services.FiscalizeSession.PlatformSessions.Expiry(fiscalizeResponse.TokeExpiry);
                if (!string.IsNullOrEmpty(fiscalizeResponse.RefreshToken))
                    targetCompany.FiscalizeRefreshToken = fiscalizeResponse.RefreshToken;
                await _appUserCompanyService.UpdateAsync(targetCompany);

                appUser.ActiveCompanyId = targetCompany.Id;
                await _appUserService.UpdateAsync(appUser);
                _membership?.RecordGranted(targetCompany);
                appUser = await _appUserService.GetByUserIdAsync(userId);

                LoginResponseXeroOrgModel? xeroOrg = null;
                string? xeroUrl = null;
                if (appUser!.ActiveCompany != null)
                {
                    var clientConfig = await GetEffectiveClientConfigAsync(appUser.ActiveCompany);
                    if (clientConfig?.TenantId != null)
                    {
                        xeroOrg = new LoginResponseXeroOrgModel
                        {
                            Id = clientConfig.TenantId.Value,
                            Name = clientConfig.TenantName,
                            IsSetupComplete = !string.IsNullOrEmpty(clientConfig.RefreshToken)
                        };
                        xeroUrl = XeroAuth.XeroOptions.XeroWebUrlFor(clientConfig.OrganisationShortCode);
                    }
                }

                return await IdentityLoginAsync(fiscalizeResponse, xeroOrg, appUser!.Email, xeroUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error switching company");
                return new ServiceResponse<LoginResponseModel> { Status = HttpStatusCode.InternalServerError, Message = "An error occurred while switching company" };
            }
        }

        public async Task<ServiceResponse<bool>> EnsureExternalIdentityAsync(int appUserCompanyId, string tenantId, string? displayName)
        {
            try
            {
                var appUserCompany = await _appUserCompanyService.GetByIdAsync(appUserCompanyId);
                if (appUserCompany == null)
                    return new ServiceResponse<bool> { Status = HttpStatusCode.NotFound, Message = "AppUserCompany not found." };

                // The company's platform session, renewed first when it has lapsed. The callback is anonymous (there
                // is no signed-in request to renew through), so the session is taken from the company's own row.
                var auth = await _fiscalizeSession.TryGetAuthForCompanyAsync(appUserCompany);
                if (auth == null)
                    return new ServiceResponse<bool> { Status = HttpStatusCode.Unauthorized, Message = "No usable Fiscalize session on this company; cannot create pairing." };

                var req = new FiscalizeXero.Fiscalize.Services.Auths.CreateExternalIdentityRequest
                {
                    CompanyId = appUserCompany.CompanyId,
                    Provider = FiscalizeXero.Fiscalize.Services.Auths.ProviderNames.Xero,
                    ExternalIdentifier = tenantId,
                    ExternalDisplayName = displayName
                };

                try
                {
                    await FiscalizeXero.Fiscalize.Services.Auths.ExternalIdentitiesService.CreateAsync(auth, req);
                }
                catch (FiscalizeXero.Fiscalize.Exceptions.FiscalizeApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // The platform ends idle sessions before their expiry: renew once and try again.
                    auth = await _fiscalizeSession.TryRefreshAsync(appUserCompany);
                    if (auth == null)
                        return new ServiceResponse<bool> { Status = HttpStatusCode.Unauthorized, Message = "The Fiscalize session could not be renewed; cannot create pairing." };
                    await FiscalizeXero.Fiscalize.Services.Auths.ExternalIdentitiesService.CreateAsync(auth, req);
                }
                return new ServiceResponse<bool> { Data = true };
            }
            catch (FiscalizeXero.Fiscalize.Exceptions.FiscalizeApiException ex)
            {
                // Conflict means the organisation is paired with a different Fiscalize company on the platform (a
                // repeat for the same company is answered as success there). The caller undoes the connection.
                _logger.LogWarning("EnsureExternalIdentityAsync: platform returned {Status}: {Message}", ex.StatusCode, ex.Message);
                return new ServiceResponse<bool> { Status = ex.StatusCode, Message = ex.Message };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EnsureExternalIdentityAsync: unexpected error");
                return new ServiceResponse<bool> { Status = HttpStatusCode.InternalServerError, Message = "Could not create the pairing." };
            }
        }

        public async Task<ServiceResponse<LoginResponseModel>> ExchangeLaunchTokenAsync(string launchToken)
        {
            try
            {
                var fiscalizeConfig = _configuration.GetSection("Fiscalize").Get<FiscalizeConfigModel>();
                var auth = new FiscalizeAuthModel { ApiUrl = fiscalizeConfig?.ApiUrl ?? throw new InvalidOperationException("Fiscalize API URL not configured") };

                var validation = await FiscalizeXero.Fiscalize.Services.AppLaunchers.AppLauncherService.ValidateLaunchTokenAsync(auth, launchToken);

                // Build a synthetic LoginResponseModel so GetOrCreateAppUserAsync can handle it
                var fiscalizeLogin = new FiscalizeXero.Fiscalize.Models.Auths.LoginResponseModel
                {
                    UserId = validation.AppUserId,
                    FullName = validation.FullName,
                    Email = validation.Email,
                    EmailConfirmed = true,
                    Token = validation.FiscalizeJwt ?? string.Empty,
                    TokeExpiry = validation.FiscalizeJwtExpiry ?? DateTime.UtcNow.AddMinutes(30),
                    // Keep the launch's own refresh token when the platform sends one: renewing with an older saved one
                    // would bring back that older session (e.g. a read-only support session after "Act on their behalf").
                    RefreshToken = validation.FiscalizeRefreshToken,
                    ActiveCompany = new FiscalizeXero.Fiscalize.Models.Auths.LoginResponseActiveCompanyModel
                    {
                        Id = validation.CompanyId,
                        Name = validation.CompanyName
                    }
                };

                var appUser = await _appUserService.GetOrCreateAppUserAsync(fiscalizeLogin, validation.Email);
                RecordGranted(appUser, fiscalizeLogin);

                LoginResponseXeroOrgModel? xeroOrg = null;
                string? xeroUrl = null;
                if (appUser.ActiveCompany != null)
                {
                    var clientConfig = await GetEffectiveClientConfigAsync(appUser.ActiveCompany);
                    if (clientConfig?.TenantId != null)
                    {
                        xeroOrg = new LoginResponseXeroOrgModel
                        {
                            Id = clientConfig.TenantId.Value,
                            Name = clientConfig.TenantName,
                            IsSetupComplete = !string.IsNullOrEmpty(clientConfig.RefreshToken)
                        };
                        xeroUrl = XeroAuth.XeroOptions.XeroWebUrlFor(clientConfig.OrganisationShortCode);
                    }
                }

                return await IdentityLoginAsync(fiscalizeLogin, xeroOrg, appUser.Email, xeroUrl);
            }
            catch (FiscalizeXero.Fiscalize.Exceptions.FiscalizeApiException ex)
            {
                _logger.LogWarning("Fiscalize launch token validation failed. Status: {Status}, Message: {Message}", ex.StatusCode, ex.Message);
                return new ServiceResponse<LoginResponseModel> { Status = ex.StatusCode, Message = ex.Message };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exchanging Fiscalize launch token");
                return new ServiceResponse<LoginResponseModel> { Status = HttpStatusCode.InternalServerError, Message = "An error occurred during sign-in." };
            }
        }

        public async Task<ServiceResponse<string>> OpenFiscalizeAsync(ClaimsPrincipal claim)
        {
            try
            {
                var userIdStr = claim.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!long.TryParse(userIdStr, out var userId))
                    return new ServiceResponse<string> { Status = HttpStatusCode.Unauthorized, Message = "Invalid session." };

                var appUser = await _appUserService.GetByUserIdAsync(userId);
                if (appUser == null)
                    return new ServiceResponse<string> { Status = HttpStatusCode.NotFound, Message = "User not found." };

                if (appUser.ActiveCompany == null)
                    return new ServiceResponse<string> { Status = HttpStatusCode.Unauthorized, Message = FiscalizeXero.Api.Services.FiscalizeSession.FiscalizeSessionService.TimedOutMessage };

                // The same renewal as every other platform call: renewed when lapsed or refused, "sign in again" otherwise.
                var result = await _fiscalizeSession.RunAsync(a => FiscalizeXero.Fiscalize.Services.AppLaunchers.AppLauncherService.GenerateReverseTokenAsync(a));
                return new ServiceResponse<string> { Data = result.LaunchUrl };
            }
            catch (Exception ex) when (ex is FiscalizeXero.Api.Exceptions.FiscalizeSessionExpiredException or FiscalizeXero.Api.Exceptions.FiscalizePlatformUnavailableException)
            {
                throw; // answered by FiscalizeSessionExceptionFilter: "sign in again", or "try again in a moment"
            }
            catch (FiscalizeXero.Fiscalize.Exceptions.FiscalizeApiException ex)
            {
                _logger.LogWarning("Failed to generate Fiscalize reverse token. Status: {Status}, Message: {Message}", ex.StatusCode, ex.Message);
                return new ServiceResponse<string> { Status = ex.StatusCode, Message = ex.Message };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating Fiscalize reverse token");
                return new ServiceResponse<string> { Status = HttpStatusCode.InternalServerError, Message = "Could not open Fiscalize." };
            }
        }

        async Task<Data.ClientConfig?> GetEffectiveClientConfigAsync(Data.AppUserCompany company)
        {
            // One connection per Fiscalize company, whichever colleague set it up.
            return await _clientConfigService.GetForCompanyAsync(company.CompanyId);
        }

        async Task<ServiceResponse<LoginResponseModel>> IdentityLoginAsync(LoginResponseModel login, LoginResponseXeroOrgModel? xeroOrg, string? email = null, string? xeroUrl = null)
        {
            var session = new SessionModel
            {
                ActiveCompanyId = (login.ActiveCompany != null) ? login.ActiveCompany.Id : null,
                ActiveCompanyName = (login.ActiveCompany != null) ? login.ActiveCompany.Name : null,
                EmailConfirmed = login.EmailConfirmed,
                UserId = login.UserId,
                UserName = login.FullName,
                Device = null
            };

            var claims = new List<Claim>()
            {
                //new Claim("Email", login.Email),
                new Claim(ClaimTypes.NameIdentifier, login.UserId.ToString()),
                new Claim("Session", vc.JsonObjectToString(session))
            };
            // The version "Sign out of all devices" checks this token against.
            if (_revocation != null)
                claims.Add(new Claim(FiscalizeXero.Api.Security.SessionRevocation.Claim,
                    (await _revocation.CurrentVersionAsync(login.UserId)).ToString(System.Globalization.CultureInfo.InvariantCulture)));

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["AuthSettings:Key"]));

            var token = new JwtSecurityToken(
                issuer: _configuration["AuthSettings:Issuer"],
                audience: _configuration["AuthSettings:Audience"],
                claims: claims,
                expires: DateTime.Now.AddDays(30),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

            string tokenAsString = new JwtSecurityTokenHandler().WriteToken(token);

            return new ServiceResponse<LoginResponseModel>
            {
                Data = new LoginResponseModel
                {
                    EmailConfirmed = login.EmailConfirmed,
                    UserId = login.UserId,
                    FullName = login.FullName,
                    Email = email,
                    ActiveCompany = login.ActiveCompany,
                    XeroOrganisation = xeroOrg,
                    XeroUrl = xeroUrl,
                    TokeExpiry = token.ValidTo,
                    Token = tokenAsString
                }
            };
        }
    }
}
