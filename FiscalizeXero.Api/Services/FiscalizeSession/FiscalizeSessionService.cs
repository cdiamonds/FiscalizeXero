using System.Collections.Concurrent;
using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Exceptions;
using FiscalizeXero.Api.Services.AppUserCompanies;
using FiscalizeXero.Api.Services.AppUsers;
using FiscalizeXero.Api.Services.CurrentUser;
using FiscalizeXero.Fiscalize.Exceptions;
using FiscalizeXero.Fiscalize.Services.Auths;
using FiscalizeXero.Shared.Models.Auths;
using FiscalizeXero.Shared.Models.Settings;

namespace FiscalizeXero.Api.Services.FiscalizeSession
{
    public class FiscalizeSessionService : IFiscalizeSessionService
    {
        /// <summary>Refresh this close to expiry so a request never goes out with a token that dies mid-flight.</summary>
        private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(2);

        // One refresh at a time per company: the platform rotates refresh tokens, so two concurrent refreshes would
        // leave one of them holding a token the platform has just invalidated.
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> RefreshLocks = new();
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> UserRefreshLocks = new();

        /// <summary>Set when the last renewal failed because the platform couldn't be reached, not because it refused.</summary>
        private bool _platformUnavailable;

        private readonly ICurrentUserService _currentUserService;
        private readonly IAppUserService _appUserService;
        private readonly IAppUserCompanyService _appUserCompanyService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FiscalizeSessionService> _logger;

