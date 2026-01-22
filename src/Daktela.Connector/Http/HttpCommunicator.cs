using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Daktela.Connector.Exceptions;

namespace Daktela.Connector.Http;

/// <summary>
/// Handles HTTP communication with the Daktela API.
/// </summary>
internal class HttpCommunicator : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly DaktelaConfig _config;
    private readonly RetryPolicy? _retryPolicy;
    private bool _disposed;

    public HttpCommunicator(DaktelaConfig config)
    {
        _config = config;
        _retryPolicy = config.RetryPolicy;

        var handler = new HttpClientHandler();

        if (!config.VerifySsl)
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(config.GetBaseUrl()),
            Timeout = config.Timeout
        };

        // Set default headers
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string endpoint,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        var attempt = 0;
        var maxAttempts = (_retryPolicy?.MaxRetries ?? 0) + 1;

        while (true)
        {
            try
            {
                var request = CreateRequest(method, endpoint, body);
                var response = await _httpClient.SendAsync(request, cancellationToken);

                // Check if we should retry
                if (!response.IsSuccessStatusCode &&
                    _retryPolicy != null &&
                    attempt < _retryPolicy.MaxRetries &&
                    _retryPolicy.RetryableStatusCodes.Contains((int)response.StatusCode))
                {
                    var delay = GetRetryDelay(response, attempt);
                    await Task.Delay(delay, cancellationToken);
                    attempt++;
                    continue;
                }

                return response;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new DaktelaTimeoutException("The request timed out.", ex);
            }
            catch (HttpRequestException ex)
            {
                if (_retryPolicy != null && attempt < _retryPolicy.MaxRetries)
                {
                    var delay = _retryPolicy.GetDelay(attempt);
                    await Task.Delay(delay, cancellationToken);
                    attempt++;
                    continue;
                }

                throw new DaktelaConnectionException($"Connection error: {ex.Message}", ex);
            }
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string endpoint, object? body)
    {
        var url = BuildUrl(endpoint);
        var request = new HttpRequestMessage(method, url);

        // Apply authentication
        ApplyAuthentication(request, url);

        // Add body if present
        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private string BuildUrl(string endpoint)
    {
        var url = endpoint.TrimStart('/');

        // Add access token as query parameter if that auth method is selected
        if (_config.AuthMethod == AuthMethod.QueryParam)
        {
            var separator = url.Contains('?') ? "&" : "?";
            url = $"{url}{separator}accessToken={Uri.EscapeDataString(_config.AccessToken)}";
        }

        return url;
    }

    private void ApplyAuthentication(HttpRequestMessage request, string url)
    {
        switch (_config.AuthMethod)
        {
            case AuthMethod.Header:
                request.Headers.Add("X-AUTH-TOKEN", _config.AccessToken);
                break;

            case AuthMethod.Cookie:
                request.Headers.Add("Cookie", $"c_user={_config.AccessToken}");
                break;

            case AuthMethod.QueryParam:
                // Already handled in BuildUrl
                break;
        }
    }

    private TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        // Check for Retry-After header (especially for 429)
        if (response.StatusCode == HttpStatusCode.TooManyRequests &&
            response.Headers.TryGetValues("Retry-After", out var values))
        {
            var retryAfter = RateLimitHandler.ParseRetryAfter(values.FirstOrDefault());
            if (retryAfter.HasValue)
            {
                return retryAfter.Value;
            }
        }

        return _retryPolicy?.GetDelay(attempt) ?? TimeSpan.FromSeconds(1);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _disposed = true;
        }
    }
}
