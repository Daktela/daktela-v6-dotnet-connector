namespace Daktela.Connector.Exceptions;

/// <summary>
/// Base exception for all Daktela API errors.
/// </summary>
public class DaktelaException : Exception
{
    private static readonly IReadOnlyList<DaktelaError> NoErrors = Array.Empty<DaktelaError>();

    /// <summary>
    /// The HTTP status code returned by the API, if applicable.
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// The response body from the API, if available.
    /// </summary>
    public string? ResponseBody { get; }

    /// <summary>
    /// Structured errors returned by the Daktela API.
    /// </summary>
    public IReadOnlyList<DaktelaError> Errors { get; }

    public DaktelaException(string message)
        : base(message)
    {
        Errors = NoErrors;
    }

    public DaktelaException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = NoErrors;
    }

    public DaktelaException(string message, int statusCode, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        Errors = NoErrors;
    }

    public DaktelaException(
        string message,
        int statusCode,
        string? responseBody,
        IReadOnlyList<DaktelaError>? errors)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        Errors = errors ?? NoErrors;
    }

    public DaktelaException(string message, int statusCode, string? responseBody, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        Errors = NoErrors;
    }

    public DaktelaException(
        string message,
        int statusCode,
        string? responseBody,
        Exception innerException,
        IReadOnlyList<DaktelaError>? errors)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        Errors = errors ?? NoErrors;
    }
}
