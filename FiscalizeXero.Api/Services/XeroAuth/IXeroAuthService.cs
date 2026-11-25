using FiscalizeXero.Api.Data;
using FiscalizeXero.Xero.OAuth;

namespace FiscalizeXero.Api.Services.XeroAuth
{
    public interface IXeroAuthService
    {
        /// <summary>True when the server holds the app's Xero client id and secret.</summary>
        bool IsConfigured { get; }

        /// <summary>
        /// The Xero consent address for a connection attempt, or null when no Xero credentials are configured.
        /// <paramref name="state"/> comes back on the redirect and identifies the connection.
        /// </summary>
        string? BuildConsentUrl(Guid state);

        /// <summary>
        /// Swaps the authorisation code for tokens. Nothing is stored: the caller first finds out which organisation
        /// the consent was for (the redirect does not say) and only then calls <see cref="StoreConnectionAsync"/>.
        /// </summary>
        Task<XeroTokenResponse> ExchangeCodeAsync(string code);

        /// <summary>Stores a completed connection: tokens, expiry, organisation and Xero connection id.</summary>
        Task StoreConnectionAsync(ClientConfig config, XeroTokenResponse tokens, Guid tenantId, string? tenantName, Guid connectionId);

        /// <summary>
        /// Refreshes the access token. Xero rotates the refresh token on every refresh, so the new one is stored
        /// each time. Null when there is nothing to refresh with; an unsuccessful answer otherwise.
        /// </summary>
        Task<XeroTokenResponse?> RefreshAccessTokenAsync(ClientConfig config);

        /// <summary>A usable access token, refreshed first when it is within five minutes of expiring. Null when the connection cannot be used.</summary>
        Task<string?> GetValidAccessTokenAsync(ClientConfig config);

        /// <summary>
        /// Xero answered a call with 401 although the stored token looked valid: refresh now regardless of the
        /// expiry we recorded, once per connection at a time. Returns the new access token, or null when the
        /// connection is gone (revoked or expired refresh token) and the user must reconnect.
        /// </summary>
        Task<string?> ForceRefreshAsync(ClientConfig config);

        /// <summary>
        /// Removes this one organisation's connection at Xero (never revokes the refresh token, which would cut off
        /// every organisation the same Xero user connected). Best-effort: returns false, never throws, when there is
        /// nothing to remove or Xero could not be reached.
        /// </summary>
        Task<bool> DisconnectAsync(ClientConfig config);
    }
}
