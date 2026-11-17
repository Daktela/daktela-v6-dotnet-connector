namespace Daktela.Connector.Auth;

/// <summary>
/// Username and password used to start a Daktela session instead of a static access token.
/// The connector logs in on the first request, refreshes the short-lived access token before
/// it expires, and logs in again when the session has ended. Accounts with two-factor
/// authentication cannot log in this way; use a static access token for them.
/// </summary>
public sealed class DaktelaLogin
{
    public DaktelaLogin(string username, string password, ITokenStore? tokenStore = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrEmpty(password);

        Username = username;
        Password = password;
        TokenStore = tokenStore;
    }

    /// <summary>
    /// Name of the Daktela user.
    /// </summary>
    public string Username { get; }

    /// <summary>
    /// Password of the Daktela user. It is kept in memory to log in again and is never stored.
    /// </summary>
    public string Password { get; }

    /// <summary>
    /// Optional store that keeps the session tokens between processes, so that every
    /// process does not start a new session on the Daktela server.
    /// </summary>
    public ITokenStore? TokenStore { get; }

    /// <summary>
    /// Describes the login without its password.
    /// </summary>
    public override string ToString() => $"DaktelaLogin {{ Username = {Username}, Password = *** }}";
}
