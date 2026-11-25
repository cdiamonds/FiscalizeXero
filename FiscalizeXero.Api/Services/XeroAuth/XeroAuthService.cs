using FiscalizeXero.Api.Data;
using FiscalizeXero.Api.Services.ClientConfigs;
using FiscalizeXero.Xero.Exceptions;
using FiscalizeXero.Xero.OAuth;
using FiscalizeXero.Xero.Services;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;

namespace FiscalizeXero.Api.Services.XeroAuth
{
    /// <summary>
    /// Xero OAuth 2.0 for one connection (<see cref="ClientConfig"/>): code exchange, proactive refresh five minutes
    /// before the 30-minute access token expires, a forced refresh when Xero still answers 401, and removal of the
    /// organisation's connection on disconnect.
    ///
    /// Xero rotates the refresh token on every refresh, so the new one is saved each time. A refresh answered with
    /// <c>invalid_grant</c> means the refresh token is dead (60 days unused, already rotated away, or the user
    /// removed the app inside Xero): the stored tokens are cleared, the organisation is kept, and the connection is
    /// reported as needing to be reconnected instead of being hammered.
    /// </summary>
    public class XeroAuthService : IXeroAuthService
    {
        private readonly XeroOAuthClient _oauth;
        private readonly IClientConfigService _clientConfigService;
        private readonly XeroOptions _options;
        private readonly ILogger<XeroAuthService> _logger;
        private readonly int _initialLookbackMinutes;

        private const int TokenRefreshBufferSeconds = 300;
        private const int RefreshFailureCooldownSeconds = 30;

        private static readonly ConcurrentDictionary<long, SemaphoreSlim> _refreshLocks = new();
        private static readonly ConcurrentDictionary<long, DateTimeOffset> _refreshFailedAt = new();

        public XeroAuthService(
            HttpClient httpClient,
            IClientConfigService clientConfigService,
            IOptions<XeroOptions> options,
            ILogger<XeroAuthService> logger,
            IOptions<Sync.XeroSyncOptions>? syncOptions = null)
        {
            _initialLookbackMinutes = syncOptions?.Value.InitialLookbackMinutes ?? 60;
            _options = options.Value;
            _oauth = new XeroOAuthClient(httpClient, _options);
            _clientConfigService = clientConfigService;
            _logger = logger;
        }

        public bool IsConfigured => _options.IsConfigured && !string.IsNullOrWhiteSpace(_options.RedirectUri);

        public string? BuildConsentUrl(Guid state) => IsConfigured ? _oauth.BuildConsentUrl(state.ToString()) : null;

        public async Task<XeroTokenResponse> ExchangeCodeAsync(string code)
        {
            if (!IsConfigured)
                return new XeroTokenResponse { Error = "not_configured", ErrorDescription = "Xero credentials are not configured on this server.", Status = HttpStatusCode.ServiceUnavailable };

            var tokens = await _oauth.ExchangeCodeAsync(code);
            if (!tokens.IsSuccess)
                // Error bodies carry only error/error_description, never a token, so they are safe to log.
                _logger.LogError("Xero code exchange failed. Status: {Status}, error: {Error} {Description}", tokens.Status, tokens.Error, tokens.ErrorDescription);
            return tokens;
        }

