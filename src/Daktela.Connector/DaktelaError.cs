namespace Daktela.Connector;

/// <summary>
/// Represents an error returned by the Daktela API.
/// </summary>
public class DaktelaError
{
    /// <summary>
    /// The error code.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// The error message.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// The field associated with the error, if applicable.
    /// </summary>
    public string? Field { get; set; }
}
