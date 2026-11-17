namespace Daktela.Connector.Exceptions;

/// <summary>
/// Exception thrown when the access token is not permitted to perform the request (HTTP 403).
/// </summary>
public class DaktelaForbiddenException : DaktelaException
{
    public DaktelaForbiddenException(string message = "The access token is not permitted to perform this request.")
        : base(message, 403)
    {
    }

    public DaktelaForbiddenException(
        string message,
        string? responseBody,
        IReadOnlyList<DaktelaError>? errors = null)
        : base(message, 403, responseBody, errors)
    {
    }
}