        public async Task StoreConnectionAsync(ClientConfig config, XeroTokenResponse tokens, Guid tenantId, string? tenantName, Guid connectionId)
        {
            var previousTenant = config.TenantId;
            config.AccessToken = tokens.AccessToken;
            config.RefreshToken = tokens.RefreshToken;
            config.TokenExpiresAt = DateTime.UtcNow.AddSeconds(tokens.ExpiresIn);
            config.TenantId = tenantId;
            config.TenantName = Cap(tenantName, 200);
            config.XeroConnectionId = connectionId;
            config.GrantedScopeVersion = _options.ScopeVersion;
            // The backstop sync's cursor. Reconnecting the same organisation (after Xero dropped the connection) keeps
            // it, so whatever was approved while the connection was down is still found and fiscalised. A different
            // organisation, or a first connection, starts from a short look-back anchored now, not months of history;
            // anchoring it now also means a device set up hours later still covers the documents approved meanwhile.
            var sameOrganisation = previousTenant == tenantId && config.LastSyncAt.HasValue;
            if (!sameOrganisation)
            {
                config.LastSyncAt = DateTime.UtcNow.AddMinutes(-Math.Max(0, _initialLookbackMinutes));
                config.LastSyncRunAt = null;
            }
            config.SyncRetryAt = null;
            config.LastSyncError = null;
            await _clientConfigService.UpdateAsync(config);
            _refreshFailedAt.TryRemove(config.Id, out _);

            _logger.LogInformation("Xero connection stored for ClientConfig {ConfigId} (tenant {TenantId}). Access token expires at {ExpiresAt}.",
                config.Id, tenantId, config.TokenExpiresAt);
        }

