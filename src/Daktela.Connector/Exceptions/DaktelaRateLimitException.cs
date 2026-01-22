namespace Daktela.Connector.Exceptions;

/// <summary>
/// Exception thrown when the rate limit is exceeded (HTTP 429).
/// </summary>
public class DaktelaRateLimitException : DaktelaException
{
    /// <summary>
    /// The time to wait before retrying, if provided by the server.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    public DaktelaRateLimitException(string message = "Rate limit exceeded. Please wait before retrying.")
        : base(message, 429)
    {
    }

    public DaktelaRateLimitException(string message, TimeSpan? retryAfter, string? responseBody = null)
        : base(message, 429, responseBody)
    {
        RetryAfter = retryAfter;
    }
}
