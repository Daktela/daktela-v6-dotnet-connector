using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Daktela.Connector.Exceptions;
using Daktela.Connector.Http;
using Daktela.Connector.Query;
using Daktela.Connector.Serialization;

namespace Daktela.Connector;

/// <summary>
/// Client for interacting with the Daktela V6 API.
/// </summary>
public class DaktelaClient : IDaktelaClient
{
    /// <summary>
    /// Default for <see cref="DaktelaConfig.MaxPages"/>: the number of page requests one
    /// pagination operation may make before it fails rather than run without bound.
    /// </summary>
    public const int ReadLimit = 999;

    private readonly HttpCommunicator _communicator;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly TimeZoneInfo? _serverTimeZone;
    private bool _disposed;

    /// <summary>
    /// Creates a client that owns its underlying HTTP transport.
    /// </summary>
    public DaktelaClient(DaktelaConfig config)
        : this(CreateCommunicator(config, null))
    {
    }

    /// <summary>
    /// Creates a client using a caller-owned <see cref="HttpClient"/>. The supplied client
    /// is not disposed with this instance, making this overload suitable for HttpClientFactory.
    /// </summary>
    public DaktelaClient(DaktelaConfig config, HttpClient httpClient)
        : this(CreateCommunicator(
            config,
            httpClient ?? throw new ArgumentNullException(nameof(httpClient))))
    {
    }

    /// <summary>
    /// Creates a client whose login session reads the time from <paramref name="timeProvider"/>.
    /// </summary>
    internal DaktelaClient(DaktelaConfig config, HttpClient httpClient, TimeProvider timeProvider)
        : this(CreateCommunicator(config, httpClient, timeProvider))
    {
    }

    private DaktelaClient(ClientDependencies dependencies)
    {
        _jsonOptions = dependencies.JsonOptions;
        _communicator = dependencies.Communicator;
        _serverTimeZone = dependencies.ServerTimeZone;
        MaxPages = dependencies.MaxPages;
    }

    internal int MaxPages { get; }

    /// <summary>
    /// Checks the authenticated <c>whoim</c> endpoint.
    /// </summary>
    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _communicator.SendAsync(
                HttpMethod.Get,
                "whoim.json",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (DaktelaException)
        {
            return false;
        }
    }

