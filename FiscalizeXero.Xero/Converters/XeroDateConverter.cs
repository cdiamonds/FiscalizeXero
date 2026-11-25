using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FiscalizeXero.Xero.Converters
{
    /// <summary>
    /// Xero's JSON writes timestamps in the old Microsoft form, <c>"/Date(1439434356790+0000)/"</c> (milliseconds
    /// since 1970 UTC, optionally with an offset that does not change the instant), and accepts ISO dates on input.
    /// This reads either form to a UTC <see cref="DateTime"/> and writes ISO 8601.
    /// </summary>
    public sealed partial class XeroDateConverter : JsonConverter<DateTime?>
    {
        [GeneratedRegex(@"^/Date\((-?\d+)([+-]\d{4})?\)/$")]
        private static partial Regex MsDate();

        public static DateTime? Parse(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var match = MsDate().Match(value.Trim());
            if (match.Success && long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
            {
                try { return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime; }
                catch (ArgumentOutOfRangeException) { return null; }
            }

            return DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed
                : null;
        }

        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String ? Parse(reader.GetString()) : null;

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value.HasValue) writer.WriteStringValue(value.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
            else writer.WriteNullValue();
        }
    }
}
