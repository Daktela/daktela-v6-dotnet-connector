namespace Daktela.Connector.Exceptions;

/// <summary>
/// Exception thrown when a request times out.
/// </summary>
public class DaktelaTimeoutException : DaktelaException
{
    public DaktelaTimeoutException(string message = "The request timed out.")
        : base(message)
    {
    }

    public DaktelaTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
