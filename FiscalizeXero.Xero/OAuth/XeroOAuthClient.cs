using FiscalizeXero.Xero.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FiscalizeXero.Xero.OAuth
{
    /// <summary>The app's own registration at developer.xero.com, plus Xero's fixed OAuth endpoints.</summary>
    public class XeroOAuthSettings
    {
        public string AuthorizeUrl { get; set; } = "https://login.xero.com/identity/connect/authorize";
        public string TokenUrl { get; set; } = "https://identity.xero.com/connect/token";
        public string RevocationUrl { get; set; } = "https://identity.xero.com/connect/revocation";

        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>Must match a redirect URI registered on the app exactly (scheme, host, port, path).</summary>
        public string RedirectUri { get; set; } = string.Empty;

        /// <summary>
        /// Space-separated scopes. <c>offline_access</c> is what makes Xero issue a refresh token. Apps created after
        /// 2 March 2026 only get the granular scopes: <c>accounting.invoices</c> covers invoices and credit notes
        /// (the old <c>accounting.transactions</c> is refused with invalid_scope).
        /// </summary>
        public string Scope { get; set; } =
            "openid profile email offline_access accounting.invoices accounting.contacts accounting.settings.read accounting.attachments";

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
    }

    public class XeroTokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
        /// <summary>Xero rotates the refresh token on every refresh; the one in a response replaces the stored one.</summary>
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        /// <summary>OpenID Connect id_token: who signed in. Present when the <c>openid</c> scope was requested.</summary>
        [JsonPropertyName("id_token")] public string? IdToken { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
        [JsonPropertyName("token_type")] public string? TokenType { get; set; }
        /// <summary>Seconds until the access token expires (30 minutes for Xero).</summary>
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("error_description")] public string? ErrorDescription { get; set; }

        /// <summary>HTTP status of the token call. Not part of Xero's JSON.</summary>
        [JsonIgnore] public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        [JsonIgnore] public bool IsSuccess => string.IsNullOrEmpty(Error) && !string.IsNullOrEmpty(AccessToken);

        /// <summary>
        /// The refresh token is dead: expired (60 days unused), already rotated away, or revoked because the user
        /// disconnected the app inside Xero. Retrying cannot help; the user must connect again.
        /// <c>invalid_client</c> means our own credentials are wrong, which retrying cannot fix either.
        /// </summary>
        [JsonIgnore]
        public bool IsConnectionLost =>
            string.Equals(Error, "invalid_grant", StringComparison.OrdinalIgnoreCase) ||
            (Status == HttpStatusCode.Unauthorized && string.Equals(Error, "invalid_client", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Xero's OAuth 2.0 authorisation-code flow for a confidential web app: build the consent address, swap the code
    /// for tokens, refresh, revoke. Holds no state and knows nothing about where tokens are stored.
    /// </summary>
    public class XeroOAuthClient
    {
        private readonly HttpClient _http;
        private readonly XeroOAuthSettings _settings;

        public XeroOAuthClient(HttpClient http, XeroOAuthSettings settings)
        {
            _http = http;
            _settings = settings;
        }

        /// <summary>The address to send the user to. <paramref name="state"/> comes back on the redirect and must be checked.</summary>
        public string BuildConsentUrl(string state) =>
            $"{_settings.AuthorizeUrl}?response_type=code" +
            $"&client_id={Uri.EscapeDataString(_settings.ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(_settings.RedirectUri)}" +
            $"&scope={Uri.EscapeDataString(_settings.Scope)}" +
            $"&state={Uri.EscapeDataString(state)}";

        public Task<XeroTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct = default) =>
            PostTokenAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = _settings.RedirectUri
            }, ct);

        public Task<XeroTokenResponse> RefreshAsync(string refreshToken, CancellationToken ct = default) =>
            PostTokenAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            }, ct);

        /// <summary>
        /// Revokes a refresh token, which removes every organisation connected under that sign-in. To remove one
        /// organisation only, use <see cref="Services.ConnectionService.DisconnectAsync"/>. Returns false, never
        /// throws, when Xero could not be reached or refused.
        /// </summary>
        public async Task<bool> RevokeAsync(string refreshToken, CancellationToken ct = default)
        {
            try
            {
                using var response = await SendAsync(_settings.RevocationUrl,
                    new Dictionary<string, string> { ["token"] = refreshToken }, ct);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return false;
            }
        }

        private async Task<XeroTokenResponse> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
        {
            HttpResponseMessage response;
            try
            {
                response = await SendAsync(_settings.TokenUrl, form, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return new XeroTokenResponse { Error = "unreachable", ErrorDescription = ex.Message, Status = HttpStatusCode.ServiceUnavailable };
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                XeroTokenResponse parsed;
                try { parsed = JsonSerializer.Deserialize<XeroTokenResponse>(body) ?? new XeroTokenResponse(); }
                catch (JsonException) { parsed = new XeroTokenResponse(); }

                parsed.Status = response.StatusCode;
                if (!response.IsSuccessStatusCode && string.IsNullOrEmpty(parsed.Error))
                    parsed.Error = $"http_{(int)response.StatusCode}";
                if (response.IsSuccessStatusCode && string.IsNullOrEmpty(parsed.AccessToken) && string.IsNullOrEmpty(parsed.Error))
                    parsed.Error = "empty_response";
                return parsed;
            }
        }

        /// <summary>
        /// One form POST to Xero's identity server with HTTP Basic client authentication. Retried only when Xero says
        /// it did not process the request (429, 502-504) or never answered: a refresh token is only rotated by a
        /// response we received, so repeating an unanswered call is safe.
        /// </summary>
        private async Task<HttpResponseMessage> SendAsync(string url, Dictionary<string, string> form, CancellationToken ct)
        {
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            var attempt = 0;
            while (true)
            {
                attempt++;
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

                HttpResponseMessage response;
                try
                {
                    response = await _http.SendAsync(request, ct);
                }
                catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
                                           && attempt < XeroRetryPolicy.MaxAttempts)
                {
                    await Task.Delay(XeroRetryPolicy.DelayFor(attempt), ct);
                    continue;
                }

                if (XeroRetryPolicy.ShouldRetry(response, HttpMethod.Post, attempt))
                {
                    var wait = XeroRetryPolicy.DelayFor(attempt, XeroRetryPolicy.RetryAfter(response));
                    response.Dispose();
                    await Task.Delay(wait, ct);
                    continue;
                }
                return response;
            }
        }
    }

    /// <summary>
    /// Reads claims from a Xero token without validating it. Only for values we then confirm with Xero itself (the
    /// connections list), never for deciding who someone is.
    /// </summary>
    public static class XeroTokenClaims
    {
        /// <summary>
        /// The id of the consent that produced an access token. Xero stamps the same id on each connection that
        /// consent created (<see cref="XeroConnection.AuthEventId"/>), which is how the organisation the user just
        /// picked is told apart from organisations they connected earlier.
        /// </summary>
        public static Guid? AuthenticationEventId(string? accessToken) =>
            Guid.TryParse(Claim(accessToken, "authentication_event_id"), out var id) ? id : null;

        public static string? Claim(string? jwt, string name)
        {
            if (string.IsNullOrWhiteSpace(jwt)) return null;
            var parts = jwt.Split('.');
            if (parts.Length < 2) return null;
            try
            {
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
                return doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Which of the user's connections a consent was for: the organisations stamped with the token's
        /// authentication event. Falls back to the most recently created organisation when Xero gave no event id.
        /// </summary>
        public static List<XeroConnection> ConnectionsFromConsent(IEnumerable<XeroConnection> connections, string? accessToken)
        {
            var organisations = connections.Where(c => c.IsOrganisation).ToList();
            var eventId = AuthenticationEventId(accessToken);
            if (eventId.HasValue)
            {
                var matched = organisations.Where(c => c.AuthEventId == eventId).ToList();
                if (matched.Count > 0) return matched;
            }
            return organisations.OrderByDescending(c => c.UpdatedDateUtc ?? c.CreatedDateUtc ?? DateTime.MinValue).Take(1).ToList();
        }
    }
}
