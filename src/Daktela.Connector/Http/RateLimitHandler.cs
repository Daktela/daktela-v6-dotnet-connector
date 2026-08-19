using System.Globalization;

namespace Daktela.Connector.Http;

/// <summary>
/// Handles rate limit information from API responses.
/// </summary>
internal class RateLimitHandler
{
    /// <summary>
    /// Parses the Retry-After header value.
    /// </summary>
    /// <param name="retryAfterHeader">The Retry-After header value.</param>
    /// <returns>The time to wait, or null if the header is invalid.</returns>
    public static TimeSpan? ParseRetryAfter(string? retryAfterHeader)
    {
        if (string.IsNullOrEmpty(retryAfterHeader))
            return null;

        // Try to parse as seconds (integer)
        if (long.TryParse(
                retryAfterHeader,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var seconds))
        {
            if (seconds <= 0)
                return TimeSpan.Zero;
            return seconds >= TimeSpan.MaxValue.TotalSeconds
                ? TimeSpan.MaxValue
                : TimeSpan.FromSeconds(seconds);
        }

        // Try to parse as HTTP date
        if (DateTimeOffset.TryParse(
                retryAfterHeader,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var date))
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }
}
