using System.Text.Json;

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
            var json = JsonDocument.Parse(responseBody);
            if (json.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                var errors = new Dictionary<string, List<string>>();
                foreach (var prop in errorsElement.EnumerateObject())
                {
                    var fieldErrors = new List<string>();
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var error in prop.Value.EnumerateArray())
                        {
                            if (error.ValueKind == JsonValueKind.String)
                            {
                                fieldErrors.Add(error.GetString() ?? string.Empty);
                            }
                        }
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        fieldErrors.Add(prop.Value.GetString() ?? string.Empty);
                    }
                    errors[prop.Name] = fieldErrors;
                }
                return errors;
            }
        }
        catch (JsonException)
        {
            // If parsing fails, return empty dictionary
        }

        return new Dictionary<string, List<string>>();
    }
}
