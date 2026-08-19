using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daktela.Connector.Serialization;

internal static class DaktelaJson
{
    public static JsonSerializerOptions CreateOptions(JsonSerializerOptions? configuredOptions)
    {
        var options = configuredOptions == null
            ? new JsonSerializerOptions()
            : new JsonSerializerOptions(configuredOptions);

        options.PropertyNamingPolicy ??= JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = true;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.NumberHandling |= JsonNumberHandling.AllowReadingFromString;

        if (!options.Converters.Any(converter => converter is DaktelaDateTimeConverter))
            options.Converters.Add(new DaktelaDateTimeConverter());
        if (!options.Converters.Any(converter => converter is DaktelaDateTimeOffsetConverter))
            options.Converters.Add(new DaktelaDateTimeOffsetConverter());

        return options;
    }

    public static List<DaktelaError> ParseErrors(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return new List<DaktelaError>();

        if (!root.TryGetProperty("error", out var errorElement) &&
            !root.TryGetProperty("errors", out errorElement))
            return new List<DaktelaError>();

        var errors = new List<DaktelaError>();
        FlattenError(errorElement, errors, null);
        return errors;
    }

    public static Dictionary<string, List<string>> ToValidationErrors(IEnumerable<DaktelaError> errors)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var error in errors)
        {
            var key = string.IsNullOrWhiteSpace(error.Field) ? "_global" : error.Field;
            if (!result.TryGetValue(key!, out var messages))
            {
                messages = new List<string>();
                result[key!] = messages;
            }
            if (!string.IsNullOrWhiteSpace(error.Message))
                messages.Add(error.Message!);
        }
        return result;
    }

    private static void FlattenError(JsonElement element, List<DaktelaError> errors, string? field)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    FlattenError(item, errors, field);
                return;

            case JsonValueKind.Object:
                if (TryParseCanonicalError(element, field, out var error))
                {
                    errors.Add(error);
                    return;
                }

                foreach (var property in element.EnumerateObject())
                {
                    var nestedField = string.IsNullOrEmpty(field)
                        ? property.Name
                        : $"{field}.{property.Name}";
                    FlattenError(property.Value, errors, nestedField);
                }
                return;

            case JsonValueKind.String:
                var message = element.GetString();
                if (!string.IsNullOrEmpty(message))
                    errors.Add(new DaktelaError { Field = field, Message = message });
                return;

            case JsonValueKind.False:
                return;

            default:
                errors.Add(new DaktelaError
                {
                    Field = field,
                    Message = element.ToString()
                });
                return;
        }
    }

    private static bool TryParseCanonicalError(JsonElement element, string? inheritedField, out DaktelaError error)
    {
        var hasMessage = element.TryGetProperty("message", out var messageElement) &&
                         messageElement.ValueKind == JsonValueKind.String;
        var hasCode = element.TryGetProperty("code", out var codeElement);
        var hasField = element.TryGetProperty("field", out var fieldElement) &&
                       fieldElement.ValueKind == JsonValueKind.String;

        if (!hasMessage && !hasCode && !hasField)
        {
            error = null!;
            return false;
        }

        error = new DaktelaError
        {
            Message = hasMessage ? messageElement.GetString() : element.ToString(),
            Code = hasCode ? ConvertJsonValueToString(codeElement) : null,
            Field = hasField ? fieldElement.GetString() : inheritedField
        };
        return true;
    }

    private static string? ConvertJsonValueToString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : element.ToString(),
        JsonValueKind.True => bool.TrueString,
        JsonValueKind.False => bool.FalseString,
        _ => element.ToString()
    };
}
