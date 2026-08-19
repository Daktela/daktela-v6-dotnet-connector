namespace Daktela.Connector.Utils;

/// <summary>
/// Formatting helpers for common Daktela values.
/// </summary>
public static class FormatHelper
{
    /// <summary>
    /// Normalizes a phone number to either <c>+COUNTRY...</c> or <c>00COUNTRY...</c> form.
    /// </summary>
    public static string? GetNormalizedPhoneNumber(
        string? number,
        bool plusSign = false,
        string intlPrefix = "420",
        int intlLength = 12)
    {
        if (number == null)
            return null;
        ArgumentException.ThrowIfNullOrEmpty(intlPrefix);
        if (intlLength < 0)
            throw new ArgumentOutOfRangeException(nameof(intlLength));

        var normalized = number.Replace(" ", string.Empty, StringComparison.Ordinal);
        var prefix = plusSign ? "+" : "00";

        if (normalized.StartsWith(intlPrefix, StringComparison.Ordinal) &&
            normalized.Length >= intlLength)
        {
            normalized = prefix + normalized;
        }

        if (!normalized.StartsWith('+') &&
            !normalized.StartsWith("00", StringComparison.Ordinal))
        {
            normalized = prefix + intlPrefix + normalized;
        }

        if (normalized.StartsWith('+'))
            normalized = prefix + normalized[1..];
        if (normalized.StartsWith("00", StringComparison.Ordinal))
            normalized = prefix + normalized[2..];

        return normalized;
    }
}
