using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Daktela.Connector.Auth;
using Daktela.Connector.Exceptions;

namespace Daktela.Connector.Http;

/// <summary>
/// Handles HTTP communication with the Daktela API.
/// </summary>
internal sealed class HttpCommunicator : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly AuthMethod _authMethod;
    private readonly string _accessToken;
    private readonly string _userAgent;
    private readonly RetryPolicy? _retryPolicy;
    private readonly RateLimitPolicy? _rateLimitPolicy;
    private readonly Uri _baseUri;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly TimeSpan _timeout;
    private readonly LoginSession? _session;
    private bool _disposed;

    public HttpCommunicator(
        DaktelaConfig config,
        JsonSerializerOptions jsonOptions,
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null)
    {
        // Settings are captured once so a client behaves consistently for its lifetime.
        _authMethod = config.AuthMethod;
        _accessToken = config.AccessToken;
        _userAgent = BuildUserAgent(config.UserAgentSuffix);
        _retryPolicy = config.RetryPolicy;
        _retryPolicy?.Validate();
        _rateLimitPolicy = config.RateLimitPolicy;
        _rateLimitPolicy?.Validate();
        _baseUri = new Uri(config.GetBaseUrl(), UriKind.Absolute);
        _jsonOptions = jsonOptions;
        _timeout = config.Timeout;
        if (config.Login != null)
        {
            _session = new LoginSession(
                config.Login,
                _baseUri.AbsoluteUri,
                (endpoint, json, cancellationToken) => DispatchAsync(
                    HttpMethod.Post, endpoint, new RawJson(json), authenticate: false, cancellationToken),
                timeProvider ?? TimeProvider.System);
        }
        Redactor = new SecretRedactor(GetSecrets);
        ValidateRequestHeaders();

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
            return;
        }

        var handler = new SocketsHttpHandler
        {
            // Recycle pooled connections so a long-lived client observes DNS changes.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            // The API is token-authenticated; do not carry server cookies between requests.
            UseCookies = false,
            // A redirect would forward the access token, and the payload of a login, to its
            // target. A 3xx response is reported as an error instead.
            AllowAutoRedirect = false
        };
        if (!config.VerifySsl)
            handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;

        _httpClient = new HttpClient(handler)
        {
            // Apply the same per-request timeout to both internally created and injected clients.
            Timeout = Timeout.InfiniteTimeSpan
        };
        _ownsHttpClient = true;
    }

    /// <summary>
    /// Removes this client's access token, and the tokens of its login session, from text.
    /// </summary>
    public SecretRedactor Redactor { get; }

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string endpoint,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_session == null)
            return await DispatchAsync(method, endpoint, body, authenticate: true, cancellationToken)
                .ConfigureAwait(false);

        await _session.PrepareAsync(cancellationToken).ConfigureAwait(false);
        var usedAccessToken = _session.AccessToken;
        var response = await DispatchAsync(method, endpoint, body, authenticate: true, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        // The server answers 401 before it processes a request, for example when it has ended
        // the session, so the request is sent once more with a renewed access token.
        response.Dispose();
        await _session.RenewRejectedAsync(usedAccessToken, cancellationToken).ConfigureAwait(false);
        return await DispatchAsync(method, endpoint, body, authenticate: true, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> DispatchAsync(
        HttpMethod method,
        string endpoint,
        object? body,
        bool authenticate,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        var canRetryMethod = IsSafeToRetry(method) || _retryPolicy?.RetryUnsafeHttpMethods == true;

        while (true)
        {
            HttpResponseMessage? response = null;
            try
            {
                using var request = CreateRequest(method, endpoint, body, authenticate);
                using var timeoutSource = CreateTimeoutSource(cancellationToken);
                var requestToken = timeoutSource?.Token ?? cancellationToken;
                response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    requestToken).ConfigureAwait(false);

                if (ShouldRetry(response, attempt, canRetryMethod))
                {
                    var delay = GetRetryDelay(response, attempt);
                    response.Dispose();
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    attempt++;
                    continue;
                }

                ThrowIfRedirectedToAnotherOrigin(request.RequestUri!, response);

                // Read the body inside the timeout and error handling so a stalled or broken
                // body is reported and retried exactly like a stalled or broken request.
                await BufferContentAsync(response, requestToken).ConfigureAwait(false);
                return response;
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                response?.Dispose();
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
            catch (OperationCanceledException)
            {
                response?.Dispose();
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                response?.Dispose();
                if (canRetryMethod &&
                    _retryPolicy?.RetryOnConnectionError == true &&
                    attempt < _retryPolicy.MaxRetries)
                {
                    await Task.Delay(_retryPolicy.GetDelay(attempt), cancellationToken).ConfigureAwait(false);
                    attempt++;
                    continue;
                }

                // An HTTP handler may quote the request URL, which carries the token with query
                // parameter authentication.
                var message = $"Connection error: {Redactor.Redact(ex.Message)}";
                throw Redactor.SafeInner(ex) is { } inner
                    ? new DaktelaConnectionException(message, inner)
                    : new DaktelaConnectionException(message);
            }
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string endpoint, object? body, bool authenticate)
    {
        var request = new HttpRequestMessage(method, BuildUrl(endpoint, authenticate));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd(_userAgent);
        if (authenticate)
            ApplyAuthentication(request);

        if (body != null)
        {
            var json = body is RawJson raw ? raw.Json : JsonSerializer.Serialize(body, _jsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private Uri BuildUrl(string endpoint, bool authenticate)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));
        if (IsAbsoluteUrl(endpoint))
            throw new ArgumentException("Endpoint must be relative to the configured Daktela instance.", nameof(endpoint));

        var relativeEndpoint = endpoint.TrimStart('/');
        var uri = new Uri(_baseUri, relativeEndpoint);
        if (!_baseUri.IsBaseOf(uri))
            throw new ArgumentException(
                "Endpoint must remain within the configured Daktela API path.",
                nameof(endpoint));

        if (!authenticate || _authMethod != AuthMethod.QueryParam)
            return uri;

        var builder = new UriBuilder(uri);
        var token = $"accessToken={Uri.EscapeDataString(CurrentAccessToken)}";
        builder.Query = string.IsNullOrEmpty(builder.Query)
            ? token
            : builder.Query.TrimStart('?') + "&" + token;
        return builder.Uri;
    }

    /// <summary>
    /// The login session's access token, or the static one.
    /// </summary>
    private string CurrentAccessToken => _session == null ? _accessToken : _session.AccessToken ?? string.Empty;

    private IEnumerable<string> GetSecrets()
        => _session == null ? [_accessToken] : _session.GetSecrets();

    /// <summary>
    /// A login or refresh payload that is already serialized, independent of the caller's
    /// JSON naming options.
    /// </summary>
    private sealed record RawJson(string Json);

    private void ApplyAuthentication(HttpRequestMessage request)
    {
        switch (_authMethod)
        {
            case AuthMethod.Header:
                request.Headers.Add("X-AUTH-TOKEN", CurrentAccessToken);
                break;

            case AuthMethod.Cookie:
                request.Headers.Add("Cookie", $"c_user={Uri.EscapeDataString(CurrentAccessToken)}");
                break;

            case AuthMethod.QueryParam:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(_authMethod), _authMethod,
                    "Unknown authentication method.");
        }
    }

    private bool ShouldRetry(HttpResponseMessage response, int attempt, bool canRetryMethod)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests && _rateLimitPolicy != null)
        {
            if (!_rateLimitPolicy.AutoRetry || attempt >= _rateLimitPolicy.MaxRetries)
                return false;

            var delay = ReadRateLimitDelay(response) ?? _rateLimitPolicy.DefaultWait;
            return delay <= _rateLimitPolicy.MaxWait;
        }

        return canRetryMethod &&
               _retryPolicy != null &&
               attempt < _retryPolicy.MaxRetries &&
               _retryPolicy.RetryableStatusCodes.Contains((int)response.StatusCode);
    }

    private TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = ReadRateLimitDelay(response);
            if (retryAfter.HasValue)
            {
                var nonNegativeDelay = retryAfter.Value < TimeSpan.Zero ? TimeSpan.Zero : retryAfter.Value;
                if (_rateLimitPolicy != null)
                    return nonNegativeDelay;
                return _retryPolicy != null && nonNegativeDelay > _retryPolicy.MaxDelay
                    ? _retryPolicy.MaxDelay
                    : nonNegativeDelay;
            }

            if (_rateLimitPolicy != null)
                return _rateLimitPolicy.DefaultWait;
        }

        return _retryPolicy?.GetDelay(attempt) ?? TimeSpan.Zero;
    }

    private static bool IsSafeToRetry(HttpMethod method)
        => method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options;

    private static TimeSpan? ReadRateLimitDelay(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Retry-After", out var values))
            return null;
        return RateLimitHandler.ParseRetryAfter(values.FirstOrDefault());
    }

    /// <summary>
    /// Whether a value names its own scheme or host. A rooted path such as <c>/users</c> is
    /// relative here, although Unix parses it as an absolute <c>file://</c> URI.
    /// </summary>
    internal static bool IsAbsoluteUrl(string value)
        => !value.StartsWith('/') && Uri.TryCreate(value, UriKind.Absolute, out _);

    /// <summary>
    /// An injected <see cref="HttpClient"/> may follow redirects. When one led to another
    /// origin, the access token or login payload went with it, so the response is not used.
    /// </summary>
    private void ThrowIfRedirectedToAnotherOrigin(Uri requested, HttpResponseMessage response)
    {
        var final = response.RequestMessage?.RequestUri;
        if (final == null ||
            !final.IsAbsoluteUri ||
            Uri.Compare(requested, final, UriComponents.SchemeAndServer, UriFormat.Unescaped,
                StringComparison.OrdinalIgnoreCase) == 0)
        {
            return;
        }

        var status = (int)response.StatusCode;
        response.Dispose();
        throw new DaktelaException(
            $"The request was redirected to another origin ({final.GetLeftPart(UriPartial.Authority)}), " +
            "which received the request's credentials. Disable automatic redirects on the supplied " +
            "HttpClient and set InstanceUrl to the final URL of the Daktela instance.",
            status);
    }

    private static Task BufferContentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
