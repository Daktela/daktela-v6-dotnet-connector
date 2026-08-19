namespace Daktela.Connector.Exceptions;

/// <summary>
/// Exception thrown when a resource is not found (HTTP 404).
/// </summary>
public class DaktelaNotFoundException : DaktelaException
{
    public DaktelaNotFoundException(string message = "The requested resource was not found.")
        : base(message, 404)
    {
    }

    public DaktelaNotFoundException(
        string message,
        string? responseBody,
        IReadOnlyList<DaktelaError>? errors = null)
        : base(message, 404, responseBody, errors)
    {
    }
}
