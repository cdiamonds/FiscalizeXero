using System.Net;
using System.Text.Json;

namespace FiscalizeXero.Xero.Exceptions
{
    /// <summary>
    /// Thrown when Xero answers a call with an error. Understands Xero's three error shapes: the Accounting API's
    /// <c>{"ErrorNumber":10,"Type":"ValidationException","Message":...,"Elements":[{"ValidationErrors":[...]}]}</c>,
    /// the gateway's problem details (<c>{"Title":...,"Status":401,"Detail":...}</c>) and the OAuth
    /// <c>{"error":...}</c> shape. Carries the <c>Xero-Correlation-Id</c> header Xero support asks for.
    /// </summary>
    public class XeroApiException : Exception
    {
        /// <summary>HTTP status code of Xero's response.</summary>
        public HttpStatusCode StatusCode { get; }

        /// <summary>Xero's <c>ErrorNumber</c> when the body carries one (10 = validation), else the HTTP status.</summary>
        public int? XeroErrorNumber { get; }

        /// <summary>Xero's error <c>Type</c> (ValidationException, PostDataInvalidException, ...) when the body carries one.</summary>
        public string? ErrorType { get; }

        /// <summary>Each validation message Xero gave, in order. Empty for non-validation errors.</summary>
        public IReadOnlyList<string> ValidationErrors { get; }

        /// <summary>Xero's per-request id from the <c>Xero-Correlation-Id</c> response header.</summary>
        public string? CorrelationId { get; }

        /// <summary>Which rate limit a 429 hit ("minute", "day", "appminute"), from <c>X-Rate-Limit-Problem</c>.</summary>
        public string? RateLimitProblem { get; }

        /// <summary>Raw response body.</summary>
        public string ResponseBody { get; }

        /// <summary>True for a 401: the access token is expired or invalid and a refresh may fix it.</summary>
        public bool IsAuthenticationFailure => StatusCode == HttpStatusCode.Unauthorized;

        /// <summary>
        /// True for a 403: the token is valid but may not do this. For Xero that means the organisation was
        /// disconnected from the app, or the app lacks the scope for this endpoint.
        /// </summary>
        public bool IsForbidden => StatusCode == HttpStatusCode.Forbidden;

        /// <summary>True for a 429.</summary>
        public bool IsThrottled => StatusCode == HttpStatusCode.TooManyRequests;

        /// <summary>True when the organisation's daily call allowance is used up; it resets at midnight UTC.</summary>
        public bool IsDailyLimit => IsThrottled && string.Equals(RateLimitProblem, "day", StringComparison.OrdinalIgnoreCase);

        /// <summary>True when Xero refused the data itself (HTTP 400 with validation messages), not the request.</summary>
        public bool IsValidation => StatusCode == HttpStatusCode.BadRequest && ValidationErrors.Count > 0;

        public XeroApiException(HttpStatusCode statusCode, string responseBody, string? correlationId = null, string? rateLimitProblem = null)
            : base(BuildMessage(statusCode, responseBody, rateLimitProblem))
        {
            StatusCode = statusCode;
            ResponseBody = responseBody ?? string.Empty;
            CorrelationId = correlationId;
            RateLimitProblem = rateLimitProblem;
            var parsed = Parse(ResponseBody);
            XeroErrorNumber = parsed.ErrorNumber ?? (int)statusCode;
            ErrorType = parsed.Type;
            ValidationErrors = parsed.ValidationErrors;
        }

        /// <summary>
        /// Loggers write <c>ToString()</c>, so the correlation id, status and error type land in the log line next to
        /// the message without every call site having to remember them.
        /// </summary>
        public override string ToString()
        {
            var id = string.IsNullOrEmpty(CorrelationId) ? "" : $" xero_correlation_id={CorrelationId}";
            var type = string.IsNullOrEmpty(ErrorType) ? "" : $" type={ErrorType}";
            var limit = string.IsNullOrEmpty(RateLimitProblem) ? "" : $" limit={RateLimitProblem}";
            return $"XeroApiException: HTTP {(int)StatusCode}{type}{limit}{id}: {Message}" +
                   (InnerException != null ? $" ---> {InnerException}" : "") +
                   (StackTrace != null ? Environment.NewLine + StackTrace : "");
        }

