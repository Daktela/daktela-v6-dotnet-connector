using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daktela.Connector.Serialization;

internal sealed class DaktelaDateTimeConverter : JsonConverter<DateTime>
{
    private const string DaktelaFormat = "yyyy-MM-dd HH:mm:ss";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected a Daktela date/time string.");

        var value = reader.GetString();
        if (DateTime.TryParseExact(value, DaktelaFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var daktelaDate))
            return DateTime.SpecifyKind(daktelaDate, DateTimeKind.Unspecified);

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var standardDate))
            return standardDate;

        throw new JsonException($"'{value}' is not a supported Daktela date/time value.");
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString(DaktelaFormat, CultureInfo.InvariantCulture));
}
