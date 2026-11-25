using FiscalizeXero.Fiscalize.Exceptions;
using FiscalizeXero.Shared.Models.Auths;
using System.Text;
using System.Text.Json;

namespace FiscalizeXero.Fiscalize
{
    public class RestClient
    {
        /// <summary>
        /// One connection pool for every platform call. A new handler per call opened (and left in TIME_WAIT) a socket
        /// each time, which runs a busy server out of ports; connections are recycled every few minutes so a DNS
        /// change on the platform is still picked up.
        /// </summary>
        private static readonly SocketsHttpHandler SharedHandler = new()
        {
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };

        private static HttpClient CreateClient(string token)
        {
            // The client is a cheap per-call wrapper (its own headers); disposing it leaves the shared pool open.
            var httpClient = new HttpClient(SharedHandler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(60) };
            httpClient.DefaultRequestHeaders.Clear();
            if (!string.IsNullOrEmpty(token))
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
            return httpClient;
        }

        private static StringContent SerializeContent<T>(T obj)
        {
            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        private static async Task<TResponse> HandleResponse<TResponse>(HttpResponseMessage resp)
        {
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new FiscalizeApiException(resp.StatusCode, body);

            return JsonSerializer.Deserialize<TResponse>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })!;
        }

        // GET
        public static async Task<TResponse> GetAsync<TResponse>(FiscalizeAuthModel auth, string endPoint)
        {
            using var httpClient = CreateClient(auth.Token);
            var resp = await httpClient.GetAsync(new Uri($"{auth.ApiUrl}/{endPoint}"));
            return await HandleResponse<TResponse>(resp);
        }

        // POST
        public static async Task<TResponse> PostAsync<TRequest, TResponse>(FiscalizeAuthModel auth, string endPoint, TRequest payload)
        {
            using var httpClient = CreateClient(auth.Token);
            var resp = await httpClient.PostAsync(new Uri($"{auth.ApiUrl}/{endPoint}"), SerializeContent(payload));
            return await HandleResponse<TResponse>(resp);
        }

        // PUT
        // POST expecting no response body (e.g. 204 No Content). Only verifies success.
        public static async Task PostNoContentAsync<TRequest>(FiscalizeAuthModel auth, string endPoint, TRequest payload)
        {
            using var httpClient = CreateClient(auth.Token);
            var resp = await httpClient.PostAsync(new Uri($"{auth.ApiUrl}/{endPoint}"), SerializeContent(payload));
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new FiscalizeApiException(resp.StatusCode, body);
            }
        }

        public static async Task<TResponse> PutAsync<TRequest, TResponse>(FiscalizeAuthModel auth, string endPoint, TRequest payload)
        {
            using var httpClient = CreateClient(auth.Token);
            var resp = await httpClient.PutAsync(new Uri($"{auth.ApiUrl}/{endPoint}"), SerializeContent(payload));
            return await HandleResponse<TResponse>(resp);
        }

        // PATCH
        public static async Task<TResponse> PatchAsync<TRequest, TResponse>(FiscalizeAuthModel auth, string endPoint, TRequest payload)
        {
            using var httpClient = CreateClient(auth.Token);
            var request = new HttpRequestMessage(new HttpMethod("PATCH"), new Uri($"{auth.ApiUrl}/{endPoint}"))
            {
                Content = SerializeContent(payload)
            };
            var resp = await httpClient.SendAsync(request);
            return await HandleResponse<TResponse>(resp);
        }

        // DELETE
        public static async Task<TResponse> DeleteAsync<TResponse>(FiscalizeAuthModel auth, string endPoint)
        {
            using var httpClient = CreateClient(auth.Token);
            var resp = await httpClient.DeleteAsync(new Uri($"{auth.ApiUrl}/{endPoint}"));
            return await HandleResponse<TResponse>(resp);
        }

        /// <summary>The status and body of a GET, never throwing on a non-success status. Sends <c>If-None-Match</c> when an ETag is given.</summary>
        public static async Task<(System.Net.HttpStatusCode Status, string Body)> GetRawAsync(FiscalizeAuthModel auth, string endPoint, string? etag = null)
        {
            using var httpClient = CreateClient(auth.Token);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{auth.ApiUrl}/{endPoint}"));
            if (!string.IsNullOrWhiteSpace(etag))
                request.Headers.TryAddWithoutValidation("If-None-Match", $"\"{etag.Trim('"')}\"");
            var resp = await httpClient.SendAsync(request);
            var body = resp.Content == null ? string.Empty : await resp.Content.ReadAsStringAsync();
            return (resp.StatusCode, body);
        }

        // GET raw bytes (e.g. PDF download)
        public static async Task<byte[]> GetBytesAsync(FiscalizeAuthModel auth, string endPoint)
        {
            using var httpClient = CreateClient(auth.Token);
            var resp = await httpClient.GetAsync(new Uri($"{auth.ApiUrl}/{endPoint}"));
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new FiscalizeApiException(resp.StatusCode, body);
            }
            return await resp.Content.ReadAsByteArrayAsync();
        }

        // HEAD (returns only status, so no TResponse)
        public static async Task<HttpResponseMessage> HeadAsync(FiscalizeAuthModel auth, string endPoint)
        {
            using var httpClient = CreateClient(auth.Token);
            var request = new HttpRequestMessage(HttpMethod.Head, new Uri($"{auth.ApiUrl}/{endPoint}"));
            return await httpClient.SendAsync(request);
        }

        // OPTIONS (returns only status/headers, so no TResponse)
        public static async Task<HttpResponseMessage> OptionsAsync(FiscalizeAuthModel auth, string endPoint)
        {
            using var httpClient = CreateClient(auth.Token);
            var request = new HttpRequestMessage(HttpMethod.Options, new Uri($"{auth.ApiUrl}/{endPoint}"));
            return await httpClient.SendAsync(request);
        }
    }
}