    /// <summary>
    /// Checks the authenticated <c>whoim</c> endpoint and returns status and latency details.
    /// Caller cancellation is propagated.
    /// </summary>
    public async Task<DaktelaHealthCheck> HealthCheckAsync(
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _communicator.SendAsync(
                HttpMethod.Get,
                "whoim.json",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            return new DaktelaHealthCheck
            {
                Healthy = response.IsSuccessStatusCode,
                Latency = stopwatch.Elapsed,
                StatusCode = (int)response.StatusCode,
                Error = response.IsSuccessStatusCode
                    ? null
                    : $"Daktela returned HTTP {(int)response.StatusCode}."
            };
        }
        catch (DaktelaException ex)
        {
            stopwatch.Stop();
            return new DaktelaHealthCheck
            {
                Healthy = false,
                Latency = stopwatch.Elapsed,
                StatusCode = ex.StatusCode,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Gets a single record by ID.
    /// </summary>
    public Task<DaktelaResponse<T>> GetAsync<T>(
        string endpoint,
        string id,
        CancellationToken cancellationToken = default)
        => GetAsync<T>(endpoint, id, null, cancellationToken);

    /// <summary>
    /// Gets a single record by ID with optional query parameters.
    /// </summary>
    public async Task<DaktelaResponse<T>> GetAsync<T>(
        string endpoint,
        string id,
        QueryBuilder? query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Record ID cannot be null or empty.", nameof(id));

        var url = AppendQuery(
            $"{NormalizeEndpoint(endpoint)}/{Uri.EscapeDataString(id)}.json",
            query);
        using var response = await _communicator.SendAsync(
            HttpMethod.Get,
            url,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return await ProcessResponseAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a list of records with optional query parameters.
    /// </summary>
    public async Task<DaktelaResponse<List<T>>> GetAsync<T>(
        string endpoint,
        QueryBuilder? query = null,
        CancellationToken cancellationToken = default)
    {
        var url = AppendQuery($"{NormalizeEndpoint(endpoint)}.json", query);
        using var response = await _communicator.SendAsync(
            HttpMethod.Get,
            url,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return await ProcessListResponseAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets untyped data from an endpoint. Arrays and objects are returned as a detached
    /// <see cref="JsonElement"/> in <see cref="DaktelaResponse{T}.Data"/>.
    /// </summary>
    public Task<DaktelaResponse<JsonElement>> GetAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
        => GetAsync(endpoint, null, cancellationToken);

    /// <summary>
    /// Gets untyped data using arbitrary Daktela query parameters.
    /// </summary>
    public async Task<DaktelaResponse<JsonElement>> GetAsync(
        string endpoint,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken = default)
    {
        QueryBuilder? query = null;
        if (parameters != null)
            query = new QueryBuilder().Parameters(parameters);

        var url = AppendQuery($"{NormalizeEndpoint(endpoint)}.json", query);
        using var response = await _communicator.SendAsync(
            HttpMethod.Get,
            url,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return await ProcessRawResponseAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a new record.
    /// </summary>
    public Task<DaktelaResponse<JsonElement>> PostAsync(
        string endpoint,
        object data,
        CancellationToken cancellationToken = default)
        => PostAsync(endpoint, data, null, cancellationToken);

    /// <summary>
    /// Creates a new record without requiring a response DTO.
    /// </summary>
    public async Task<DaktelaResponse<JsonElement>> PostAsync(
        string endpoint,
        object data,
        QueryBuilder? query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var url = AppendQuery($"{NormalizeEndpoint(endpoint)}.json", query);
        using var response = await _communicator.SendAsync(
            HttpMethod.Post,
            url,
            data,
            cancellationToken).ConfigureAwait(false);
        return await ProcessRawResponseAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a new record and deserializes its response.
    /// </summary>
    public Task<DaktelaResponse<T>> PostAsync<T>(
        string endpoint,
        object data,
        CancellationToken cancellationToken = default)
        => PostAsync<T>(endpoint, data, null, cancellationToken);

    /// <summary>
    /// Creates a new record with optional query parameters.
    /// </summary>
    public async Task<DaktelaResponse<T>> PostAsync<T>(
        string endpoint,
        object data,
        QueryBuilder? query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var url = AppendQuery($"{NormalizeEndpoint(endpoint)}.json", query);
        using var response = await _communicator.SendAsync(
            HttpMethod.Post,
            url,
            data,
            cancellationToken).ConfigureAwait(false);
        return await ProcessResponseAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates an existing record.
    /// </summary>
    public Task<DaktelaResponse<JsonElement>> PutAsync(
        string endpoint,
        string id,
        object data,
        CancellationToken cancellationToken = default)
        => PutAsync(endpoint, id, data, null, cancellationToken);

    /// <summary>
    /// Updates an existing record without requiring a response DTO.
    /// </summary>
    public async Task<DaktelaResponse<JsonElement>> PutAsync(
        string endpoint,
        string id,
        object data,
        QueryBuilder? query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Record ID cannot be null or empty.", nameof(id));
        ArgumentNullException.ThrowIfNull(data);

        var url = AppendQuery(
            $"{NormalizeEndpoint(endpoint)}/{Uri.EscapeDataString(id)}.json",
            query);
        using var response = await _communicator.SendAsync(
            HttpMethod.Put,
            url,
            data,
            cancellationToken).ConfigureAwait(false);
        return await ProcessRawResponseAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a PUT to a complete relative API path such as <c>tickets/1234</c>.
    /// </summary>
    public Task<DaktelaResponse<JsonElement>> PutAsync(
        string endpoint,
        object data,
        CancellationToken cancellationToken = default)
        => PutAsync(endpoint, data, null, cancellationToken);

    /// <summary>
    /// Sends a PUT to a complete relative API path with optional query parameters.
    /// </summary>
    public async Task<DaktelaResponse<JsonElement>> PutAsync(
        string endpoint,
        object data,
        QueryBuilder? query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var url = AppendQuery($"{NormalizeEndpoint(endpoint)}.json", query);
        using var response = await _communicator.SendAsync(
            HttpMethod.Put,
            url,
            data,
            cancellationToken).ConfigureAwait(false);
        return await ProcessRawResponseAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a typed PUT to a complete relative API path such as <c>tickets/1234</c>.
    /// </summary>
    public Task<DaktelaResponse<T>> PutAsync<T>(
        string endpoint,
        object data,
        CancellationToken cancellationToken = default)
        => PutAsync<T>(endpoint, data, null, cancellationToken);

    /// <summary>
    /// Sends a typed PUT to a complete relative API path with optional query parameters.
    /// </summary>
    public async Task<DaktelaResponse<T>> PutAsync<T>(
        string endpoint,
        object data,
        QueryBuilder? query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var url = AppendQuery($"{NormalizeEndpoint(endpoint)}.json", query);
        using var response = await _communicator.SendAsync(
            HttpMethod.Put,
            url,
            data,
            cancellationToken).ConfigureAwait(false);
        return await ProcessResponseAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates an existing record and deserializes its response.
    /// </summary>
    public Task<DaktelaResponse<T>> PutAsync<T>(
        string endpoint,
        string id,
        object data,
        CancellationToken cancellationToken = default)
        => PutAsync<T>(endpoint, id, data, null, cancellationToken);

    /// <summary>
    /// Updates an existing record with optional query parameters.
    /// </summary>
    public async Task<DaktelaResponse<T>> PutAsync<T>(
        string endpoint,
        string id,
        object data,
        QueryBuilder? query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Record ID cannot be null or empty.", nameof(id));
        ArgumentNullException.ThrowIfNull(data);

        var url = AppendQuery(
            $"{NormalizeEndpoint(endpoint)}/{Uri.EscapeDataString(id)}.json",
            query);
        using var response = await _communicator.SendAsync(
            HttpMethod.Put,
            url,
            data,
            cancellationToken).ConfigureAwait(false);
        return await ProcessResponseAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a record.
    /// </summary>
    public Task<DaktelaResponse> DeleteAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
        => DeleteAsync(endpoint, (QueryBuilder?)null, cancellationToken);

    /// <summary>
    /// Sends DELETE to a complete relative API path such as <c>contacts/john_smith</c>.
    /// </summary>
    public Task<DaktelaResponse> DeleteAsync(
        string endpoint,
        QueryBuilder? query,
        CancellationToken cancellationToken = default)
        => DeletePathAsync(NormalizeEndpoint(endpoint), query, cancellationToken);

    /// <summary>
    /// Deletes a record identified separately from its model endpoint.
    /// </summary>
    public Task<DaktelaResponse> DeleteAsync(
        string endpoint,
        string id,
        CancellationToken cancellationToken = default)
        => DeleteAsync(endpoint, id, null, cancellationToken);

    /// <summary>
    /// Deletes a record with optional query parameters.
    /// </summary>
    public async Task<DaktelaResponse> DeleteAsync(
        string endpoint,
        string id,
        QueryBuilder? query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Record ID cannot be null or empty.", nameof(id));

        var path = $"{NormalizeEndpoint(endpoint)}/{Uri.EscapeDataString(id)}";
        return await DeletePathAsync(path, query, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DaktelaResponse> DeletePathAsync(
        string path,
        QueryBuilder? query,
        CancellationToken cancellationToken)
    {
        var url = AppendQuery($"{path}.json", query);
        using var response = await _communicator.SendAsync(
            HttpMethod.Delete,
            url,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var parsed = ParseBody(response, body);
        ThrowOnError(response, body, parsed.Errors);
        return new DaktelaResponse(
            (int)response.StatusCode,
            parsed.Errors,
            body);
    }

    /// <summary>
    /// Iterates through all records, handling pagination automatically. A failed page
    /// request throws.
    /// </summary>
    public async IAsyncEnumerable<T> IterateAsync<T>(
        string endpoint,
        QueryBuilder? query = null,
        int pageSize = 100,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var paginator = Paginate<T>(endpoint, query, pageSize);
        await foreach (var item in paginator.ItemsAsync(cancellationToken).ConfigureAwait(false))
            yield return item;
    }

    /// <summary>
    /// Creates a reusable paginator with item, page, collection, filtering, and mapping helpers.
    /// A failed page request throws unless another <paramref name="errorMode"/> is chosen.
    /// </summary>
    public DaktelaPaginator<T> Paginate<T>(
        string endpoint,
        QueryBuilder? query = null,
        int pageSize = 100,
        int? maxItems = null,
        PaginationErrorMode errorMode = PaginationErrorMode.Throw)
        => new(this, endpoint, query, pageSize, maxItems, errorMode, MaxPages);

    /// <summary>
    /// Creates a paginator that silently stops (<c>true</c>) or skips the page
    /// (<c>false</c>) when a page request fails.
    /// </summary>
    [Obsolete("Use the overload taking PaginationErrorMode. Pass PaginationErrorMode.Stop or " +
              "PaginationErrorMode.Skip to keep hiding failed page requests; the default now throws.")]
    public DaktelaPaginator<T> Paginate<T>(
        string endpoint,
        QueryBuilder? query,
        int pageSize,
        int? maxItems,
        bool stopOnError)
        => Paginate<T>(endpoint, query, pageSize, maxItems, ToErrorMode(stopOnError));

    /// <summary>
    /// Iterates over full page responses rather than individual records.
    /// A failed page request throws unless another <paramref name="errorMode"/> is chosen.
    /// </summary>
    public IAsyncEnumerable<DaktelaResponse<List<T>>> IteratePagesAsync<T>(
        string endpoint,
        QueryBuilder? query = null,
        int pageSize = 100,
        int? maxItems = null,
        PaginationErrorMode errorMode = PaginationErrorMode.Throw,
        CancellationToken cancellationToken = default)
        => Paginate<T>(endpoint, query, pageSize, maxItems, errorMode)
            .PagesAsync(cancellationToken);

    /// <summary>
    /// Iterates over full page responses, silently stopping (<c>true</c>) or skipping the
    /// page (<c>false</c>) when a page request fails.
    /// </summary>
    [Obsolete("Use the overload taking PaginationErrorMode. Pass PaginationErrorMode.Stop or " +
              "PaginationErrorMode.Skip to keep hiding failed page requests; the default now throws.")]
    public IAsyncEnumerable<DaktelaResponse<List<T>>> IteratePagesAsync<T>(
        string endpoint,
        QueryBuilder? query,
        int pageSize,
        int? maxItems,
        bool stopOnError,
        CancellationToken cancellationToken = default)
        => IteratePagesAsync<T>(
            endpoint, query, pageSize, maxItems, ToErrorMode(stopOnError), cancellationToken);

    private static PaginationErrorMode ToErrorMode(bool stopOnError)
        => stopOnError ? PaginationErrorMode.Stop : PaginationErrorMode.Skip;

    private async Task<DaktelaResponse<T>> ProcessResponseAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var parsed = ParseBody(response, body);
        ThrowOnError(response, body, parsed.Errors);

        var data = parsed.HasData
            ? Deserialize<T>(parsed.Data, response, body)
            : default;
        return new DaktelaResponse<T>(
            (int)response.StatusCode,
            data,
            parsed.Total,
            parsed.Errors,
            body);
    }

    private async Task<DaktelaResponse<List<T>>> ProcessListResponseAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var parsed = ParseBody(response, body);
        ThrowOnError(response, body, parsed.Errors);

        var data = new List<T>();
        if (parsed.HasData && parsed.Data.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (parsed.Data.ValueKind != JsonValueKind.Array)
                throw CreateSerializationException(
                    response,
                    body,
                    "Daktela returned a non-array value for a list request.");
            data = Deserialize<List<T>>(parsed.Data, response, body) ?? new List<T>();
        }

        return new DaktelaResponse<List<T>>(
            (int)response.StatusCode,
            data,
            parsed.Total,
            parsed.Errors,
            body);
    }

    private async Task<DaktelaResponse<JsonElement>> ProcessRawResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var parsed = ParseBody(response, body);
        ThrowOnError(response, body, parsed.Errors);

        var data = parsed.HasData ? parsed.Data.Clone() : default;
        return new DaktelaResponse<JsonElement>(
            (int)response.StatusCode,
            data,
            parsed.Total,
            parsed.Errors,
            body);
    }

    private ParsedBody ParseBody(HttpResponseMessage response, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return ParsedBody.Empty;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            if (!response.IsSuccessStatusCode)
                return ParsedBody.Empty;
            throw CreateSerializationException(
                response,
                body,
                "Daktela returned malformed JSON.",
                ex);
        }

        var root = document.RootElement;
        var errors = DaktelaJson.ParseErrors(root);
        var data = root;
        var hasData = true;
        int? total = null;
        var hasResult = false;

        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("result", out var result))
            {
                hasResult = true;
                data = result;
                if (result.ValueKind == JsonValueKind.Object)
                {
                    if (result.TryGetProperty("data", out var nestedData))
                        data = nestedData;
                    total = ReadTotal(result);
                }
            }
            else if (root.TryGetProperty("data", out var rootData))
            {
                data = rootData;
            }

            total ??= ReadTotal(root);
            if (!total.HasValue && hasResult)
                total = 1;
        }

        return new ParsedBody(document, data, hasData, total, errors);
    }

    private T? Deserialize<T>(JsonElement element, HttpResponseMessage response, string body)
    {
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return default;

        try
        {
            return element.Deserialize<T>(_jsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw CreateSerializationException(
                response,
                body,
                $"Daktela response data could not be deserialized as {typeof(T).FullName}.",
                ex);
        }
    }

    private static int? ReadTotal(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("total", out var totalElement))
            return null;

        if (totalElement.ValueKind == JsonValueKind.Number &&
            totalElement.TryGetInt32(out var number))
            return number;
        if (totalElement.ValueKind == JsonValueKind.String &&
            int.TryParse(
                totalElement.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out number))
            return number;
        return null;
    }

    private void ThrowOnError(
        HttpResponseMessage response,
        string body,
        IReadOnlyList<DaktelaError> errors)
    {
        if (!response.IsSuccessStatusCode)
            throw ResponseErrors.Create(response, body, errors, _communicator.Redactor);
    }

    private static DaktelaException CreateSerializationException(
        HttpResponseMessage response,
        string body,
        string message,
        Exception? innerException = null)
        => innerException == null
            ? new DaktelaException(message, (int)response.StatusCode, body)
            : new DaktelaException(message, (int)response.StatusCode, body, innerException);

    private static string NormalizeEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));
        if (HttpCommunicator.IsAbsoluteUrl(endpoint))
            throw new ArgumentException("Endpoint must be relative.", nameof(endpoint));
        if (endpoint.Contains('?') || endpoint.Contains('#'))
            throw new ArgumentException(
                "Put query parameters in a QueryBuilder instead of the endpoint string.",
                nameof(endpoint));
        if (endpoint.Contains('\\'))
            throw new ArgumentException("Endpoint contains an invalid path.", nameof(endpoint));

        var path = endpoint;
        path = path.Trim('/');
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            path = path[..^5];
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Endpoint contains an invalid path.", nameof(endpoint));

        try
        {
            if (path.Split('/').Any(segment => Uri.UnescapeDataString(segment) is "." or ".."))
                throw new ArgumentException("Endpoint contains an invalid path.", nameof(endpoint));
        }
        catch (UriFormatException ex)
        {
            throw new ArgumentException("Endpoint contains invalid escaping.", nameof(endpoint), ex);
        }

        return char.ToLowerInvariant(path[0]) + path[1..];
    }

    private string AppendQuery(string url, QueryBuilder? query)
    {
        if (query == null)
            return url;
        var queryString = query.Build(_serverTimeZone);
        if (string.IsNullOrEmpty(queryString))
            return url;
        return url + (url.Contains('?') ? "&" : "?") + queryString;
    }

    private static void ValidateConfig(DaktelaConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.InstanceUrl))
            throw new ArgumentException("Instance URL is required.", nameof(config));
        if (config.Login == null && string.IsNullOrWhiteSpace(config.AccessToken))
            throw new ArgumentException("An access token or a login is required.", nameof(config));
        if (config.Login != null && !string.IsNullOrEmpty(config.AccessToken))
            throw new ArgumentException("Set either an access token or a login, not both.", nameof(config));
        if (config.Timeout <= TimeSpan.Zero && config.Timeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(config), "Timeout must be positive or infinite.");
        if (!Enum.IsDefined(config.AuthMethod))
            throw new ArgumentOutOfRangeException(nameof(config), "Authentication method is invalid.");
        if (config.MaxPages <= 0)
            throw new ArgumentOutOfRangeException(nameof(config), "Maximum pages must be greater than zero.");
        config.RetryPolicy?.Validate();
        config.RateLimitPolicy?.Validate();
        if (config.AccessToken.Any(char.IsControl))
            throw new ArgumentException("Access token cannot contain control characters.", nameof(config));
        if (config.UserAgentSuffix?.Any(char.IsControl) == true)
            throw new ArgumentException("User-Agent suffix cannot contain control characters.", nameof(config));
        _ = config.GetBaseUrl();
    }

    private static ClientDependencies CreateCommunicator(
        DaktelaConfig config,
        HttpClient? httpClient,
        TimeProvider? timeProvider = null)
    {
        ValidateConfig(config);
        var jsonOptions = DaktelaJson.CreateOptions(
            config.JsonSerializerOptions,
            config.ServerTimeZone,
            config.IncludeNullValues);
        return new ClientDependencies(
            new HttpCommunicator(config, jsonOptions, httpClient, timeProvider),
            jsonOptions,
            config.ServerTimeZone,
            config.MaxPages);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _communicator.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private sealed class ParsedBody : IDisposable
    {
        public static ParsedBody Empty => new(null, default, false, null, new List<DaktelaError>());

        private readonly JsonDocument? _document;
        public JsonElement Data { get; }
        public bool HasData { get; }
        public int? Total { get; }
        public List<DaktelaError> Errors { get; }

        public ParsedBody(
            JsonDocument? document,
            JsonElement data,
            bool hasData,
            int? total,
            List<DaktelaError> errors)
        {
            _document = document;
            Data = data;
            HasData = hasData;
            Total = total;
            Errors = errors;
        }

        public void Dispose() => _document?.Dispose();
    }

    private sealed record ClientDependencies(
        HttpCommunicator Communicator,
        JsonSerializerOptions JsonOptions,
        TimeZoneInfo? ServerTimeZone,
        int MaxPages);
}