        private sealed record Parsed(int? ErrorNumber, string? Type, string? Message, IReadOnlyList<string> ValidationErrors);

        private static string BuildMessage(HttpStatusCode statusCode, string responseBody, string? rateLimitProblem)
        {
            if (statusCode == HttpStatusCode.TooManyRequests)
            {
                return string.Equals(rateLimitProblem, "day", StringComparison.OrdinalIgnoreCase)
                    ? "Xero's daily limit of calls for this organisation has been reached. It resets at midnight UTC."
                    : "Xero is receiving too many calls for this organisation right now. Please try again in a minute.";
            }

            var parsed = Parse(responseBody);
            if (parsed.ValidationErrors.Count > 0)
                return string.Join("; ", parsed.ValidationErrors);
            if (!string.IsNullOrWhiteSpace(parsed.Message))
                return parsed.Message!;
            if (!string.IsNullOrWhiteSpace(responseBody) && responseBody.Length <= 300 && !responseBody.TrimStart().StartsWith('<'))
                return responseBody.Trim();
            return DefaultMessage(statusCode);
        }

        private static Parsed Parse(string? responseBody)
        {
            var none = new Parsed(null, null, null, Array.Empty<string>());
            if (string.IsNullOrWhiteSpace(responseBody)) return none;

            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return none;

                int? number = null;
                if (TryGet(root, "ErrorNumber", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var parsedNumber))
                    number = parsedNumber;
                var type = GetString(root, "Type");

                var validation = new List<string>();
                if (TryGet(root, "Elements", out var elements) && elements.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in elements.EnumerateArray())
                    {
                        if (element.ValueKind != JsonValueKind.Object) continue;
                        if (!TryGet(element, "ValidationErrors", out var errors) || errors.ValueKind != JsonValueKind.Array) continue;
                        foreach (var error in errors.EnumerateArray())
                        {
                            var text = error.ValueKind == JsonValueKind.Object ? GetString(error, "Message") : null;
                            if (!string.IsNullOrWhiteSpace(text) && !validation.Contains(text!)) validation.Add(text!.Trim());
                        }
                    }
                }

                // Accounting API: "Message". Gateway problem details: "Detail", else "Title". OAuth: "error".
                var message = GetString(root, "Message") ?? GetString(root, "Detail") ?? GetString(root, "Title");
                if (string.IsNullOrWhiteSpace(message))
                {
                    var error = GetString(root, "error");
                    var description = GetString(root, "error_description");
                    if (!string.IsNullOrWhiteSpace(error))
                        message = string.IsNullOrWhiteSpace(description) ? error : $"{error}: {description}";
                }

                return new Parsed(number, type, message, validation);
            }
            catch (JsonException)
            {
                return none;
            }
        }

        private static bool TryGet(JsonElement obj, string name, out JsonElement value)
        {
            foreach (var p in obj.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }

        private static string? GetString(JsonElement obj, string name) =>
            TryGet(obj, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static string DefaultMessage(HttpStatusCode statusCode) => statusCode switch
        {
            HttpStatusCode.BadRequest => "Xero could not accept the request.",
            HttpStatusCode.Unauthorized => "Xero did not accept the sign-in. Reconnect Xero and try again.",
            HttpStatusCode.Forbidden => "Xero refused access. The organisation may have been disconnected from Fiscalize.",
            HttpStatusCode.NotFound => "Xero could not find that record.",
            HttpStatusCode.InternalServerError => "Xero had an internal error. Please try again.",
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout
                => "Xero is not available right now. Please try again shortly.",
            _ => $"Xero answered with HTTP {(int)statusCode}."
        };
    }
}
