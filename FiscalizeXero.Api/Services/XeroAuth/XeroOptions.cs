using FiscalizeXero.Xero.OAuth;

namespace FiscalizeXero.Api.Services.XeroAuth
{
    /// <summary>
    /// The <c>Xero</c> configuration section: the app's one registration at developer.xero.com (client id, secret,
    /// redirect URI and scopes, inherited from <see cref="XeroOAuthSettings"/>) plus what this API adds around it.
    /// Xero has no sandbox/production split and customers never bring their own credentials.
    /// </summary>
    public class XeroOptions : XeroOAuthSettings
    {
        public const string SectionName = "Xero";

        /// <summary>
        /// Raised whenever <see cref="XeroOAuthSettings.Scope"/> gains a scope. A connection granted under a lower
        /// version is asked to re-authorise.
        /// </summary>
        public int ScopeVersion { get; set; } = 1;

        /// <summary>
        /// The webhook key the Xero developer portal shows once the webhook is saved. Every delivery is signed with
        /// it (HMAC-SHA256 in the x-xero-signature header). Empty = every delivery is answered 401.
        /// </summary>
        public string? WebhookKey { get; set; }

        /// <summary>
        /// Public https origin Xero can reach, e.g. https://apps.onaerp.africa. The webhook address shown to users
        /// is this + PathBase + /api/xero/webhooks.
        /// </summary>
        public string WebhookCallbackBaseUrl { get; set; } = string.Empty;

        /// <summary>Where "Open in Xero" links go. Xero signs the user in and shows the organisation they last used.</summary>
        public const string XeroWebUrl = "https://go.xero.com/";

        /// <summary>
        /// "Open in Xero" for one organisation: with its short code Xero opens that organisation (signing the user in first
        /// if needed) instead of the one they last used. Without the code (not read from Xero yet) it is <see cref="XeroWebUrl"/>.
        /// </summary>
        public static string XeroWebUrlFor(string? organisationShortCode) =>
            string.IsNullOrWhiteSpace(organisationShortCode)
                ? XeroWebUrl
                : $"{XeroWebUrl}organisationlogin/default.aspx?shortcode={Uri.EscapeDataString(organisationShortCode.Trim())}";
    }
}
