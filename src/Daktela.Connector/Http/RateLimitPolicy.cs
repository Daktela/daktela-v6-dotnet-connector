namespace Daktela.Connector.Http;

/// <summary>
/// Controls automatic handling of HTTP 429 rate-limit responses.
/// </summary>
public class RateLimitPolicy
{
    /// <summary>
    /// Whether eligible requests should automatically wait and retry. Default is true.
    /// </summary>
    public bool AutoRetry { get; set; } = true;

    /// <summary>
    /// Maximum number of rate-limit retries. Default is 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Largest server-requested wait that will be honored. Default is 60 seconds.
    /// </summary>
    public TimeSpan MaxWait { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Wait used when Retry-After is absent or invalid. Default is 5 seconds.
    /// </summary>
    public TimeSpan DefaultWait { get; set; } = TimeSpan.FromSeconds(5);

    internal void Validate()
    {
        if (MaxRetries < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxRetries), "Maximum retries must be non-negative.");
        if (MaxWait < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MaxWait), "Maximum wait must be non-negative.");
        if (DefaultWait < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(DefaultWait), "Default wait must be non-negative.");
    }
}
