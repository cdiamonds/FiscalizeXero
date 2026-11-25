using FiscalizeXero.Xero.Exceptions;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FiscalizeXero.Xero
{
    /// <summary>
    /// Thin HTTP layer over the Xero Accounting API. Every call:
    /// <list type="bullet">
    ///   <item>carries the bearer token and the <c>xero-tenant-id</c> header;</item>
    ///   <item>retries transient failures (a minute rate limit, 5xx, dropped connections) with backoff, honouring
    ///         Retry-After (<see cref="XeroRetryPolicy"/>);</item>
    ///   <item>answers one 401 (expired or revoked access token) with a forced token refresh through
    ///         <see cref="XeroAuth.RefreshAccessTokenAsync"/> and a single retry;</item>
    ///   <item>surfaces failures as <see cref="XeroApiException"/> carrying Xero's parsed error and correlation id.</item>
    /// </list>
    /// </summary>
    public static class RestClient
    {
        // One pooled client for every Xero call. A new HttpClient per request exhausts sockets under load;
        // PooledConnectionLifetime rotates connections so DNS changes are picked up. Xero allows 5 calls at once per
        // organisation, and connections are shared by all organisations, so the pool is larger than that.
        private static readonly HttpClient _client = new HttpClient(
            new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 20,
                UseCookies = false
            })
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        /// <summary>Xero's JSON is PascalCase and it ignores nulls, so null properties are left out of what we send.</summary>
        public static readonly JsonSerializerOptions WriteOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        private static HttpRequestMessage BuildRequest(HttpMethod method, XeroAuth auth, string token, Uri uri,
            HttpContent? content, string accept, DateTime? modifiedSince)
        {
            var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            // Xero answers in XML unless JSON is asked for.
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            if (auth.TenantId != Guid.Empty)
                request.Headers.TryAddWithoutValidation("xero-tenant-id", auth.TenantId.ToString());
            if (modifiedSince.HasValue)
                request.Headers.IfModifiedSince = new DateTimeOffset(DateTime.SpecifyKind(modifiedSince.Value, DateTimeKind.Utc));
            if (content != null)
                request.Content = content;
            return request;
        }

        /// <summary>
        /// Serialised once; a fresh StringContent per attempt, because disposing a request disposes its content and a
        /// retry must send a live body.
        /// </summary>
        private static Func<HttpContent?> SerializeContent<T>(T obj)
        {
            var json = JsonSerializer.Serialize(obj, WriteOptions);
            return () => new StringContent(json, Encoding.UTF8, "application/json");
        }

        /// <summary>
        /// Sends a request built fresh for every attempt (an HttpRequestMessage can only be sent once). Handles
        /// retries and the one-off token refresh; the returned response is a success or the caller's to throw.
        /// </summary>
        private static async Task<HttpResponseMessage> SendWithPolicyAsync(XeroAuth auth, HttpMethod method, Func<string, HttpRequestMessage> build)
        {
            var token = auth.Token;
            var refreshed = false;
            var attempt = 0;

            while (true)
            {
                attempt++;
                HttpResponseMessage response;
                using var request = build(token);
                try
                {
                    response = await _client.SendAsync(request);
                }
                catch (Exception ex) when (XeroRetryPolicy.ShouldRetry(ex, method, attempt))
                {
                    await Task.Delay(XeroRetryPolicy.DelayFor(attempt));
                    continue;
                }
                catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
                {
                    throw new XeroApiException(HttpStatusCode.GatewayTimeout,
                        $"{{\"Message\":\"Xero did not respond within {_client.Timeout.TotalSeconds:0} seconds. Please try again.\"}}");
                }

                if (response.IsSuccessStatusCode)
                    return response;

                // Expired or revoked access token: refresh once and replay. Anything after that is the caller's.
                if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshed && auth.RefreshAccessTokenAsync != null)
                {
                    refreshed = true;
                    var fresh = await auth.RefreshAccessTokenAsync();
                    if (!string.IsNullOrEmpty(fresh) && fresh != token)
                    {
                        response.Dispose();
                        token = fresh;
                        auth.Token = fresh;
                        continue;
                    }
                }

                if (XeroRetryPolicy.ShouldRetry(response, method, attempt))
                {
                    var wait = XeroRetryPolicy.DelayFor(attempt, XeroRetryPolicy.RetryAfter(response));
                    response.Dispose();
                    await Task.Delay(wait);
                    continue;
                }

                return response;
            }
        }

        private static async Task<XeroApiException> ToExceptionAsync(HttpResponseMessage resp)
        {
            var body = await resp.Content.ReadAsStringAsync();
            return new XeroApiException(resp.StatusCode, body, XeroRetryPolicy.CorrelationId(resp), XeroRetryPolicy.RateLimitProblem(resp));
        }

        private static async Task<TResponse> HandleResponse<TResponse>(HttpResponseMessage resp)
        {
            // A "modified since" request with nothing newer may be answered 304 Not Modified: that is "no rows", not an error.
            if (resp.StatusCode == HttpStatusCode.NotModified)
                return default!;
            if (!resp.IsSuccessStatusCode)
                throw await ToExceptionAsync(resp);

            var body = await resp.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(body))
                return default!;
            return JsonSerializer.Deserialize<TResponse>(body, ReadOptions)!;
        }

        /// <summary>Accepts either an endpoint relative to the Accounting API ("Invoices/{id}") or a full URL.</summary>
        private static Uri BuildUri(XeroAuth auth, string endPoint) =>
            new(endPoint.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? endPoint
                : $"{auth.ApiUrl.TrimEnd('/')}/{endPoint.TrimStart('/')}");

        private static async Task<TResponse> SendJsonAsync<TResponse>(XeroAuth auth, HttpMethod method, string endPoint,
            Func<HttpContent?>? content = null, DateTime? modifiedSince = null)
        {
            var uri = BuildUri(auth, endPoint);
            using var resp = await SendWithPolicyAsync(auth, method,
                token => BuildRequest(method, auth, token, uri, content?.Invoke(), "application/json", modifiedSince));
            return await HandleResponse<TResponse>(resp);
        }

        /// <summary>GET. <paramref name="modifiedSince"/> (UTC) asks Xero for only what was created or changed after it.</summary>
        public static Task<TResponse> GetAsync<TResponse>(XeroAuth auth, string endPoint, DateTime? modifiedSince = null) =>
            SendJsonAsync<TResponse>(auth, HttpMethod.Get, endPoint, null, modifiedSince);

        /// <summary>POST: in Xero this creates or updates (an update names the record's id).</summary>
        public static Task<TResponse> PostAsync<TRequest, TResponse>(XeroAuth auth, string endPoint, TRequest payload) =>
            SendJsonAsync<TResponse>(auth, HttpMethod.Post, endPoint, SerializeContent(payload));

        /// <summary>PUT: in Xero this only creates (history notes use it).</summary>
        public static Task<TResponse> PutAsync<TRequest, TResponse>(XeroAuth auth, string endPoint, TRequest payload) =>
            SendJsonAsync<TResponse>(auth, HttpMethod.Put, endPoint, SerializeContent(payload));

        public static async Task DeleteAsync(XeroAuth auth, string endPoint)
        {
            var uri = BuildUri(auth, endPoint);
            using var resp = await SendWithPolicyAsync(auth, HttpMethod.Delete,
                token => BuildRequest(HttpMethod.Delete, auth, token, uri, null, "application/json", null));
            if (!resp.IsSuccessStatusCode)
                throw await ToExceptionAsync(resp);
        }

        /// <summary>GET a document as a file. Xero renders an invoice or credit note as PDF when asked for <c>application/pdf</c>.</summary>
        public static async Task<byte[]> GetBytesAsync(XeroAuth auth, string endPoint, string accept = "application/pdf")
        {
            var uri = BuildUri(auth, endPoint);
            using var resp = await SendWithPolicyAsync(auth, HttpMethod.Get,
                token => BuildRequest(HttpMethod.Get, auth, token, uri, null, accept, null));
            if (!resp.IsSuccessStatusCode)
                throw await ToExceptionAsync(resp);
            return await resp.Content.ReadAsByteArrayAsync();
        }

        /// <summary>
        /// POST raw bytes (an attachment's content). The body is an in-memory array, so each attempt sends a fresh copy
        /// and the call is retried like any other POST.
        /// </summary>
        public static async Task<TResponse> PostBytesAsync<TResponse>(XeroAuth auth, string endPoint, byte[] bytes, string contentType)
        {
            var uri = BuildUri(auth, endPoint);
            HttpContent Content()
            {
                var content = new ByteArrayContent(bytes);
                content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                return content;
            }
            using var resp = await SendWithPolicyAsync(auth, HttpMethod.Post,
                token => BuildRequest(HttpMethod.Post, auth, token, uri, Content(), "application/json", null));
            return await HandleResponse<TResponse>(resp);
        }
    }
}
