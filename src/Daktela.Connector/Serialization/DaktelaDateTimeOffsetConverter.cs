using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daktela.Connector.Serialization;

internal sealed class DaktelaDateTimeOffsetConverter(TimeZoneInfo? serverTimeZone = null) : JsonConverter<DateTimeOffset>
{
    private const string FormatWithOffset = DaktelaDateTime.Format + " zzz";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected a Daktela date/time string.");

        return Parse(reader.GetString(), serverTimeZone);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        => writer.WriteStringValue(DaktelaDateTime.ToWire(value, serverTimeZone));

    internal static DateTimeOffset Parse(string? value, TimeZoneInfo? serverTimeZone)
    {
        if (DateTimeOffset.TryParseExact(value, FormatWithOffset, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var withOffset))
            return withOffset;

        // A value without an offset is the Daktela server's wall-clock time. Without a
        // configured server time zone the only available assumption is this machine's zone.
        if (DateTime.TryParseExact(value, DaktelaDateTime.Format, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var wallClock))
            return DaktelaDateTime.FromServerLocal(wallClock, serverTimeZone ?? TimeZoneInfo.Local);

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var standardDate))
            return standardDate;

        throw new JsonException($"'{value}' is not a supported Daktela date/time value.");
    }
}

/// <summary>
/// Reads Daktela's empty-string and zero-date placeholders as null.
/// </summary>
internal sealed class DaktelaNullableDateTimeOffsetConverter(TimeZoneInfo? serverTimeZone = null) : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected a Daktela date/time string.");

        var value = reader.GetString();
        return DaktelaDateTime.IsEmptyValue(value)
            ? null
            : DaktelaDateTimeOffsetConverter.Parse(value, serverTimeZone);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(DaktelaDateTime.ToWire(value.Value, serverTimeZone));
        else
            writer.WriteNullValue();
    }
}
