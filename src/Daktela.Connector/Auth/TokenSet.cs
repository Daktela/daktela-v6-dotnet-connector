using System.Text.Json;

namespace Daktela.Connector.Auth;

/// <summary>
/// Access and refresh token of a session started by logging in with a username and password.
/// Both are JWTs; their expiry is read from the <c>exp</c> claim without verifying the
/// signature, which only the Daktela server can do.
/// </summary>
public sealed class TokenSet
{
    /// <summary>
    /// Creates a token set, for example to restore one saved by an <see cref="ITokenStore"/>.
    /// </summary>
    /// <param name="accessToken">Access token returned by the API.</param>
    /// <param name="refreshToken">Refresh token returned by the API.</param>
    /// <param name="owner">
    /// Session the tokens were issued for, as returned by <see cref="Owner"/> when they were saved.
    /// </param>
    public TokenSet(string accessToken, string refreshToken, string? owner = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(accessToken);
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);

        AccessToken = accessToken;
        RefreshToken = refreshToken;
        Owner = owner;
        AccessTokenExpiresAt = ReadExpiry(accessToken);
        RefreshTokenExpiresAt = ReadExpiry(refreshToken);
    }

    public string AccessToken { get; }

    public string RefreshToken { get; }

    /// <summary>
    /// Session the tokens were issued for, as <c>username@API URL</c>, or null when unknown.
    /// The connector only resumes a stored session whose owner matches its own login.
    /// </summary>
    public string? Owner { get; }

    /// <summary>
    /// When the access token expires, or null when unknown.
    /// </summary>
    public DateTimeOffset? AccessTokenExpiresAt { get; }

    /// <summary>
    /// When the refresh token expires, or null when unknown.
    /// </summary>
    public DateTimeOffset? RefreshTokenExpiresAt { get; }

    internal bool AccessTokenExpiresWithin(TimeSpan margin, DateTimeOffset now)
        => AccessTokenExpiresAt.HasValue && AccessTokenExpiresAt.Value - margin <= now;

    internal bool IsRefreshTokenExpired(DateTimeOffset now)
        => RefreshTokenExpiresAt.HasValue && RefreshTokenExpiresAt.Value <= now;

    /// <summary>
    /// Describes the token set without its tokens.
    /// </summary>
    public override string ToString()
        => $"TokenSet {{ Owner = {Owner}, AccessTokenExpiresAt = {AccessTokenExpiresAt:o}, " +
           $"RefreshTokenExpiresAt = {RefreshTokenExpiresAt:o} }}";

    private static DateTimeOffset? ReadExpiry(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
            return null;

        try
        {
            using var payload = JsonDocument.Parse(DecodeBase64Url(parts[1]));
            if (payload.RootElement.ValueKind == JsonValueKind.Object &&
                payload.RootElement.TryGetProperty("exp", out var exp) &&
                exp.ValueKind == JsonValueKind.Number &&
                exp.TryGetInt64(out var seconds))
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds);
            }
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            // Not a JWT with a readable expiry; the token is refreshed after the server rejects it.
        }

        return null;
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }
}
