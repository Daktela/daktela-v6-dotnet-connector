using System.Text.Json;

namespace Daktela.Connector;

/// <summary>
/// Represents a response from the Daktela API without data.
/// </summary>
public class DaktelaResponse
{
    /// <summary>
    /// The HTTP status code of the response.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// List of errors returned by the API, if any.
    /// </summary>
    public List<DaktelaError>? Errors { get; }

    /// <summary>
    /// Indicates whether the request was successful (2xx status code).
    /// </summary>
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;

    /// <summary>
    /// Indicates whether the API returned one or more structured errors.
    /// </summary>
    public bool HasErrors => Errors is { Count: > 0 };

    /// <summary>
    /// The first structured API error, or null when none were returned.
    /// </summary>
    public DaktelaError? FirstError => Errors?.FirstOrDefault();

    /// <summary>
    /// The raw JSON response body.
    /// </summary>
    public string? RawResponse { get; }

    public DaktelaResponse(int statusCode, List<DaktelaError>? errors = null, string? rawResponse = null)
    {
        StatusCode = statusCode;
        Errors = errors;
        RawResponse = rawResponse;
    }
}

/// <summary>
/// Represents a response from the Daktela API with typed data.
/// </summary>
/// <typeparam name="T">The type of the response data.</typeparam>
public class DaktelaResponse<T> : DaktelaResponse
{
    /// <summary>
    /// The deserialized response data.
    /// </summary>
    public T? Data { get; }

    /// <summary>
    /// The total number of records available (for paginated responses).
    /// </summary>
    public int? Total { get; }

    /// <summary>
    /// Indicates whether response data is null, undefined, or an empty collection/object.
    /// </summary>
    public bool IsEmpty => Data switch
    {
        null => true,
        JsonElement element => element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => true,
            JsonValueKind.Array => element.GetArrayLength() == 0,
            JsonValueKind.Object => !element.EnumerateObject().Any(),
            _ => false
        },
        System.Collections.ICollection collection => collection.Count == 0,
        System.Collections.IEnumerable enumerable and not string =>
            !enumerable.Cast<object?>().Any(),
        _ => false
    };

    public DaktelaResponse(int statusCode, T? data = default, int? total = null, List<DaktelaError>? errors = null, string? rawResponse = null)
        : base(statusCode, errors, rawResponse)
    {
        Data = data;
        Total = total;
    }
}
