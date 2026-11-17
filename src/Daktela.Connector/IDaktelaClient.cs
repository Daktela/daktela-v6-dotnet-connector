using System.Text.Json;
using Daktela.Connector.Query;

namespace Daktela.Connector;

/// <summary>
/// Operations of the Daktela V6 API client. Depend on this interface to substitute the
/// client in tests.
/// </summary>
public interface IDaktelaClient : IDisposable
{
    /// <summary>Checks the authenticated <c>whoim</c> endpoint.</summary>
    Task<bool> PingAsync(CancellationToken cancellationToken = default);

    /// <summary>Checks the authenticated <c>whoim</c> endpoint and returns status and latency details.</summary>
    Task<DaktelaHealthCheck> HealthCheckAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets a single record by ID.</summary>
    Task<DaktelaResponse<T>> GetAsync<T>(string endpoint, string id, CancellationToken cancellationToken = default);

    /// <summary>Gets a single record by ID with optional query parameters.</summary>
    Task<DaktelaResponse<T>> GetAsync<T>(string endpoint, string id, QueryBuilder? query, CancellationToken cancellationToken = default);

    /// <summary>Gets a list of records with optional query parameters.</summary>
    Task<DaktelaResponse<List<T>>> GetAsync<T>(string endpoint, QueryBuilder? query = null, CancellationToken cancellationToken = default);

    /// <summary>Gets untyped data from an endpoint.</summary>
    Task<DaktelaResponse<JsonElement>> GetAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>Gets untyped data using arbitrary Daktela query parameters.</summary>
    Task<DaktelaResponse<JsonElement>> GetAsync(string endpoint, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken = default);

    /// <summary>Creates a new record without requiring a response DTO.</summary>
    Task<DaktelaResponse<JsonElement>> PostAsync(string endpoint, object data, CancellationToken cancellationToken = default);

    /// <summary>Creates a new record without requiring a response DTO, with optional query parameters.</summary>
    Task<DaktelaResponse<JsonElement>> PostAsync(string endpoint, object data, QueryBuilder? query, CancellationToken cancellationToken = default);

    /// <summary>Creates a new record and deserializes its response.</summary>
    Task<DaktelaResponse<T>> PostAsync<T>(string endpoint, object data, CancellationToken cancellationToken = default);

    /// <summary>Creates a new record and deserializes its response, with optional query parameters.</summary>
    Task<DaktelaResponse<T>> PostAsync<T>(string endpoint, object data, QueryBuilder? query, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing record without requiring a response DTO.</summary>
    Task<DaktelaResponse<JsonElement>> PutAsync(string endpoint, string id, object data, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing record without requiring a response DTO, with optional query parameters.</summary>
    Task<DaktelaResponse<JsonElement>> PutAsync(string endpoint, string id, object data, QueryBuilder? query, CancellationToken cancellationToken = default);

    /// <summary>Sends a PUT to a complete relative API path such as <c>tickets/1234</c>.</summary>
    Task<DaktelaResponse<JsonElement>> PutAsync(string endpoint, object data, CancellationToken cancellationToken = default);

    /// <summary>Sends a PUT to a complete relative API path with optional query parameters.</summary>
    Task<DaktelaResponse<JsonElement>> PutAsync(string endpoint, object data, QueryBuilder? query, CancellationToken cancellationToken = default);

    /// <summary>Sends a typed PUT to a complete relative API path such as <c>tickets/1234</c>.</summary>
    Task<DaktelaResponse<T>> PutAsync<T>(string endpoint, object data, CancellationToken cancellationToken = default);

    /// <summary>Sends a typed PUT to a complete relative API path with optional query parameters.</summary>
    Task<DaktelaResponse<T>> PutAsync<T>(string endpoint, object data, QueryBuilder? query, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing record and deserializes its response.</summary>
    Task<DaktelaResponse<T>> PutAsync<T>(string endpoint, string id, object data, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing record and deserializes its response, with optional query parameters.</summary>
    Task<DaktelaResponse<T>> PutAsync<T>(string endpoint, string id, object data, QueryBuilder? query, CancellationToken cancellationToken = default);

    /// <summary>Sends DELETE to a complete relative API path such as <c>contacts/john_smith</c>.</summary>
    Task<DaktelaResponse> DeleteAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>Sends DELETE to a complete relative API path with optional query parameters.</summary>
    Task<DaktelaResponse> DeleteAsync(string endpoint, QueryBuilder? query, CancellationToken cancellationToken = default);

    /// <summary>Deletes a record identified separately from its model endpoint.</summary>
    Task<DaktelaResponse> DeleteAsync(string endpoint, string id, CancellationToken cancellationToken = default);

    /// <summary>Deletes a record with optional query parameters.</summary>
    Task<DaktelaResponse> DeleteAsync(string endpoint, string id, QueryBuilder? query, CancellationToken cancellationToken = default);

    /// <summary>Iterates through all records, handling pagination automatically.</summary>
    IAsyncEnumerable<T> IterateAsync<T>(string endpoint, QueryBuilder? query = null, int pageSize = 100, CancellationToken cancellationToken = default);

    /// <summary>Creates a reusable paginator.</summary>
    DaktelaPaginator<T> Paginate<T>(string endpoint, QueryBuilder? query = null, int pageSize = 100, int? maxItems = null, PaginationErrorMode errorMode = PaginationErrorMode.Throw);

    /// <summary>Iterates over full page responses rather than individual records.</summary>
    IAsyncEnumerable<DaktelaResponse<List<T>>> IteratePagesAsync<T>(string endpoint, QueryBuilder? query = null, int pageSize = 100, int? maxItems = null, PaginationErrorMode errorMode = PaginationErrorMode.Throw, CancellationToken cancellationToken = default);
}
