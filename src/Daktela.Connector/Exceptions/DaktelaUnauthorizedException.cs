namespace Daktela.Connector.Exceptions;

/// <summary>
/// Exception thrown when authentication fails (HTTP 401).
/// </summary>
public class DaktelaUnauthorizedException : DaktelaException
{
    public DaktelaUnauthorizedException(string message = "Authentication failed. Check your access token.")
        : base(message, 401)
    {
    }

    public DaktelaUnauthorizedException(
        string message,
        string? responseBody,
        IReadOnlyList<DaktelaError>? errors = null)
        : base(message, 401, responseBody, errors)
    {
    }
}
