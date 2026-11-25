using FiscalizeXero.Api.Data;
using FiscalizeXero.Fiscalize.Exceptions;
using FiscalizeXero.Fiscalize.Services.DeviceAuths;
using FiscalizeXero.Shared.Models.Auths;
using FiscalizeXero.Shared.Models.Fiscalize;
using System.Collections.Concurrent;

namespace FiscalizeXero.Api.Services.DeviceSession
{
    /// <summary>
    /// Singleton service that manages per-device Fiscalize sessions.
    /// Sessions are held in memory, keyed by AppUserDeviceConfig.Id.
    /// Tokens are refreshed automatically before expiry; a full re-login
    /// is performed when no refresh token is available.
    /// </summary>
    public class DeviceSessionService : IDeviceSessionService
    {
        // How many seconds before expiry we proactively refresh
        private const int RefreshBufferSeconds = 300;

        private record DeviceSession(string Token, string RefreshToken, DateTime ExpiresAt);

        private readonly ConcurrentDictionary<int, DeviceSession> _sessions = new();
        // One semaphore per device prevents concurrent login races
        private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();
        private readonly IConfiguration _configuration;
        private readonly ILogger<DeviceSessionService> _logger;

        public DeviceSessionService(IConfiguration configuration, ILogger<DeviceSessionService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<FiscalizeAuthModel> GetAuthAsync(AppUserDeviceConfig deviceConfig, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(deviceConfig.ApiKey) || string.IsNullOrEmpty(deviceConfig.ApiSecret))
                throw new FiscalizeXero.Api.Exceptions.UserFacingException(
                    $"Device config {deviceConfig.Id} has no API key/secret configured.");

            var sem = _locks.GetOrAdd(deviceConfig.Id, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync(ct);
            try
            {
                var apiUrl = GetApiUrl();
                var session = await GetOrRenewSessionAsync(deviceConfig, apiUrl, ct);
                return new FiscalizeAuthModel { ApiUrl = apiUrl, Token = session.Token };
            }
            finally
            {
                sem.Release();
            }
        }

        public void InvalidateSession(int deviceConfigId)
        {
            _sessions.TryRemove(deviceConfigId, out _);
            _logger.LogInformation("Device session invalidated for config {DeviceConfigId}", deviceConfigId);
        }

        public async Task<FiscalizeAuthModel> ReloginAsync(AppUserDeviceConfig deviceConfig, CancellationToken ct = default)
        {
            // Drop the cached session and force a fresh ApiKey/ApiSecret login.
            // Used when a Fiscalize 401 indicates the cached token is no longer
            // valid (e.g. credentials rotated server-side, token revoked).
            InvalidateSession(deviceConfig.Id);
            return await GetAuthAsync(deviceConfig, ct);
        }

        private async Task<DeviceSession> GetOrRenewSessionAsync(
            AppUserDeviceConfig deviceConfig, string apiUrl, CancellationToken ct)
        {
            if (_sessions.TryGetValue(deviceConfig.Id, out var existing))
            {
                // Guard: ExpiresAt may be DateTime.MinValue if the API response omits the field
                DateTime bufferExpiry;
                if (existing.ExpiresAt > DateTime.MinValue)
                    bufferExpiry = existing.ExpiresAt.AddSeconds(-RefreshBufferSeconds);
                else
                    bufferExpiry = DateTime.MaxValue; // treat as non-expiring; rely on API 401 to invalidate

                // Token still valid
                if (DateTime.UtcNow < bufferExpiry)
                    return existing;

                // Try refreshing with stored refresh token
                if (!string.IsNullOrEmpty(existing.RefreshToken))
                {
                    try
                    {
                        _logger.LogInformation("Refreshing device session for config {DeviceConfigId}", deviceConfig.Id);
                        var refreshed = await DeviceAuthService.RefreshAsync(apiUrl,
                            new DeviceRefreshRequest { RefreshToken = existing.RefreshToken });
                        var newSession = new DeviceSession(refreshed.Token, refreshed.RefreshToken, NormalizeExpiry(refreshed.ExpiresAt));
                        _sessions[deviceConfig.Id] = newSession;
                        return newSession;
                    }
                    catch (FiscalizeApiException ex)
                    {
                        _logger.LogWarning(ex, "Token refresh failed for device config {DeviceConfigId}, falling back to re-login", deviceConfig.Id);
                        _sessions.TryRemove(deviceConfig.Id, out _);
                    }
                }
            }

            // Fresh login with ApiKey + ApiSecret
            _logger.LogInformation("Logging in device config {DeviceConfigId} (device #{DeviceNumber}) at {ApiUrl}",
                deviceConfig.Id, deviceConfig.DeviceNumber, apiUrl);

            DeviceLoginResponse loginResponse;
            try
            {
                loginResponse = await DeviceAuthService.LoginAsync(apiUrl,
                    new DeviceLoginRequest
                    {
                        ApiKey = deviceConfig.ApiKey!,
                        ApiSecret = deviceConfig.ApiSecret!
                    });
            }
            catch (FiscalizeApiException ex)
            {
                _logger.LogError(
                    "Device login REJECTED for config {DeviceConfigId} (device #{DeviceNumber}). Status: {StatusCode}. Response body: {Body}",
                    deviceConfig.Id, deviceConfig.DeviceNumber, ex.StatusCode, ex.ResponseBody);
                throw;
            }

            if (string.IsNullOrEmpty(loginResponse.Token))
            {
                _logger.LogError(
                    "Device login for config {DeviceConfigId} returned an empty token. RefreshToken empty: {NoRefresh}, ExpiresAt: {ExpiresAt:u}",
                    deviceConfig.Id, string.IsNullOrEmpty(loginResponse.RefreshToken), loginResponse.ExpiresAt);
                throw new FiscalizeXero.Api.Exceptions.UserFacingException(
                    $"Device login for config {deviceConfig.Id} returned an empty token. " +
                    "The Fiscalize API accepted the credentials but did not issue a session token.");
            }

            var session = new DeviceSession(loginResponse.Token, loginResponse.RefreshToken, NormalizeExpiry(loginResponse.ExpiresAt));
            _sessions[deviceConfig.Id] = session;

            _logger.LogInformation(
                "Device session established for config {DeviceConfigId}: Fiscalize device {FiscalizeDeviceId}, expires {ExpiresAt:u}",
                deviceConfig.Id, loginResponse.DeviceID, session.ExpiresAt);

            return session;
        }

        // If the API doesn't return a valid ExpiresAt, default to 1 hour from now
        // so the token is eventually refreshed rather than kept forever.
        private static DateTime NormalizeExpiry(DateTime expiresAt) =>
            expiresAt > DateTime.MinValue ? expiresAt : DateTime.UtcNow.AddHours(1);

        private string GetApiUrl()
        {
            return _configuration["Fiscalize:ApiUrl"]
                ?? throw new InvalidOperationException("Fiscalize API URL is not configured.");
        }
    }
}
