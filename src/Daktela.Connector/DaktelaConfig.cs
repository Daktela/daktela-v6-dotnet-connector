using Daktela.Connector.Auth;
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
    /// The static API access token for authentication. Set either this or <see cref="Login"/>.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Username and password to start a session with instead of a static
    /// <see cref="AccessToken"/>. The connector logs in on the first request and keeps the
    /// session's short-lived access token fresh.
    /// </summary>
    public DaktelaLogin? Login { get; set; }

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
    /// Time zone of the Daktela server. Daktela exchanges date/time values as server-local
    /// wall-clock time without an offset. When set, <see cref="DateTimeOffset"/> values and
    /// <see cref="DateTime"/> values of kind <see cref="DateTimeKind.Utc"/> or
    /// <see cref="DateTimeKind.Local"/> are converted to this zone when sent, and values read
    /// into a <see cref="DateTimeOffset"/> receive this zone's offset. When null (default),
    /// values are sent as written and offset-less values read into a
    /// <see cref="DateTimeOffset"/> assume the zone of the machine running the connector.
    /// </summary>
    public TimeZoneInfo? ServerTimeZone { get; set; }

    /// <summary>
    /// Whether null properties of a request body are sent as JSON <c>null</c>. Default is
    /// false, which omits them. Enable this to clear a field by sending an explicit null.
    /// </summary>
    public bool IncludeNullValues { get; set; }

    /// <summary>
    /// Maximum number of page requests made by one pagination operation. When the limit is
    /// reached while more records may remain, a
    /// <see cref="Exceptions.DaktelaPaginationLimitException"/> is thrown.
    /// Default is <see cref="DaktelaClient.ReadLimit"/>.
    /// </summary>
    public int MaxPages { get; set; } = DaktelaClient.ReadLimit;

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
