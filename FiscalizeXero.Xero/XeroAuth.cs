namespace FiscalizeXero.Xero
{
    /// <summary>What one call to the Xero Accounting API needs: a bearer token and the organisation it is for.</summary>
    public class XeroAuth
    {
        /// <summary>Base of the Accounting API, without a trailing slash.</summary>
        public string ApiUrl { get; set; } = "https://api.xero.com/api.xro/2.0";

        public string Token { get; set; } = string.Empty;

        /// <summary>The Xero organisation (tenant). Sent as the <c>xero-tenant-id</c> header on every call.</summary>
        public Guid TenantId { get; set; }

        /// <summary>
        /// Optional hook the API layer sets so a 401 from Xero (expired or revoked access token) can be answered with
        /// one forced token refresh and a single retry. Returns the new access token, or null when the connection
        /// cannot be refreshed (the caller then surfaces the 401 and the user is asked to reconnect).
        /// </summary>
        public Func<Task<string?>>? RefreshAccessTokenAsync { get; set; }
    }
}
