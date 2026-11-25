using System.Text.Json;
using System.Text.Json.Serialization;

namespace FiscalizeXero.Shared.Converters
{
    /// <summary>
    /// Handles JSON fields that may arrive as either a number or a string.
    /// Converts numbers to their string representation; nulls stay null.
    /// </summary>
    public class NumberToStringConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => reader.TryGetInt64(out var l) ? l.ToString() : reader.GetDouble().ToString(),
                JsonTokenType.Null   => null,
                JsonTokenType.True   => "true",
                JsonTokenType.False  => "false",
                _                    => reader.GetString()
            };
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (value is null) writer.WriteNullValue();
            else writer.WriteStringValue(value);
        }
    }
}
