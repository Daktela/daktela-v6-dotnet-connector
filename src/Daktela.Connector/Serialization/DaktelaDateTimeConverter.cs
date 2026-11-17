using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daktela.Connector.Serialization;

internal sealed class DaktelaDateTimeConverter(TimeZoneInfo? serverTimeZone = null) : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected a Daktela date/time string.");

        return Parse(reader.GetString());
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(DaktelaDateTime.ToWire(value, serverTimeZone));

    internal static DateTime Parse(string? value)
    {
        if (DateTime.TryParseExact(value, DaktelaDateTime.Format, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var daktelaDate))
            return DateTime.SpecifyKind(daktelaDate, DateTimeKind.Unspecified);

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var standardDate))
            return standardDate;

        throw new JsonException($"'{value}' is not a supported Daktela date/time value.");
    }
}

/// <summary>
/// Reads Daktela's empty-string and zero-date placeholders as null.
/// </summary>
internal sealed class DaktelaNullableDateTimeConverter(TimeZoneInfo? serverTimeZone = null) : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected a Daktela date/time string.");

        var value = reader.GetString();
        return DaktelaDateTime.IsEmptyValue(value) ? null : DaktelaDateTimeConverter.Parse(value);
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(DaktelaDateTime.ToWire(value.Value, serverTimeZone));
        else
            writer.WriteNullValue();
    }
}
