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
        if (int.TryParse(retryAfterHeader, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        // Try to parse as HTTP date
        if (DateTimeOffset.TryParse(retryAfterHeader, out var date))
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }
}
