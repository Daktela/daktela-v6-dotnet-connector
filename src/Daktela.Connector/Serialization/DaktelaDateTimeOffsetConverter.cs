using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daktela.Connector.Serialization;

internal sealed class DaktelaDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    private static readonly string[] Formats =
    {
        "yyyy-MM-dd HH:mm:ss zzz",
        "yyyy-MM-dd HH:mm:ss"
    };

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected a Daktela date/time string.");

        var value = reader.GetString();
        if (DateTimeOffset.TryParseExact(value, Formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var daktelaDate))
            return daktelaDate;

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var standardDate))
            return standardDate;

        throw new JsonException($"'{value}' is not a supported Daktela date/time value.");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
}
