using Daktela.Connector.Http;

namespace Daktela.Connector;

/// <summary>
/// Configuration options for the Daktela API client.
/// </summary>
public class DaktelaConfig
{
    /// <summary>
    /// The Daktela instance URL (e.g., "my.daktela.com" or "https://my.daktela.com").
    /// </summary>
    public required string InstanceUrl { get; set; }

    /// <summary>
    /// The API access token for authentication.
    /// </summary>
    public required string AccessToken { get; set; }

    /// <summary>
    /// The authentication method to use. Default is Header.
    /// </summary>
    public AuthMethod AuthMethod { get; set; } = AuthMethod.Header;

    /// <summary>
    /// Request timeout. Default is 30 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether to verify SSL certificates. Default is true.
    /// </summary>
    public bool VerifySsl { get; set; } = true;

    /// <summary>
    /// Retry policy for failed requests. Default is null (no retries).
    /// </summary>
    public RetryPolicy? RetryPolicy { get; set; }

    /// <summary>
    /// Gets the base URL for API requests.
    /// </summary>
    internal string GetBaseUrl()
    {
        var url = InstanceUrl.Trim();

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        return url.TrimEnd('/') + "/api/v6";
    }
}
