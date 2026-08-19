using Daktela.Connector.Http;
using System.Text.Json;

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
    /// Optional policy dedicated to HTTP 429 responses. When omitted, 429 responses use
    /// <see cref="RetryPolicy"/> like any other configured retryable status code.
    /// </summary>
    public RateLimitPolicy? RateLimitPolicy { get; set; }

    /// <summary>
    /// Optional suffix appended to the connector User-Agent, for example <c>MyApp/2.0</c>.
    /// </summary>
    public string? UserAgentSuffix { get; set; }

    /// <summary>
    /// Optional JSON serializer settings. The connector copies these settings and adds
    /// converters for Daktela date/time values.
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    /// <summary>
    /// Gets the base URL for API requests.
    /// </summary>
    internal string GetBaseUrl()
    {
        var url = InstanceUrl.Trim();

        if (Uri.TryCreate(url, UriKind.Absolute, out var suppliedUri) &&
            suppliedUri.Scheme != Uri.UriSchemeHttp &&
            suppliedUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "Instance URL must use the HTTP or HTTPS scheme.",
                nameof(InstanceUrl));
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Instance URL must be a valid HTTP or HTTPS URL.", nameof(InstanceUrl));

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Instance URL cannot contain a query string or fragment.", nameof(InstanceUrl));

        var path = uri.AbsolutePath.TrimEnd('/');
        if (!path.EndsWith("/api/v6", StringComparison.OrdinalIgnoreCase))
            path += "/api/v6";

        var builder = new UriBuilder(uri)
        {
            Path = path + "/",
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.AbsoluteUri;
    }
}
