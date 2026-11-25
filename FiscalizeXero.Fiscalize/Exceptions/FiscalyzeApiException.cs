using System.Net;
using System.Text.Json;

namespace FiscalizeXero.Fiscalize.Exceptions
{
    /// <summary>
    /// Exception thrown when a Fiscalize API call fails
    /// </summary>
    public class FiscalizeApiException : Exception
    {
        /// <summary>
        /// The HTTP status code returned by the Fiscalize API
        /// </summary>
        public HttpStatusCode StatusCode { get; }

        /// <summary>
        /// The raw response body from the Fiscalize API
        /// </summary>
        public string ResponseBody { get; }

        /// <summary>
        /// Detailed error strings extracted from the response body's errors array
        /// </summary>
        public List<string> Errors { get; }

        public FiscalizeApiException(HttpStatusCode statusCode, string responseBody)
            : base(Safe(ParseMessage(responseBody)))
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
            Errors = ParseErrors(responseBody);
        }

        public FiscalizeApiException(HttpStatusCode statusCode, string responseBody, Exception innerException)
            : base(Safe(ParseMessage(responseBody)), innerException)
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
            Errors = ParseErrors(responseBody);
        }

        private static List<string> ParseErrors(string responseBody)
        {
            var errors = new List<string>();
            if (string.IsNullOrEmpty(responseBody)) return errors;
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                // "errors": ["msg1", "msg2"] or "errors": {"Field": ["msg"]}
                foreach (var key in new[] { "errors", "Errors" })
                {
                    if (!root.TryGetProperty(key, out var errProp)) continue;

                    if (errProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in errProp.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                                errors.Add(item.GetString()!);
                            else if (item.TryGetProperty("message", out var msg))
                                errors.Add(msg.GetString()!);
                            else
                                errors.Add(item.GetRawText());
                        }
                    }
                    else if (errProp.ValueKind == JsonValueKind.Object)
                    {
                        // e.g. ASP.NET validation: {"Field": ["msg1"]}
                        foreach (var prop in errProp.EnumerateObject())
                        {
                            if (prop.Value.ValueKind == JsonValueKind.Array)
                                foreach (var v in prop.Value.EnumerateArray())
                                    if (v.ValueKind == JsonValueKind.String)
                                        errors.Add($"{prop.Name}: {v.GetString()}");
                        }
                    }
                    break;
                }
            }
            catch { }
            return errors;
        }

        /// <summary>
        /// The message is shown on screen, so a raw body that is an HTML error page (a proxy or server crash page) or
        /// something very long is replaced by plain words; the full body stays in <see cref="ResponseBody"/> for the log.
        /// </summary>
        internal static string Safe(string message)
        {
            var text = message.Trim();
            if (text.StartsWith('<') || text.Contains("<html", StringComparison.OrdinalIgnoreCase) || text.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
                return "The Fiscalize platform answered with an error page instead of a result. Try again in a minute.";
            return text.Length > 400 ? text[..400].TrimEnd() + "…" : text;
        }

        /// <summary>
        /// Attempts to parse a user-friendly error message from the response body
        /// </summary>
        private static string ParseMessage(string responseBody)
        {
            if (string.IsNullOrEmpty(responseBody))
            {
                return "An error occurred while communicating with the Fiscalize API";
            }

            try
            {
                // Try to parse as JSON and extract common error message fields
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                // Try common error message field names
                if (root.TryGetProperty("message", out var message))
                    return message.GetString() ?? responseBody;

                if (root.TryGetProperty("Message", out var messageUpper))
                    return messageUpper.GetString() ?? responseBody;

                if (root.TryGetProperty("error", out var error))
                {
                    if (error.ValueKind == JsonValueKind.String)
                        return error.GetString() ?? responseBody;
                    if (error.TryGetProperty("message", out var errorMessage))
                        return errorMessage.GetString() ?? responseBody;
                }

                if (root.TryGetProperty("Error", out var errorUpper))
                {
                    if (errorUpper.ValueKind == JsonValueKind.String)
                        return errorUpper.GetString() ?? responseBody;
                    if (errorUpper.TryGetProperty("Message", out var errorMessageUpper))
                        return errorMessageUpper.GetString() ?? responseBody;
                }

                if (root.TryGetProperty("title", out var title))
                    return title.GetString() ?? responseBody;

                if (root.TryGetProperty("errors", out var errors))
                {
                    if (errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                    {
                        var firstError = errors[0];
                        if (firstError.ValueKind == JsonValueKind.String)
                            return firstError.GetString() ?? responseBody;
                        if (firstError.TryGetProperty("message", out var firstErrorMessage))
                            return firstErrorMessage.GetString() ?? responseBody;
                    }
                }

                // Return the raw body if we can't find a message field
                return responseBody;
            }
            catch
            {
                // If parsing fails, return the raw body
                return responseBody;
            }
        }
    }
}
