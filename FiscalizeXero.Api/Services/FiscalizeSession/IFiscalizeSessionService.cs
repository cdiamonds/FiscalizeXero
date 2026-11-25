using FiscalizeXero.Api.Data;
using FiscalizeXero.Shared.Models.Auths;

namespace FiscalizeXero.Api.Services.FiscalizeSession
{
    /// <summary>What a keep-alive found.</summary>
    public enum PlatformSessionState
    {
        /// <summary>The session works (renewed if needed).</summary>
        Active,
        /// <summary>No company chosen yet: nothing to keep alive.</summary>
        NoCompany,
        /// <summary>The platform couldn't be reached; the session may be fine. Never a reason to ask for a sign-in.</summary>
        PlatformUnavailable
    }

    /// <summary>
    /// The signed-in user's session on the Fiscalize platform (used for user-level calls: company list, HS codes,
    /// devices and keys). The platform token is short-lived; it is refreshed silently with the stored refresh token
    /// whenever it has lapsed, so a user only has to sign in again when the refresh token itself is gone or rejected.
    /// </summary>
    public interface IFiscalizeSessionService
    {
        /// <summary>
        /// A valid platform session for the current user's active company, refreshed first when it has lapsed.
        /// Throws FiscalizeSessionExpiredException only when no usable token can be obtained.
        /// </summary>
        Task<FiscalizeAuthModel> GetAuthAsync();

        /// <summary>
        /// The same for a given company row (background work, or code that already holds the entity). The row is
        /// updated in place and saved when a refresh happens. Returns null when no usable token can be obtained.
        /// </summary>
        Task<FiscalizeAuthModel?> TryGetAuthForCompanyAsync(AppUserCompany company);

        /// <summary>
        /// Forces a refresh now (after the platform rejected a token that looked valid). When the company's own refresh
        /// token is gone or refused, a new company session is opened from the user's own platform session. Null when
        /// neither works.
        /// </summary>
        Task<FiscalizeAuthModel?> TryRefreshAsync(AppUserCompany company);

        /// <summary>
        /// The user's own platform session from their last direct sign-in (not tied to a company), refreshed first
        /// when it has lapsed. It can list the user's companies and open a session on one. Null when there is none.
        /// </summary>
        Task<FiscalizeAuthModel?> TryGetUserAuthAsync(AppUser user);

        /// <summary>
        /// Clears the current user's Fiscalize token from the database.
        /// </summary>
        Task ClearSessionAsync();

        /// <summary>
        /// Runs a platform call with the active company's session. A lapsed session is renewed first; when the platform
        /// rejects the session anyway (it ends idle sessions before their expiry), it is renewed once and the call
        /// retried. Only when it can't be renewed does this throw FiscalizeSessionExpiredException, which the browser
        /// shows as "sign in again", never as an error on the page.
        /// </summary>
        Task<T> RunAsync<T>(Func<FiscalizeAuthModel, Task<T>> call);

        /// <summary>
        /// Keeps the active company's platform session alive while the user works (renewed ahead of expiry, otherwise
        /// used once so the platform doesn't end it as idle). Throws FiscalizeSessionExpiredException when it is over.
        /// </summary>
        Task<PlatformSessionState> KeepAliveAsync();
    }
}