        public FiscalizeSessionService(
            ICurrentUserService currentUserService,
            IAppUserService appUserService,
            IAppUserCompanyService appUserCompanyService,
            IConfiguration configuration,
            ILogger<FiscalizeSessionService> logger)
        {
            _currentUserService = currentUserService;
            _appUserService = appUserService;
            _appUserCompanyService = appUserCompanyService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<FiscalizeAuthModel> GetAuthAsync()
        {
            var appUser = await _currentUserService.GetCurrentUserAsync();
            if (appUser?.ActiveCompany == null)
                throw new FiscalizeSessionExpiredException(TimedOutMessage);

            _platformUnavailable = false;
            var auth = await TryGetAuthForCompanyAsync(appUser.ActiveCompany);
            if (auth != null) return auth;
            throw await NotRenewedAsync(appUser.ActiveCompany);
        }

        public async Task<FiscalizeAuthModel?> TryGetAuthForCompanyAsync(AppUserCompany company)
        {
            var lapsed = string.IsNullOrEmpty(company.FiscalizeToken)
                || (company.FiscalizeTokenExpiresAt.HasValue && company.FiscalizeTokenExpiresAt.Value <= DateTime.UtcNow.Add(RefreshBuffer));
            if (!lapsed)
                return new FiscalizeAuthModel { ApiUrl = ApiUrl(), Token = company.FiscalizeToken! };

            return await TryRefreshAsync(company);
        }

        public async Task<FiscalizeAuthModel?> TryRefreshAsync(AppUserCompany company)
            => await RefreshCompanyTokenAsync(company) ?? await OpenFromUserSessionAsync(company);

        /// <inheritdoc />
        public async Task<FiscalizeAuthModel?> TryGetUserAuthAsync(AppUser user)
        {
            if (string.IsNullOrEmpty(user.FiscalizeToken)) return null;
            var lapsed = user.FiscalizeTokenExpiresAt.HasValue && user.FiscalizeTokenExpiresAt.Value <= DateTime.UtcNow.Add(RefreshBuffer);
            if (!lapsed) return new FiscalizeAuthModel { ApiUrl = ApiUrl(), Token = user.FiscalizeToken };
            if (string.IsNullOrEmpty(user.FiscalizeRefreshToken)) return null;

            var gate = UserRefreshLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                // Another request may have refreshed while we waited.
                var fresh = await _appUserService.GetByIdAsync(user.Id) ?? user;
                if (!string.IsNullOrEmpty(fresh.FiscalizeToken) && fresh.FiscalizeTokenExpiresAt > DateTime.UtcNow.Add(RefreshBuffer))
                {
                    CopyUserTokens(fresh, user);
                    return new FiscalizeAuthModel { ApiUrl = ApiUrl(), Token = user.FiscalizeToken! };
                }
                var refreshToken = fresh.FiscalizeRefreshToken ?? user.FiscalizeRefreshToken!;
                var refreshed = await LoginService.RefreshTokenAsync(ApiUrl(), refreshToken);
                if (string.IsNullOrEmpty(refreshed?.Token)) return null;

                fresh.FiscalizeToken = refreshed.Token;
                fresh.FiscalizeTokenExpiresAt = PlatformSessions.Expiry(refreshed.TokeExpiry);
                fresh.FiscalizeRefreshToken = string.IsNullOrEmpty(refreshed.RefreshToken) ? refreshToken : refreshed.RefreshToken;
                await _appUserService.UpdateAsync(fresh);
                if (!ReferenceEquals(fresh, user)) CopyUserTokens(fresh, user);
                return new FiscalizeAuthModel { ApiUrl = ApiUrl(), Token = user.FiscalizeToken! };
            }
            catch (FiscalizeApiException ex)
            {
                if (IsUnavailable(ex)) _platformUnavailable = true;
                _logger.LogInformation("Refreshing the Fiscalize sign-in of user {UserId} was refused: {Status} {Message}", user.UserId, ex.StatusCode, ex.Message);
                return null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _platformUnavailable = true;
                _logger.LogWarning("Renewing a Fiscalize session couldn't reach the platform: {Message}", ex.Message);
                return null;
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Opens a new session on the company from the user's own platform session, exactly as choosing the company in
        /// the app does. Used when the company's saved session has lapsed and can't be renewed, so a user who signed
        /// in again is never sent round to the sign-in page by a company session from an earlier visit.
        /// </summary>
        private async Task<FiscalizeAuthModel?> OpenFromUserSessionAsync(AppUserCompany company)
        {
            var user = await _appUserService.GetByIdAsync(company.AppUserId);
            if (user == null) return null;
            var userAuth = await TryGetUserAuthAsync(user);
            if (userAuth == null) return null;

            try
            {
                var opened = await LoginService.SwitchCompanyAsync(userAuth, company.CompanyId);
                if (opened.ActiveCompany?.Id != company.CompanyId || string.IsNullOrEmpty(opened.Token))
                {
                    _logger.LogWarning("Opening a Fiscalize session on company {CompanyId} answered with company {Answered}; not used", company.CompanyId, opened.ActiveCompany?.Id);
                    return null;
                }
                company.FiscalizeToken = opened.Token;
                company.FiscalizeTokenExpiresAt = PlatformSessions.Expiry(opened.TokeExpiry);
                if (!string.IsNullOrEmpty(opened.RefreshToken)) company.FiscalizeRefreshToken = opened.RefreshToken;
                await _appUserCompanyService.UpdateAsync(company);
                _logger.LogInformation("Opened a new Fiscalize session on company {CompanyId} from the user's sign-in", company.CompanyId);
                return new FiscalizeAuthModel { ApiUrl = ApiUrl(), Token = company.FiscalizeToken };
            }
            catch (FiscalizeApiException ex)
            {
                if (IsUnavailable(ex)) _platformUnavailable = true;
                _logger.LogInformation("Opening a Fiscalize session on company {CompanyId} from the user's sign-in was refused: {Status} {Message}", company.CompanyId, ex.StatusCode, ex.Message);
                return null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _platformUnavailable = true;
                _logger.LogWarning("Renewing a Fiscalize session couldn't reach the platform: {Message}", ex.Message);
                return null;
            }
        }

        private static void CopyUserTokens(AppUser from, AppUser to)
        {
            to.FiscalizeToken = from.FiscalizeToken;
            to.FiscalizeTokenExpiresAt = from.FiscalizeTokenExpiresAt;
            to.FiscalizeRefreshToken = from.FiscalizeRefreshToken;
        }

        private async Task<FiscalizeAuthModel?> RefreshCompanyTokenAsync(AppUserCompany company)
        {
            if (string.IsNullOrEmpty(company.FiscalizeRefreshToken))
            {
                _logger.LogInformation("Fiscalize session for company {CompanyId} has lapsed and there is no refresh token", company.CompanyId);
                return null;
            }

            var gate = RefreshLocks.GetOrAdd(company.Id, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                // Another request may have refreshed while we waited: re-read the row before spending the refresh token.
                var fresh = await _appUserCompanyService.GetByIdAsync(company.Id);
                if (fresh != null && !string.IsNullOrEmpty(fresh.FiscalizeToken) && fresh.FiscalizeTokenExpiresAt.HasValue
                    && fresh.FiscalizeTokenExpiresAt.Value > DateTime.UtcNow.Add(RefreshBuffer) && fresh.FiscalizeToken != company.FiscalizeToken)
                {
                    CopyTokens(fresh, company);
                    return new FiscalizeAuthModel { ApiUrl = ApiUrl(), Token = company.FiscalizeToken! };
                }

                var refreshToken = fresh?.FiscalizeRefreshToken ?? company.FiscalizeRefreshToken!;
                var refreshed = await LoginService.RefreshTokenAsync(ApiUrl(), refreshToken);
                if (string.IsNullOrEmpty(refreshed?.Token))
                {
                    _logger.LogWarning("Fiscalize token refresh for company {CompanyId} returned no token", company.CompanyId);
                    return null;
                }

                company.FiscalizeToken = refreshed.Token;
                company.FiscalizeTokenExpiresAt = PlatformSessions.Expiry(refreshed.TokeExpiry);
                company.FiscalizeRefreshToken = refreshed.RefreshToken ?? refreshToken;
                if (fresh != null && !ReferenceEquals(fresh, company))
                {
                    CopyTokens(company, fresh);
                    await _appUserCompanyService.UpdateAsync(fresh);
                }
                else
                {
                    await _appUserCompanyService.UpdateAsync(company);
                }
                _logger.LogInformation("Refreshed the Fiscalize session for company {CompanyId}; valid until {Expiry:u}", company.CompanyId, company.FiscalizeTokenExpiresAt);
                return new FiscalizeAuthModel { ApiUrl = ApiUrl(), Token = company.FiscalizeToken! };
            }
            catch (FiscalizeApiException ex)
            {
                if (IsUnavailable(ex)) _platformUnavailable = true;
                _logger.LogWarning("Fiscalize token refresh for company {CompanyId} was rejected: {Status} {Message}", company.CompanyId, ex.StatusCode, ex.Message);
                return null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _platformUnavailable = true;
                _logger.LogWarning("Renewing a Fiscalize session couldn't reach the platform: {Message}", ex.Message);
                return null;
            }
            finally
            {
                gate.Release();
            }
        }

        private static void CopyTokens(AppUserCompany from, AppUserCompany to)
        {
            to.FiscalizeToken = from.FiscalizeToken;
            to.FiscalizeTokenExpiresAt = from.FiscalizeTokenExpiresAt;
            to.FiscalizeRefreshToken = from.FiscalizeRefreshToken;
        }

        private string ApiUrl()
        {
            var config = _configuration.GetSection("Fiscalize").Get<FiscalizeConfigModel>();
            return config?.ApiUrl ?? throw new InvalidOperationException("Fiscalize API URL not configured");
        }

        /// <summary>Renewed this close to expiry by the keep-alive, so an active user never meets an expired token.</summary>
        public static readonly TimeSpan KeepAliveRenewWindow = TimeSpan.FromMinutes(10);

        public const string TimedOutMessage = "Your Fiscalize session has timed out. Sign in again to carry on where you left off.";

        /// <inheritdoc />
        public async Task<T> RunAsync<T>(Func<FiscalizeAuthModel, Task<T>> call)
        {
            var appUser = await _currentUserService.GetCurrentUserAsync();
            var company = appUser?.ActiveCompany ?? throw new FiscalizeSessionExpiredException(TimedOutMessage);
            _platformUnavailable = false;
            var auth = await TryGetAuthForCompanyAsync(company);
            if (auth == null) throw await NotRenewedAsync(company);
            try
            {
                return await call(auth);
            }
            catch (FiscalizeApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                // The platform ended the session before its expiry (idle timeout, sign-out elsewhere): renew once and retry.
                _logger.LogInformation("Fiscalize rejected the session of company {CompanyId} before its expiry; renewing", company.CompanyId);
                _platformUnavailable = false;
                var renewed = await TryRefreshAsync(company);
                if (renewed == null || renewed.Token == auth.Token) throw await NotRenewedAsync(company);
                // A 401 with a brand-new session isn't the session: it goes back to the caller as the platform's refusal.
                return await call(renewed);
            }
        }

        /// <inheritdoc />
        public async Task<PlatformSessionState> KeepAliveAsync()
        {
            var appUser = await _currentUserService.GetCurrentUserAsync();
            var company = appUser?.ActiveCompany;
            if (company == null) return PlatformSessionState.NoCompany;

            if (string.IsNullOrEmpty(company.FiscalizeToken)
                || (company.FiscalizeTokenExpiresAt.HasValue && company.FiscalizeTokenExpiresAt.Value <= DateTime.UtcNow.Add(KeepAliveRenewWindow)))
            {
                // Renew ahead of expiry while the user is here; a renewal also counts as activity on the platform.
                _platformUnavailable = false;
                if (await TryRefreshAsync(company) == null)
                {
                    // Not answering: leave the session as it is (it may still be valid) and try again on the next ping.
                    if (_platformUnavailable) return PlatformSessionState.PlatformUnavailable;
                    throw await NotRenewedAsync(company);
                }
                return PlatformSessionState.Active;
            }
            try
            {
                // Use the session once: the platform ends idle sessions, and this finds a session it already ended.
                await RunAsync(a => CompanyUsersService.MyCompaniesAsync(a, 1, 1));
                return PlatformSessionState.Active;
            }
            catch (FiscalizePlatformUnavailableException)
            {
                return PlatformSessionState.PlatformUnavailable;
            }
            catch (FiscalizeApiException ex)
            {
                _logger.LogInformation("Fiscalize keep-alive for company {CompanyId} answered {Status}; leaving the session as it is", company.CompanyId, ex.StatusCode);
                return PlatformSessionState.PlatformUnavailable;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogInformation("Fiscalize keep-alive for company {CompanyId} couldn't reach the platform: {Message}", company.CompanyId, ex.Message);
                return PlatformSessionState.PlatformUnavailable;
            }
            catch (TaskCanceledException)
            {
                return PlatformSessionState.PlatformUnavailable;
            }
        }

        private static bool IsUnavailable(FiscalizeApiException ex) => (int)ex.StatusCode >= 500 || (int)ex.StatusCode is 408 or 429;

        /// <summary>The session couldn't be renewed: over (sign in again), unless the platform simply didn't answer.</summary>
        private async Task<Exception> NotRenewedAsync(AppUserCompany company)
        {
            if (_platformUnavailable)
            {
                _logger.LogWarning("Fiscalize session of company {CompanyId} couldn't be renewed because the platform isn't answering", company.CompanyId);
                return new FiscalizePlatformUnavailableException();
            }
            await MarkLapsedAsync(company);
            _logger.LogInformation("Fiscalize session of company {CompanyId} couldn't be renewed: asking the user to sign in", company.CompanyId);
            return new FiscalizeSessionExpiredException(TimedOutMessage);
        }

        /// <summary>A session the platform refused and that couldn't be renewed: treat it as lapsed so nothing keeps using it.</summary>
        private async Task MarkLapsedAsync(AppUserCompany company)
        {
            if (company.FiscalizeTokenExpiresAt.HasValue && company.FiscalizeTokenExpiresAt.Value <= DateTime.UtcNow) return;
            company.FiscalizeTokenExpiresAt = DateTime.UtcNow;
            try { await _appUserCompanyService.UpdateAsync(company); }
            catch (Exception ex) { _logger.LogWarning(ex, "Couldn't mark the Fiscalize session of company {CompanyId} as lapsed", company.CompanyId); }
        }

        public async Task ClearSessionAsync()
        {
            var appUser = await _currentUserService.GetCurrentUserAsync();
            if (appUser == null) return;
            await ClearSessionAsync(appUser.Id);
        }

        private async Task ClearSessionAsync(int appUserId)
        {
            var appUser = await _appUserService.GetByIdAsync(appUserId);
            if (appUser?.ActiveCompany == null) return;

            appUser.ActiveCompany.FiscalizeToken = null;
            appUser.ActiveCompany.FiscalizeTokenExpiresAt = null;
            await _appUserCompanyService.UpdateAsync(appUser.ActiveCompany);
        }
    }
}
