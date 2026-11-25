using System.Security.Claims;
using System.Text.Json;
using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.AppUserCompanies;
using FiscalizeXero.Api.Services.AppUsers;
using FiscalizeXero.Api.Services.ClientConfigs;
using FiscalizeXero.Shared.Models.Auths;

namespace FiscalizeXero.Api.Services.CurrentUser
{
    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IAppUserService _appUserService;
        private readonly IClientConfigService _clientConfigService;
        private readonly IAppUserCompanyService _appUserCompanyService;

        private AppUser? _cachedUser;
        private bool _userLoaded;
        private ClientConfig? _cachedConfig;
        private bool _configLoaded;

        private readonly ILogger<CurrentUserService>? _logger;

        public CurrentUserService(
            IHttpContextAccessor httpContextAccessor,
            IAppUserService appUserService,
            IClientConfigService clientConfigService,
            IAppUserCompanyService appUserCompanyService,
            ILogger<CurrentUserService>? logger = null)
        {
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            _appUserService = appUserService;
            _clientConfigService = clientConfigService;
            _appUserCompanyService = appUserCompanyService;
        }

        public long? GetCurrentUserId()
        {
            var userIdClaim = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !long.TryParse(userIdClaim, out var userId))
            {
                return null;
            }

            return userId;
        }

        public async Task<AppUser?> GetCurrentUserAsync()
        {
            if (_userLoaded) return _cachedUser;

            var userId = GetCurrentUserId();
            if (!userId.HasValue)
            {
                _userLoaded = true;
                return null;
            }

            var user = await _appUserService.GetByUserIdAsync(userId.Value);
            if (user == null)
            {
                // The session is valid but names a user this app doesn't have: the browser is signed out (bare 401).
                _logger?.LogWarning("Signed-in platform user {UserId} has no AppUser row; request {Path} answered 401",
                    userId.Value, _httpContextAccessor.HttpContext?.Request.Path.Value);
                _userLoaded = true;
                return null;
            }

            // Guard: the JWT's Session claim encodes the Fiscalize company ID the user last
            // authenticated against. If the DB's ActiveCompanyId has drifted (e.g. a switch
            // only partially committed, or the record was modified externally), all data
            // queries would silently serve the wrong company's records.
            // We detect the mismatch here and heal the DB so every downstream query uses the
            // company the JWT was issued for.
            var jwtCompanyId = GetJwtActiveCompanyId();
            if (jwtCompanyId.HasValue && user.ActiveCompany?.CompanyId != jwtCompanyId.Value)
            {
                var intended = user.Companies.FirstOrDefault(c => c.CompanyId == jwtCompanyId.Value);
                if (intended != null && user.ActiveCompanyId != intended.Id)
                {
                    user.ActiveCompanyId = intended.Id;
                    await _appUserService.UpdateAsync(user);
                    user = await _appUserService.GetByUserIdAsync(userId.Value);
                }
            }

            _cachedUser = user;
            _userLoaded = true;
            return _cachedUser;
        }

        public async Task<ClientConfig?> GetCurrentClientConfigAsync()
        {
            if (_configLoaded) return _cachedConfig;

            var user = await GetCurrentUserAsync();

            if (user?.ActiveCompany == null)
            {
                _configLoaded = true;
                return null;
            }

            // One Xero connection per Fiscalize company: whichever colleague connected it, everyone signed in to
            // that company uses it (and the company data stored under it).
            _cachedConfig = await _clientConfigService.GetForCompanyAsync(user.ActiveCompany.CompanyId);
            _configLoaded = true;
            return _cachedConfig;
        }

        public async Task<int?> GetCompanyAnchorIdAsync()
        {
            var config = await GetCurrentClientConfigAsync();
            if (config != null) return config.AppUserCompanyId;

            // Not connected (yet, or any more): keep using the member row that already holds the company's devices and
            // fiscal records, so whoever (re)connects doesn't strand them; otherwise the user's own row.
            var user = await GetCurrentUserAsync();
            if (user?.ActiveCompany == null) return null;
            return await _appUserCompanyService.GetDataHolderIdAsync(user.ActiveCompany.CompanyId) ?? user.ActiveCompany.Id;
        }

        private long? GetJwtActiveCompanyId()
        {
            var sessionStr = _httpContextAccessor.HttpContext?.User.FindFirst("Session")?.Value;
            if (string.IsNullOrEmpty(sessionStr)) return null;
            try
            {
                var session = JsonSerializer.Deserialize<SessionModel>(sessionStr,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return session?.ActiveCompanyId;
            }
            catch
            {
                return null;
            }
        }
    }
}
