namespace Daktela.Connector.Http;

/// <summary>
/// Configuration for retry behavior with exponential backoff.
/// </summary>
public class RetryPolicy
{
    /// <summary>
    /// Maximum number of retry attempts. Default is 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Initial delay between retries. Default is 1 second.
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum delay between retries. Default is 30 seconds.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Multiplier for exponential backoff. Default is 2.0.
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// HTTP status codes that should trigger a retry.
    /// </summary>
    public HashSet<int> RetryableStatusCodes { get; set; } = new()
    {
        408, // Request Timeout
        429, // Too Many Requests
        500, // Internal Server Error
        502, // Bad Gateway
        503, // Service Unavailable
        504  // Gateway Timeout
    };

    /// <summary>
    /// Creates a default retry policy.
    /// </summary>
    public static RetryPolicy Default => new();

    /// <summary>
    /// Creates a retry policy with no retries.
    /// </summary>
    public static RetryPolicy NoRetry => new() { MaxRetries = 0 };

    /// <summary>
    /// Calculates the delay for a given retry attempt using exponential backoff.
    /// </summary>
    /// <param name="attempt">The retry attempt number (0-based).</param>
    /// <returns>The delay to wait before the next attempt.</returns>
    public TimeSpan GetDelay(int attempt)
    {
        var delay = TimeSpan.FromMilliseconds(
            InitialDelay.TotalMilliseconds * Math.Pow(BackoffMultiplier, attempt));

        return delay > MaxDelay ? MaxDelay : delay;
    }
}
