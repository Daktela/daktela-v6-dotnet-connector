namespace Daktela.Connector.Exceptions;

/// <summary>
/// Exception thrown when a network connection error occurs.
/// </summary>
public class DaktelaConnectionException : DaktelaException
{
    public DaktelaConnectionException(string message = "Failed to connect to the Daktela API.")
        : base(message)
    {
    }

    public DaktelaConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
