using System.Net;
using Daktela.Connector.Exceptions;

namespace Daktela.Connector.Http;

/// <summary>
/// Maps a non-success HTTP response to the connector's typed exceptions.
/// </summary>
internal static class ResponseErrors
{
    /// <param name="response">The non-success response.</param>
    /// <param name="body">Response body to expose on the exception, or null to withhold it.</param>
    /// <param name="errors">Structured errors parsed from the body.</param>
    /// <param name="redactor">Removes secrets the server may have echoed.</param>
    public static DaktelaException Create(
        HttpResponseMessage response,
        string? body,
        IReadOnlyList<DaktelaError> errors,
        SecretRedactor redactor)
    {
        body = body == null ? null : redactor.Redact(body);
        errors = errors.Select(redactor.Redact).ToList();

        var statusCode = (int)response.StatusCode;
        var detail = string.Join("; ", errors
            .Select(error => error.Message)
            .Where(message => !string.IsNullOrWhiteSpace(message)));
        var suffix = string.IsNullOrEmpty(detail) ? string.Empty : $" {detail}";
        if (statusCode is >= 300 and < 400 && response.Headers.Location is { } location)
        {
            // The location may repeat the query, which carries the token with query
            // parameter authentication.
            suffix += $" The server redirected the request to {redactor.Redact(location.OriginalString)}. " +
                      "Redirects are not followed, because they would forward the access token; " +
                      "set InstanceUrl to the final URL of the Daktela instance.";
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new DaktelaUnauthorizedException(
                "Authentication failed. Check your access token." + suffix,
                body,
                errors),

            HttpStatusCode.Forbidden => new DaktelaForbiddenException(
                "The access token is not permitted to perform this request." + suffix,
                body,
                errors),

            HttpStatusCode.NotFound => new DaktelaNotFoundException(
                "The requested resource was not found." + suffix,
                body,
                errors),

            HttpStatusCode.TooManyRequests => new DaktelaRateLimitException(
                "Rate limit exceeded. Please wait before retrying.",
                ReadRetryAfter(response),
                body,
                errors),

            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity =>
                new DaktelaValidationException(
                    "Validation failed." + suffix,
                    errors,
                    statusCode,
                    body),

            _ => new DaktelaException(
                $"API request failed with status {statusCode}." + suffix,
                statusCode,
                body,
                errors)
        };
    }

    public static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
        => response.Headers.TryGetValues("Retry-After", out var values)
            ? RateLimitHandler.ParseRetryAfter(values.FirstOrDefault())
            : null;
}
