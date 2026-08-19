using System.Text.Json;
using Daktela.Connector.Serialization;

namespace Daktela.Connector.Exceptions;

/// <summary>
/// Exception thrown when request validation fails (HTTP 400/422).
/// </summary>
public class DaktelaValidationException : DaktelaException
{
    /// <summary>
    /// The validation errors returned by the API.
    /// </summary>
    public Dictionary<string, List<string>> ValidationErrors { get; }

    public DaktelaValidationException(string message, int statusCode = 400)
        : base(message, statusCode)
    {
        ValidationErrors = new Dictionary<string, List<string>>();
    }

    public DaktelaValidationException(string message, Dictionary<string, List<string>> validationErrors, int statusCode = 400, string? responseBody = null)
        : base(message, statusCode, responseBody)
    {
        ValidationErrors = validationErrors;
    }

    public DaktelaValidationException(
        string message,
        IReadOnlyList<DaktelaError> errors,
        int statusCode = 400,
        string? responseBody = null)
        : base(message, statusCode, responseBody, errors)
    {
        ValidationErrors = DaktelaJson.ToValidationErrors(errors);
    }

    public DaktelaValidationException(string message, string? responseBody, int statusCode = 400)
        : base(message, statusCode, responseBody)
    {
        ValidationErrors = ParseValidationErrors(responseBody);
    }

    private static Dictionary<string, List<string>> ParseValidationErrors(string? responseBody)
    {
        if (string.IsNullOrEmpty(responseBody))
            return new Dictionary<string, List<string>>();

        try
        {
            using var json = JsonDocument.Parse(responseBody);
            return DaktelaJson.ToValidationErrors(DaktelaJson.ParseErrors(json.RootElement));
        }
        catch (JsonException)
        {
            // If parsing fails, return empty dictionary
        }

        return new Dictionary<string, List<string>>();
    }
}
