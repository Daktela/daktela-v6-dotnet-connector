namespace Daktela.Connector.Exceptions;

/// <summary>
/// Base exception for all Daktela API errors.
/// </summary>
public class DaktelaException : Exception
{
    /// <summary>
    /// The HTTP status code returned by the API, if applicable.
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// The response body from the API, if available.
    /// </summary>
    public string? ResponseBody { get; }

    public DaktelaException(string message)
        : base(message)
    {
    }

    public DaktelaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public DaktelaException(string message, int statusCode, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public DaktelaException(string message, int statusCode, string? responseBody, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