#if NET9_0_OR_GREATER
        return response.Content.LoadIntoBufferAsync(cancellationToken);
#else
        // Reading the content buffers it, and unlike LoadIntoBufferAsync this overload
        // accepts a cancellation token on .NET 8.
        return response.Content.ReadAsByteArrayAsync(cancellationToken);
#endif
    }

    /// <summary>
    /// Fails at construction, rather than on every request, when the configured token or
    /// User-Agent suffix cannot be written as an HTTP header.
    /// </summary>
    private void ValidateRequestHeaders()
    {
        using var probe = new HttpRequestMessage();
        try
        {
            probe.Headers.UserAgent.ParseAdd(_userAgent);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException(
                "User-Agent suffix must be a valid User-Agent value of ASCII product tokens " +
                "such as 'MyApp/2.0', optionally with parenthesized comments.",
                "config",
                ex);
        }

        if (_session != null)
            return;

        try
        {
            ApplyAuthentication(probe);
        }
        catch (FormatException)
        {
            // The framework's message quotes the rejected value, so it is not chained here.
            throw new ArgumentException(
                "Access token contains characters that cannot be sent in an HTTP header.",
                "config");
        }
    }

    private static string BuildUserAgent(string? suffix)
    {
        var assemblyVersion = typeof(HttpCommunicator).Assembly.GetName().Version;
        var version = assemblyVersion == null || assemblyVersion.Build < 0
            ? "unknown"
            : $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
        var userAgent = $"Daktela.Connector/{version}";
        return string.IsNullOrWhiteSpace(suffix)
            ? userAgent
            : $"{userAgent} {suffix.Trim()}";
    }

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
        _session?.Dispose();
        _disposed = true;
    }
}