        public async Task<XeroTokenResponse?> RefreshAccessTokenAsync(ClientConfig config)
        {
            if (string.IsNullOrEmpty(config.RefreshToken))
            {
                _logger.LogWarning("Cannot refresh access token for ClientConfig {ConfigId}: missing refresh token", config.Id);
                return null;
            }
            if (!_options.IsConfigured)
            {
                _logger.LogWarning("Cannot refresh access token for ClientConfig {ConfigId}: Xero credentials are not configured", config.Id);
                return null;
            }

            // Another process (the webhook worker, the sync, a colleague's request) may have rotated the refresh token
            // since this copy of the row was loaded. Xero refuses a used refresh token, so always spend the latest one.
            try
            {
                var (freshAccess, freshRefresh, freshExpires) = await _clientConfigService.GetFreshTokensAsync(config.Id);
                if (string.IsNullOrEmpty(freshRefresh))
                {
                    config.AccessToken = null; config.RefreshToken = null; config.TokenExpiresAt = null;
                    _logger.LogWarning("Cannot refresh access token for ClientConfig {ConfigId}: the connection was lost meanwhile", config.Id);
                    return null;
                }
                if (freshRefresh != config.RefreshToken)
                {
                    config.AccessToken = freshAccess; config.RefreshToken = freshRefresh; config.TokenExpiresAt = freshExpires;
                    if (!string.IsNullOrEmpty(freshAccess) && freshExpires.HasValue && freshExpires.Value - DateTime.UtcNow > TimeSpan.FromSeconds(TokenRefreshBufferSeconds))
                        return new XeroTokenResponse { AccessToken = freshAccess, RefreshToken = freshRefresh, ExpiresIn = (int)(freshExpires.Value - DateTime.UtcNow).TotalSeconds, Status = HttpStatusCode.OK };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not re-read the stored tokens for ClientConfig {ConfigId}; refreshing with the copy in hand", config.Id);
            }

            try
            {
                var tokens = await _oauth.RefreshAsync(config.RefreshToken!);
                if (!tokens.IsSuccess)
                {
                    _logger.LogError("Xero token refresh failed for ClientConfig {ConfigId} (tenant {TenantId}). Status: {Status}, error: {Error} {Description}",
                        config.Id, config.TenantId, tokens.Status, tokens.Error, tokens.ErrorDescription);
                    if (tokens.IsConnectionLost)
                        await MarkConnectionLostAsync(config, tokens.Error);
                    return tokens;
                }

                config.AccessToken = tokens.AccessToken;
                // Xero rotates the refresh token on every refresh: the old one stops working, so the new one must be saved.
                if (!string.IsNullOrEmpty(tokens.RefreshToken))
                    config.RefreshToken = tokens.RefreshToken;
                config.TokenExpiresAt = DateTime.UtcNow.AddSeconds(tokens.ExpiresIn);
                await SaveRotatedTokensAsync(config);

                _logger.LogInformation("Xero token refresh succeeded for ClientConfig {ConfigId}. Expires at {ExpiresAt}.", config.Id, config.TokenExpiresAt);
                return tokens;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during Xero token refresh for ClientConfig {ConfigId}", config.Id);
                return new XeroTokenResponse { Error = "refresh_failed", ErrorDescription = ex.Message, Status = HttpStatusCode.InternalServerError };
            }
        }

        /// <summary>
        /// The refresh token is dead, so the stored tokens are cleared (the organisation stays, so the company list
        /// can say "reconnect" rather than "never connected"). The sync and the webhooks skip connections without a
        /// refresh token, which stops the app calling Xero with credentials that can never work.
        /// </summary>
        private async Task MarkConnectionLostAsync(ClientConfig config, string? error)
        {
            _logger.LogWarning("Xero connection lost for ClientConfig {ConfigId} (tenant {TenantId}): {Error}. The user must reconnect.",
                config.Id, config.TenantId, error);
            config.AccessToken = null;
            config.RefreshToken = null;
            config.TokenExpiresAt = null;
            try
            {
                await _clientConfigService.SaveTokensAsync(config);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not record the lost Xero connection for ClientConfig {ConfigId}", config.Id);
            }
        }

        /// <summary>
        /// Xero has already rotated the refresh token, so losing this write would strand the connection once Xero's
        /// grace period for the old token ends. Retried a few times before giving up loudly.
        /// </summary>
        private async Task SaveRotatedTokensAsync(ClientConfig config)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await _clientConfigService.SaveTokensAsync(config);
                    return;
                }
                catch (Exception ex) when (attempt < 3)
                {
                    _logger.LogWarning(ex, "Saving the rotated Xero tokens for ClientConfig {ConfigId} failed (attempt {Attempt}); retrying", config.Id, attempt);
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt));
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(ex, "Could not save the rotated Xero tokens for ClientConfig {ConfigId}. The connection will need reconnecting if this persists.", config.Id);
                    throw;
                }
            }
        }

        public async Task<string?> ForceRefreshAsync(ClientConfig config)
        {
            if (string.IsNullOrEmpty(config.RefreshToken)) return null;

            var semaphore = _refreshLocks.GetOrAdd(config.Id, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            try
            {
                // Another request may have refreshed while we waited: a token that differs from the one
                // Xero just rejected is worth trying before spending a refresh.
                var (freshToken, freshRefresh, freshExpires) = await _clientConfigService.GetFreshTokensAsync(config.Id);
                if (!string.IsNullOrEmpty(freshToken) && freshToken != config.AccessToken)
                {
                    Adopt(config, freshToken, freshRefresh, freshExpires);
                    return freshToken;
                }

                _logger.LogInformation("Xero rejected the access token for ClientConfig {ConfigId} before its recorded expiry; refreshing", config.Id);
                var refreshed = await RefreshAccessTokenAsync(config);
                if (refreshed == null || !refreshed.IsSuccess)
                {
                    _refreshFailedAt[config.Id] = DateTimeOffset.UtcNow;
                    return null;
                }
                _refreshFailedAt.TryRemove(config.Id, out _);
                return refreshed.AccessToken;
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task<string?> GetValidAccessTokenAsync(ClientConfig config)
        {
            var buffer = TimeSpan.FromSeconds(TokenRefreshBufferSeconds);

            if (string.IsNullOrEmpty(config.AccessToken))
            {
                if (string.IsNullOrEmpty(config.RefreshToken))
                {
                    _logger.LogWarning("No access token or refresh token available for ClientConfig {ConfigId}", config.Id);
                    return null;
                }

                var bootstrapSemaphore = _refreshLocks.GetOrAdd(config.Id, _ => new SemaphoreSlim(1, 1));
                await bootstrapSemaphore.WaitAsync();
                try
                {
                    var (freshToken, freshRefresh, freshExpiresAt) = await _clientConfigService.GetFreshTokensAsync(config.Id);
                    if (!string.IsNullOrEmpty(freshToken) && (!freshExpiresAt.HasValue || freshExpiresAt.Value - DateTime.UtcNow > buffer))
                    {
                        Adopt(config, freshToken, freshRefresh, freshExpiresAt);
                        return freshToken;
                    }

                    var refreshResponse = await RefreshAccessTokenAsync(config);
                    return refreshResponse is { IsSuccess: true } ? refreshResponse.AccessToken : null;
                }
                finally
                {
                    bootstrapSemaphore.Release();
                }
            }

            if (config.TokenExpiresAt.HasValue)
            {
                var timeUntilExpiry = config.TokenExpiresAt.Value - DateTime.UtcNow;

                if (timeUntilExpiry > buffer)
                    return config.AccessToken;

                var semaphore = _refreshLocks.GetOrAdd(config.Id, _ => new SemaphoreSlim(1, 1));
                await semaphore.WaitAsync();
                try
                {
                    // Double-checked: another request may have refreshed while this one waited for the lock.
                    var (freshAccessToken, freshRefresh, freshExpiresAt) = await _clientConfigService.GetFreshTokensAsync(config.Id);
                    if (!string.IsNullOrEmpty(freshAccessToken) && freshExpiresAt.HasValue && freshExpiresAt.Value - DateTime.UtcNow > buffer)
                    {
                        Adopt(config, freshAccessToken, freshRefresh, freshExpiresAt);
                        return freshAccessToken;
                    }

                    if (_refreshFailedAt.TryGetValue(config.Id, out var failedAt) &&
                        DateTimeOffset.UtcNow - failedAt < TimeSpan.FromSeconds(RefreshFailureCooldownSeconds))
                    {
                        _logger.LogWarning(
                            "Token refresh for ClientConfig {ConfigId} failed {Seconds:F0}s ago; skipping retry during cooldown",
                            config.Id, (DateTimeOffset.UtcNow - failedAt).TotalSeconds);
                        return null;
                    }

                    if (timeUntilExpiry <= TimeSpan.Zero)
                    {
                        var tokenResponse = await RefreshAccessTokenAsync(config);
                        if (tokenResponse == null || !tokenResponse.IsSuccess)
                        {
                            _refreshFailedAt[config.Id] = DateTimeOffset.UtcNow;
                            return null;
                        }
                        _refreshFailedAt.TryRemove(config.Id, out _);
                        return tokenResponse.AccessToken;
                    }

                    var proactive = await RefreshAccessTokenAsync(config);
                    if (proactive == null || !proactive.IsSuccess)
                    {
                        if (string.IsNullOrEmpty(config.RefreshToken))
                            return null; // connection lost: the current token will stop working within minutes anyway
                        _logger.LogWarning("Proactive token refresh failed, returning current token valid for {Minutes:F1} more minutes",
                            timeUntilExpiry.TotalMinutes);
                        return config.AccessToken;
                    }
                    _refreshFailedAt.TryRemove(config.Id, out _);
                    return proactive.AccessToken;
                }
                finally
                {
                    semaphore.Release();
                }
            }

            return config.AccessToken;
        }

        public async Task<bool> DisconnectAsync(ClientConfig config)
        {
            if (!config.XeroConnectionId.HasValue) return false;
            try
            {
                var token = await GetValidAccessTokenAsync(config);
                if (string.IsNullOrEmpty(token)) return false;
                await ConnectionService.DisconnectAsync(token, config.XeroConnectionId.Value);
                return true;
            }
            catch (XeroApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return true; // already removed at Xero (the user disconnected the app there)
            }
            catch (Exception ex) when (ex is XeroApiException or HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "Could not remove the Xero connection for ClientConfig {ConfigId}", config.Id);
                return false;
            }
        }

        /// <summary>Takes the stored tokens onto this copy of the row, so nothing later works from a rotated refresh token.</summary>
        private static void Adopt(ClientConfig config, string access, string? refresh, DateTime? expires)
        {
            config.AccessToken = access;
            if (!string.IsNullOrEmpty(refresh)) config.RefreshToken = refresh;
            config.TokenExpiresAt = expires;
        }

        private static string? Cap(string? value, int max) =>
            value == null ? null : value.Trim() is var t && t.Length > max ? t[..max] : value.Trim();
    }
}
