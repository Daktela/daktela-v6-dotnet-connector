using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Daktela.Connector.Exceptions;
using Daktela.Connector.Http;
using Daktela.Connector.Query;

namespace Daktela.Connector;

/// <summary>
/// Client for interacting with the Daktela V6 API.
/// </summary>
public class DaktelaClient : IDisposable
{
    private readonly HttpCommunicator _communicator;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    /// <summary>
    /// Creates a new instance of the Daktela client.
    /// </summary>
    /// <param name="config">The configuration for the client.</param>
    public DaktelaClient(DaktelaConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(config.InstanceUrl))
            throw new ArgumentException("Instance URL is required.", nameof(config));

        if (string.IsNullOrWhiteSpace(config.AccessToken))
            throw new ArgumentException("Access token is required.", nameof(config));

        _communicator = new HttpCommunicator(config);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <summary>
    /// Checks if the API is reachable and the credentials are valid.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the ping was successful.</returns>
    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _communicator.SendAsync(
                HttpMethod.Get,
                "/users.json?take=1",
                cancellationToken: cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (DaktelaException)
        {
            return false;
        }
    }

    /// <summary>
    /// Gets a single record by ID.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the response to.</typeparam>
    /// <param name="endpoint">The API endpoint (e.g., "users", "contacts").</param>
    /// <param name="id">The record ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The API response with the record data.</returns>
    public async Task<DaktelaResponse<T>> GetAsync<T>(
        string endpoint,
        string id,
        CancellationToken cancellationToken = default)
    {
        var url = $"/{NormalizeEndpoint(endpoint)}/{Uri.EscapeDataString(id)}.json";
        var response = await _communicator.SendAsync(HttpMethod.Get, url, cancellationToken: cancellationToken);
        return await ProcessResponseAsync<T>(response, cancellationToken);
    }

    /// <summary>
    /// Gets a list of records with optional query parameters.
    /// </summary>
    /// <typeparam name="T">The type to deserialize each record to.</typeparam>
    /// <param name="endpoint">The API endpoint (e.g., "users", "contacts").</param>
    /// <param name="query">Optional query builder for filtering, sorting, and pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The API response with the list of records.</returns>
    public async Task<DaktelaResponse<List<T>>> GetAsync<T>(
        string endpoint,
        QueryBuilder? query = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"/{NormalizeEndpoint(endpoint)}.json";

        if (query != null)
        {
            var queryString = query.Build();
            if (!string.IsNullOrEmpty(queryString))
            {
                url = $"{url}?{queryString}";
            }
        }

        var response = await _communicator.SendAsync(HttpMethod.Get, url, cancellationToken: cancellationToken);
        return await ProcessListResponseAsync<T>(response, cancellationToken);
    }

    /// <summary>
    /// Creates a new record.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the response to.</typeparam>
    /// <param name="endpoint">The API endpoint (e.g., "users", "contacts").</param>
    /// <param name="data">The data for the new record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The API response with the created record.</returns>
    public async Task<DaktelaResponse<T>> PostAsync<T>(
        string endpoint,
        object data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        var url = $"/{NormalizeEndpoint(endpoint)}.json";
        var response = await _communicator.SendAsync(HttpMethod.Post, url, data, cancellationToken);
        return await ProcessResponseAsync<T>(response, cancellationToken);
    }

    /// <summary>
    /// Updates an existing record.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the response to.</typeparam>
    /// <param name="endpoint">The API endpoint (e.g., "users", "contacts").</param>
    /// <param name="id">The record ID.</param>
    /// <param name="data">The updated data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The API response with the updated record.</returns>
    public async Task<DaktelaResponse<T>> PutAsync<T>(
        string endpoint,
        string id,
        object data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        var url = $"/{NormalizeEndpoint(endpoint)}/{Uri.EscapeDataString(id)}.json";
        var response = await _communicator.SendAsync(HttpMethod.Put, url, data, cancellationToken);
        return await ProcessResponseAsync<T>(response, cancellationToken);
    }

