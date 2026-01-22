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

    public DaktelaResponse(int statusCode, T? data = default, int? total = null, List<DaktelaError>? errors = null, string? rawResponse = null)
        : base(statusCode, errors, rawResponse)
    {
        Data = data;
        Total = total;
    }
}
