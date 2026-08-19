namespace Daktela.Connector;

/// <summary>
/// Detailed result of an authenticated Daktela API health check.
/// </summary>
public sealed class DaktelaHealthCheck
{
    /// <summary>
    /// Whether the API returned a successful response.
    /// </summary>
    public bool Healthy { get; init; }

    /// <summary>
    /// End-to-end latency of the health-check request.
    /// </summary>
    public TimeSpan Latency { get; init; }

    /// <summary>
    /// HTTP status code, when the server returned a response.
    /// </summary>
    public int? StatusCode { get; init; }

    /// <summary>
    /// Failure description, when the request could not be completed.
    /// </summary>
    public string? Error { get; init; }
}