    /// <summary>
    /// Deletes a record.
    /// </summary>
    /// <param name="endpoint">The API endpoint (e.g., "users", "contacts").</param>
    /// <param name="id">The record ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The API response.</returns>
    public async Task<DaktelaResponse> DeleteAsync(
        string endpoint,
        string id,
        CancellationToken cancellationToken = default)
    {
        var url = $"/{NormalizeEndpoint(endpoint)}/{Uri.EscapeDataString(id)}.json";
        var response = await _communicator.SendAsync(HttpMethod.Delete, url, cancellationToken: cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        await ThrowOnErrorAsync(response, body, cancellationToken);

        return new DaktelaResponse((int)response.StatusCode, rawResponse: body);
    }

    /// <summary>
    /// Iterates through all records, handling pagination automatically.
    /// </summary>
    /// <typeparam name="T">The type to deserialize each record to.</typeparam>
    /// <param name="endpoint">The API endpoint (e.g., "users", "contacts").</param>
    /// <param name="query">Optional query builder for filtering and sorting.</param>
    /// <param name="pageSize">The number of records to fetch per page. Default is 100.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of records.</returns>
    public async IAsyncEnumerable<T> IterateAsync<T>(
        string endpoint,
        QueryBuilder? query = null,
        int pageSize = 100,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var currentQuery = (query?.Clone() ?? new QueryBuilder()).Take(pageSize);
        var skip = query?.GetSkip() ?? 0;
        var maxRecords = query?.GetTake();
        var recordsReturned = 0;

        while (true)
        {
            var pageQuery = currentQuery.WithSkip(skip);
            var response = await GetAsync<T>(endpoint, pageQuery, cancellationToken);

            if (!response.IsSuccess || response.Data == null || response.Data.Count == 0)
            {
                yield break;
            }

            foreach (var item in response.Data)
            {
                yield return item;
                recordsReturned++;

                // Check if we've reached the max requested records
                if (maxRecords.HasValue && recordsReturned >= maxRecords.Value)
                {
                    yield break;
                }
            }

            // Check if we've received fewer records than requested (last page)
            if (response.Data.Count < pageSize)
            {
                yield break;
            }

            // Check if we've reached the total (if available)
            if (response.Total.HasValue && skip + response.Data.Count >= response.Total.Value)
            {
                yield break;
            }

            skip += pageSize;
        }
    }

    private async Task<DaktelaResponse<T>> ProcessResponseAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        await ThrowOnErrorAsync(response, body, cancellationToken);

        var data = default(T);
        if (!string.IsNullOrEmpty(body))
        {
            try
            {
                var json = JsonDocument.Parse(body);

                // Check for "result" wrapper
                if (json.RootElement.TryGetProperty("result", out var resultElement))
                {
                    data = resultElement.Deserialize<T>(_jsonOptions);
                }
                else
                {
                    data = json.RootElement.Deserialize<T>(_jsonOptions);
                }
            }
            catch (JsonException)
            {
                // If JSON parsing fails, return default
            }
        }

        return new DaktelaResponse<T>((int)response.StatusCode, data, rawResponse: body);
    }

    private async Task<DaktelaResponse<List<T>>> ProcessListResponseAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        await ThrowOnErrorAsync(response, body, cancellationToken);

        var data = new List<T>();
        int? total = null;

        if (!string.IsNullOrEmpty(body))
        {
            try
            {
                var json = JsonDocument.Parse(body);

                // Get total if present
                if (json.RootElement.TryGetProperty("total", out var totalElement) &&
                    totalElement.TryGetInt32(out var totalValue))
                {
                    total = totalValue;
                }

                // Get result array
                if (json.RootElement.TryGetProperty("result", out var resultElement) &&
                    resultElement.ValueKind == JsonValueKind.Array)
                {
                    data = resultElement.Deserialize<List<T>>(_jsonOptions) ?? new List<T>();
                }
                else if (json.RootElement.ValueKind == JsonValueKind.Array)
                {
                    data = json.RootElement.Deserialize<List<T>>(_jsonOptions) ?? new List<T>();
                }
            }
            catch (JsonException)
            {
                // If JSON parsing fails, return empty list
            }
        }

        return new DaktelaResponse<List<T>>((int)response.StatusCode, data, total, rawResponse: body);
    }

    private Task ThrowOnErrorAsync(
        HttpResponseMessage response,
        string body,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return Task.CompletedTask;

        var statusCode = (int)response.StatusCode;

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new DaktelaUnauthorizedException(
                "Authentication failed. Check your access token.", body),

            HttpStatusCode.NotFound => new DaktelaNotFoundException(
                "The requested resource was not found.", body),

            HttpStatusCode.TooManyRequests => CreateRateLimitException(response, body),

            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => new DaktelaValidationException(
                "Validation failed.", body, statusCode),

            _ => new DaktelaException($"API request failed with status {statusCode}.", statusCode, body)
        };
    }

    private static DaktelaRateLimitException CreateRateLimitException(HttpResponseMessage response, string body)
    {
        TimeSpan? retryAfter = null;

        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            retryAfter = RateLimitHandler.ParseRetryAfter(values.FirstOrDefault());
        }

        return new DaktelaRateLimitException(
            "Rate limit exceeded. Please wait before retrying.",
            retryAfter,
            body);
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        var normalized = endpoint.Trim('/');
        if (normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^5];
        }
        return normalized;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _communicator.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
