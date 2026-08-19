using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Daktela.Connector.Exceptions;

namespace Daktela.Connector.Http;

/// <summary>
/// Handles HTTP communication with the Daktela API.
/// </summary>
internal sealed class HttpCommunicator : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly DaktelaConfig _config;
    private readonly RetryPolicy? _retryPolicy;
    private readonly Uri _baseUri;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly TimeSpan _timeout;
    private bool _disposed;

    public HttpCommunicator(
        DaktelaConfig config,
        JsonSerializerOptions jsonOptions,
        HttpClient? httpClient = null)
    {
        _config = config;
        _retryPolicy = config.RetryPolicy;
        _retryPolicy?.Validate();
        _baseUri = new Uri(config.GetBaseUrl(), UriKind.Absolute);
        _jsonOptions = jsonOptions;
        _timeout = config.Timeout;

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
            return;
        }

        var handler = new HttpClientHandler();
        if (!config.VerifySsl)
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

        _httpClient = new HttpClient(handler)
        {
            // Apply the same per-request timeout to both internally created and injected clients.
            Timeout = Timeout.InfiniteTimeSpan
        };
        _ownsHttpClient = true;
    }

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string endpoint,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var attempt = 0;
        var canRetryMethod = IsSafeToRetry(method) || _retryPolicy?.RetryUnsafeHttpMethods == true;

        while (true)
        {
            try
            {
                using var request = CreateRequest(method, endpoint, body);
                using var timeoutSource = CreateTimeoutSource(cancellationToken);
                var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutSource?.Token ?? cancellationToken).ConfigureAwait(false);

                if (ShouldRetry(response.StatusCode, attempt, canRetryMethod))
                {
                    var delay = GetRetryDelay(response, attempt);
                    response.Dispose();
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    attempt++;
                    continue;
                }

                return response;
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (canRetryMethod &&
                    _retryPolicy?.RetryOnTimeout == true &&
                    attempt < _retryPolicy.MaxRetries)
                {
                    await Task.Delay(_retryPolicy.GetDelay(attempt), cancellationToken).ConfigureAwait(false);
                    attempt++;
                    continue;
                }

                throw new DaktelaTimeoutException("The request timed out.", ex);
            }
            catch (HttpRequestException ex)
            {
                if (canRetryMethod && _retryPolicy != null && attempt < _retryPolicy.MaxRetries)
                {
                    await Task.Delay(_retryPolicy.GetDelay(attempt), cancellationToken).ConfigureAwait(false);
                    attempt++;
                    continue;
                }

                throw new DaktelaConnectionException($"Connection error: {ex.Message}", ex);
            }
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string endpoint, object? body)
    {
        var request = new HttpRequestMessage(method, BuildUrl(endpoint));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Daktela.Connector/1.0");
        ApplyAuthentication(request);

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, _jsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private Uri BuildUrl(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            throw new ArgumentException("Endpoint must be relative to the configured Daktela instance.", nameof(endpoint));

        var relativeEndpoint = endpoint.TrimStart('/');
        var uri = new Uri(_baseUri, relativeEndpoint);
        if (!_baseUri.IsBaseOf(uri))
            throw new ArgumentException(
                "Endpoint must remain within the configured Daktela API path.",
                nameof(endpoint));

        if (_config.AuthMethod != AuthMethod.QueryParam)
            return uri;

        var builder = new UriBuilder(uri);
        var token = $"accessToken={Uri.EscapeDataString(_config.AccessToken)}";
        builder.Query = string.IsNullOrEmpty(builder.Query)
            ? token
            : builder.Query.TrimStart('?') + "&" + token;
        return builder.Uri;
    }

    private void ApplyAuthentication(HttpRequestMessage request)
    {
        switch (_config.AuthMethod)
        {
            case AuthMethod.Header:
                request.Headers.Add("X-AUTH-TOKEN", _config.AccessToken);
                break;

            case AuthMethod.Cookie:
                request.Headers.Add("Cookie", $"c_user={Uri.EscapeDataString(_config.AccessToken)}");
                break;

            case AuthMethod.QueryParam:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(_config.AuthMethod), _config.AuthMethod,
                    "Unknown authentication method.");
        }
    }

    private bool ShouldRetry(HttpStatusCode statusCode, int attempt, bool canRetryMethod)
        => canRetryMethod &&
           _retryPolicy != null &&
           attempt < _retryPolicy.MaxRetries &&
           _retryPolicy.RetryableStatusCodes.Contains((int)statusCode);

    private TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests &&
            response.Headers.TryGetValues("Retry-After", out var values))
        {
            var retryAfter = RateLimitHandler.ParseRetryAfter(values.FirstOrDefault());
            if (retryAfter.HasValue)
            {
                var nonNegativeDelay = retryAfter.Value < TimeSpan.Zero ? TimeSpan.Zero : retryAfter.Value;
                return _retryPolicy != null && nonNegativeDelay > _retryPolicy.MaxDelay
                    ? _retryPolicy.MaxDelay
                    : nonNegativeDelay;
            }
        }

        return _retryPolicy?.GetDelay(attempt) ?? TimeSpan.Zero;
    }

    private static bool IsSafeToRetry(HttpMethod method)
        => method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options;

    private CancellationTokenSource? CreateTimeoutSource(CancellationToken cancellationToken)
    {
        if (_timeout == Timeout.InfiniteTimeSpan)
            return null;

        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(_timeout);
        return source;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_ownsHttpClient)
            _httpClient.Dispose();
        _disposed = true;
    }
}
