using System.Net;

namespace FiscalizeXero.Xero
{
    /// <summary>
    /// Decides whether a failed Xero call is worth retrying and how long to wait. Pure functions so the policy can be
    /// unit tested without a network.
    ///
    /// Transient: HTTP 429 (a rate limit was hit), 502/503/504, 500 on idempotent calls, connection failures and
    /// client-side timeouts on idempotent calls. Never retried: other 4xx (validation, authentication,
    /// authorisation, invalid_grant), because repeating them cannot succeed.
    ///
    /// Xero's limits are per organisation: 60 calls a minute, a daily cap (1,000 on the Starter plan, 5,000 on paid
    /// plans) and 5 calls at once, plus 10,000 a minute across the whole app. A 429 carries <c>Retry-After</c> in
    /// seconds and <c>X-Rate-Limit-Problem</c> naming the limit. A minute limit clears within <see cref="MaxDelay"/>
    /// and is waited out; a daily limit does not, so it is surfaced instead of holding a request open for hours.
    /// </summary>
    public static class XeroRetryPolicy
    {
        /// <summary>Total attempts including the first one.</summary>
        public const int MaxAttempts = 4;

        /// <summary>Base delay; doubles per attempt (1s, 2s, 4s) with up to 25% jitter.</summary>
        public static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(1);

        /// <summary>The longest a single retry waits. A minute-limit 429 asks for at most 60 seconds.</summary>
        public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

        public static bool IsIdempotent(HttpMethod method) =>
            method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options || method == HttpMethod.Delete;

        /// <summary>
        /// Whether a response status is worth another attempt. Non-idempotent calls (POST/PUT) are only retried when
        /// the server says it did not process the request (429, 502, 503, 504); a 500 on a POST might have been
        /// applied already, so it is surfaced instead.
        /// </summary>
        public static bool ShouldRetry(HttpStatusCode status, HttpMethod method, int attempt)
        {
            if (attempt >= MaxAttempts) return false;
            return status switch
            {
                HttpStatusCode.TooManyRequests => true,
                HttpStatusCode.BadGateway => true,
                HttpStatusCode.ServiceUnavailable => true,
                HttpStatusCode.GatewayTimeout => true,
                HttpStatusCode.InternalServerError => IsIdempotent(method),
                _ => false
            };
        }

        /// <summary>
        /// As <see cref="ShouldRetry(HttpStatusCode, HttpMethod, int)"/>, but a 429 whose Retry-After is longer than
        /// <see cref="MaxDelay"/> (the daily limit) is not retried: waiting cannot help within a request.
        /// </summary>
        public static bool ShouldRetry(HttpResponseMessage response, HttpMethod method, int attempt)
        {
            if (!ShouldRetry(response.StatusCode, method, attempt)) return false;
            if (response.StatusCode != HttpStatusCode.TooManyRequests) return true;
            var wait = RetryAfter(response);
            return !wait.HasValue || wait.Value <= MaxDelay;
        }

        /// <summary>
        /// Whether a transport failure (no response at all) is worth another attempt. Only for idempotent calls:
        /// a POST whose connection dropped may have reached Xero.
        /// </summary>
        public static bool ShouldRetry(Exception transportError, HttpMethod method, int attempt)
        {
            if (attempt >= MaxAttempts) return false;
            if (!IsIdempotent(method)) return false;
            return transportError is HttpRequestException ||
                   transportError is TaskCanceledException tce && !tce.CancellationToken.IsCancellationRequested ||
                   transportError is TimeoutException;
        }

        /// <summary>Exponential backoff with jitter, or the server's Retry-After when it gave one, capped at <see cref="MaxDelay"/>.</summary>
        public static TimeSpan DelayFor(int attempt, TimeSpan? retryAfter = null, Random? random = null)
        {
            if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero)
                return retryAfter.Value > MaxDelay ? MaxDelay : retryAfter.Value;

            var exponential = BaseDelay * Math.Pow(2, Math.Max(0, attempt - 1));
            var jitter = 1 + ((random ?? Random.Shared).NextDouble() * 0.5 - 0.25);
            var delay = TimeSpan.FromMilliseconds(exponential.TotalMilliseconds * jitter);
            return delay > MaxDelay ? MaxDelay : delay;
        }

        /// <summary>Reads Retry-After (seconds or HTTP date) from a response, or null.</summary>
        public static TimeSpan? RetryAfter(HttpResponseMessage response)
        {
            var ra = response.Headers.RetryAfter;
            if (ra == null) return null;
            if (ra.Delta.HasValue) return ra.Delta;
            if (ra.Date.HasValue)
            {
                var wait = ra.Date.Value - DateTimeOffset.UtcNow;
                return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
            }
            return null;
        }

        /// <summary>Which limit a 429 hit: "minute", "day" or "appminute" (the <c>X-Rate-Limit-Problem</c> header), or null.</summary>
        public static string? RateLimitProblem(HttpResponseMessage response) => Header(response, "X-Rate-Limit-Problem");

        /// <summary>Xero's per-request id from the <c>Xero-Correlation-Id</c> response header. Quote it to Xero support.</summary>
        public static string? CorrelationId(HttpResponseMessage response) => Header(response, "Xero-Correlation-Id");

        /// <summary>Calls left today for this organisation (<c>X-DayLimit-Remaining</c>), or null when Xero did not say.</summary>
        public static int? DayLimitRemaining(HttpResponseMessage response) =>
            int.TryParse(Header(response, "X-DayLimit-Remaining"), out var n) ? n : null;

        private static string? Header(HttpResponseMessage response, string name) =>
            response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }
}
