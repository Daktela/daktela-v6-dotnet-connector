using System.Text.Encodings.Web;
using System.Text.Json;
using System.Web;

namespace Daktela.Connector.Http;

/// <summary>
/// Removes access tokens, refresh tokens and (while a login is sent) the password from text
/// that ends up in exceptions, where a server or an HTTP handler may have echoed them.
/// </summary>
internal sealed class SecretRedactor(Func<IEnumerable<string>> secrets)
{
    public const string Mask = "***";

    public string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        foreach (var form in Forms())
            text = text.Replace(form, Mask, StringComparison.Ordinal);
        return text;
    }

    public bool ContainsSecret(string text)
        => !string.IsNullOrEmpty(text) && Forms().Any(form => text.Contains(form, StringComparison.Ordinal));

    public DaktelaError Redact(DaktelaError error) => new()
    {
        Code = error.Code == null ? null : Redact(error.Code),
        Message = error.Message == null ? null : Redact(error.Message),
        Field = error.Field == null ? null : Redact(error.Field)
    };

    /// <summary>
    /// Returns the exception when it is safe to chain as an inner exception, or null when
    /// any message in its chain or its stack trace text quotes a secret.
    /// </summary>
    public Exception? SafeInner(Exception exception)
        => ContainsSecret(exception.ToString()) ? null : exception;

    /// <summary>
    /// How the secret reads in <see cref="Uri.ToString"/> of a request URL carrying it as a
    /// query value: only partly unescaped, so neither the raw nor the escaped form matches.
    /// </summary>
    private static string UriDisplayForm(string secret)
    {
        const string prefix = "http://host/?";
        return Uri.TryCreate(prefix + Uri.EscapeDataString(secret), UriKind.Absolute, out var uri)
            ? uri.ToString()[prefix.Length..]
            : secret;
    }

    // Longest first, so that an encoded form is not partly replaced through a shorter one.
    private IEnumerable<string> Forms()
        => secrets()
            .Where(secret => !string.IsNullOrEmpty(secret))
            .SelectMany(secret => new[]
            {
                secret,
                Uri.EscapeDataString(secret),
                HttpUtility.UrlEncode(secret),
                UriDisplayForm(secret),
                JsonEncodedText.Encode(secret).ToString(),
                JsonEncodedText.Encode(secret, JavaScriptEncoder.UnsafeRelaxedJsonEscaping)
                    .ToString()
                    .Replace("/", "\\/", StringComparison.Ordinal)
            })
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(form => form.Length);
}
