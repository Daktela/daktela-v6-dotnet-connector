using System.Globalization;

namespace Daktela.Connector.Serialization;

/// <summary>
/// Conversions between .NET date/time values and Daktela's server-local wall-clock format.
/// </summary>
internal static class DaktelaDateTime
{
    public const string Format = "yyyy-MM-dd HH:mm:ss";

    /// <summary>
    /// Daktela sends an empty string or a zero date for a date/time that has no value.
    /// </summary>
    public static bool IsEmptyValue(string? value)
        => string.IsNullOrWhiteSpace(value) ||
           value.AsSpan().TrimStart().StartsWith("0000-00-00", StringComparison.Ordinal);

    public static string ToWire(DateTime value, TimeZoneInfo? serverTimeZone)
    {
        if (serverTimeZone != null && value.Kind != DateTimeKind.Unspecified)
            value = TimeZoneInfo.ConvertTime(value, serverTimeZone);
        return value.ToString(Format, CultureInfo.InvariantCulture);
    }

    public static string ToWire(DateTimeOffset value, TimeZoneInfo? serverTimeZone)
    {
        if (serverTimeZone != null)
            value = TimeZoneInfo.ConvertTime(value, serverTimeZone);
        return value.ToString(Format, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Attaches the server's offset to a wall-clock value that carried no offset.
    /// </summary>
    public static DateTimeOffset FromServerLocal(DateTime wallClock, TimeZoneInfo serverTimeZone)
    {
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, serverTimeZone.GetUtcOffset(unspecified));
    }
}
