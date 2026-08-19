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
    /// Whether POST, PUT, PATCH, and other potentially non-idempotent methods may be retried.
    /// Disabled by default to avoid duplicate operations.
    /// </summary>
    public bool RetryUnsafeHttpMethods { get; set; }

    /// <summary>
    /// Whether timed-out idempotent requests should be retried. Default is true.
    /// </summary>
    public bool RetryOnTimeout { get; set; } = true;

    /// <summary>
    /// Whether connection failures should be retried. Default is true.
    /// </summary>
    public bool RetryOnConnectionError { get; set; } = true;

    /// <summary>
    /// Creates a default retry policy.
    /// </summary>
    public static RetryPolicy Default => new();

    /// <summary>
    /// Creates a retry policy with no retries.
    /// </summary>
    public static RetryPolicy NoRetry => new() { MaxRetries = 0 };

    /// <summary>
    /// Creates an aggressive high-resilience policy.
    /// </summary>
    public static RetryPolicy Aggressive => new()
    {
        MaxRetries = 5,
        InitialDelay = TimeSpan.FromMilliseconds(50),
        MaxDelay = TimeSpan.FromSeconds(30),
        BackoffMultiplier = 2.5
    };

    /// <summary>
    /// Calculates the delay for a given retry attempt using exponential backoff.
    /// </summary>
    /// <param name="attempt">The retry attempt number (0-based).</param>
    /// <returns>The delay to wait before the next attempt.</returns>
    public TimeSpan GetDelay(int attempt)
    {
        Validate();
        if (attempt < 0)
            throw new ArgumentOutOfRangeException(nameof(attempt));

        if (InitialDelay == TimeSpan.Zero)
            return TimeSpan.Zero;

        var milliseconds = InitialDelay.TotalMilliseconds * Math.Pow(BackoffMultiplier, attempt);
        if (double.IsInfinity(milliseconds) || milliseconds >= MaxDelay.TotalMilliseconds)
            return MaxDelay;

        return TimeSpan.FromMilliseconds(milliseconds);
    }

    internal void Validate()
    {
        if (MaxRetries < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxRetries), "Max retries must be non-negative.");
        if (InitialDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(InitialDelay), "Initial delay must be non-negative.");
        if (MaxDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MaxDelay), "Maximum delay must be non-negative.");
        if (BackoffMultiplier < 1 || double.IsNaN(BackoffMultiplier) || double.IsInfinity(BackoffMultiplier))
            throw new ArgumentOutOfRangeException(nameof(BackoffMultiplier), "Backoff multiplier must be a finite value of at least 1.");
        if (RetryableStatusCodes == null)
            throw new ArgumentNullException(nameof(RetryableStatusCodes));
    }
}
