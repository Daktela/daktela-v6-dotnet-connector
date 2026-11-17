using System.Text.Json;
using Daktela.Connector.Exceptions;
using Daktela.Connector.Http;
using Daktela.Connector.Serialization;

namespace Daktela.Connector.Auth;

/// <summary>
/// State and protocol of a session started with a username and password: logging in,
/// refreshing the access token, and resuming a stored session. One request at a time
/// changes the session; concurrent requests wait for it and then reuse its result.
/// </summary>
internal sealed class LoginSession : IDisposable
{
    /// <summary>
    /// How long before its expiry an access token is refreshed.
    /// </summary>
    internal static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    private const string LoginEndpoint = "login.json";
    private const string RefreshEndpoint = "refreshAccessToken.json";
    private const int RetiredTokenLimit = 4;

    /// <summary>
    /// Sends a login or refresh payload (already JSON) without the access token.
    /// </summary>
    internal delegate Task<HttpResponseMessage> Sender(
        string endpoint,
        string jsonPayload,
        CancellationToken cancellationToken);

    private readonly string _username;
    private readonly string _password;
    private readonly ITokenStore? _tokenStore;
    private readonly string _owner;
    private readonly Sender _send;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SecretRedactor _redactor;

    private volatile TokenSet? _tokens;
    // Tokens replaced by a refresh are still redacted, as later responses may repeat them.
    private volatile string[] _retiredTokens = [];
    private volatile bool _loginInFlight;
    private bool _storeChecked;
    private bool _refreshBeforeExpiry = true;
    // HTTP status of a login the server rejected. No further login is sent, because
    // repeating a rejected login could lock the account.
    private int? _rejectedStatus;

    public LoginSession(DaktelaLogin login, string apiBaseUrl, Sender send, TimeProvider time)
    {
        _username = login.Username;
        _password = login.Password;
        _tokenStore = login.TokenStore;
        _owner = $"{login.Username}@{apiBaseUrl}";
        _send = send;
        _time = time;
        _redactor = new SecretRedactor(GetSecrets);
    }

    /// <summary>
    /// Access token to authenticate requests with, or null before the first login.
    /// </summary>
    public string? AccessToken => _tokens?.AccessToken;

    /// <summary>
    /// Everything of this session that must not appear in an exception. The password is
    /// included only while a login is sent: in other responses a short password would
    /// mangle unrelated text.
    /// </summary>
    public IEnumerable<string> GetSecrets()
    {
        var tokens = _tokens;
        if (tokens != null)
        {
            yield return tokens.AccessToken;
            yield return tokens.RefreshToken;
        }

        foreach (var retired in _retiredTokens)
            yield return retired;

        if (_loginInFlight)
            yield return _password;
    }

    /// <summary>
    /// Makes the session ready for a request: resumes a stored session or logs in when
    /// there are no tokens, and refreshes an access token that is about to expire. A
    /// refresh that fails, for example on a network error, leaves the current access
    /// token in use until it expires.
    /// </summary>
    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (_tokens is { } current && !NeedsRefresh(current))
            return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_tokens == null && !_storeChecked)
            {
                _storeChecked = true;
                if (await LoadStoredTokensAsync(cancellationToken).ConfigureAwait(false) is { } stored)
                    UseTokens(stored, received: false);
            }

            if (_tokens == null)
            {
                await LoginAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!NeedsRefresh(_tokens))
                return;

            try
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DaktelaException) when (
                _tokens is { } kept && !kept.AccessTokenExpiresWithin(TimeSpan.Zero, _time.GetUtcNow()))
            {
                // The current access token is still valid; keep using it.
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Replaces an access token the server answered HTTP 401 to. When another request has
    /// replaced it in the meantime, nothing is sent.
    /// </summary>
    public async Task RenewRejectedAsync(string? rejectedAccessToken, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_tokens != null && _tokens.AccessToken != rejectedAccessToken)
                return;
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private bool NeedsRefresh(TokenSet tokens)
        => _refreshBeforeExpiry && tokens.AccessTokenExpiresWithin(RefreshMargin, _time.GetUtcNow());

    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        if (_rejectedStatus is { } rejected)
            throw CreateRejectedLoginException(rejected);

        try
        {
            UseTokens(await RequestTokensAsync(LoginEndpoint, cancellationToken).ConfigureAwait(false), received: true);
        }
        catch (DaktelaException ex) when (ex.StatusCode is 400 or 401)
        {
            _rejectedStatus = ex.StatusCode;
            _tokens = null;
            throw;
        }

        await SaveTokensAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a new access token; logs in again when the refresh is rejected, for example
    /// because the session has ended.
    /// </summary>
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var tokens = _tokens;
        if (tokens == null || tokens.IsRefreshTokenExpired(_time.GetUtcNow()))
        {
            await LoginAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        TokenSet refreshed;
        try
        {
            refreshed = await RequestTokensAsync(RefreshEndpoint, cancellationToken).ConfigureAwait(false);
        }
        catch (DaktelaException ex) when (ex.StatusCode is 400 or 401)
        {
            await LoginAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        UseTokens(refreshed, received: true);
        await SaveTokensAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<TokenSet> RequestTokensAsync(string endpoint, CancellationToken cancellationToken)
    {
        var isLogin = endpoint == LoginEndpoint;
        var current = _tokens;
        var payload = isLogin
            ? new Dictionary<string, string>
            {
                ["username"] = _username,
                ["password"] = _password,
                // The tokens are returned instead of being set as cookies.
                ["only_token"] = "true",
                // The connector refreshes the session itself; the server does not renew it implicitly.
                ["client_side_refresh"] = "true"
            }
            : new Dictionary<string, string> { ["refresh_token"] = current?.RefreshToken ?? string.Empty };

        // An error response to a login may echo the password.
        _loginInFlight = isLogin;
        try
        {
            using var response = await _send(endpoint, JsonSerializer.Serialize(payload), cancellationToken)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var status = (int)response.StatusCode;

            JsonDocument? document = null;
            try
            {
                document = string.IsNullOrWhiteSpace(body) ? null : JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                // Handled below as a response without tokens or without structured errors.
            }

            using (document)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var errors = document == null
                        ? new List<DaktelaError>()
                        : DaktelaJson.ParseErrors(document.RootElement);
                    // A redirect is not followed, and its body is of no use to the caller.
                    throw ResponseErrors.Create(response, status >= 400 ? body : null, errors, _redactor);
                }

                var accessToken = ReadToken(document, "accessToken");
                var refreshToken = ReadToken(document, "refreshToken");
                // The server keeps the refresh token when it does not rotate it.
                if (!isLogin && string.IsNullOrEmpty(refreshToken))
                    refreshToken = current?.RefreshToken;

                // The body of a successful response is withheld: it may contain the new tokens.
                if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
                    throw new DaktelaException(
                        $"The {endpoint} response (HTTP {status}) does not contain an access token and a refresh token.",
                        status);

                return new TokenSet(accessToken, refreshToken, _owner);
            }
        }
        finally
        {
            _loginInFlight = false;
        }
    }

    private static string? ReadToken(JsonDocument? document, string name)
        => document != null &&
           document.RootElement.ValueKind == JsonValueKind.Object &&
           document.RootElement.TryGetProperty("result", out var result) &&
           result.ValueKind == JsonValueKind.Object &&
           result.TryGetProperty(name, out var token) &&
           token.ValueKind == JsonValueKind.String
            ? token.GetString()
            : null;

    private void UseTokens(TokenSet tokens, bool received)
    {
        if (_tokens is { } replaced)
        {
            _retiredTokens = new[] { replaced.AccessToken, replaced.RefreshToken }
                .Concat(_retiredTokens)
                .Where(token => token != tokens.AccessToken && token != tokens.RefreshToken)
                .Distinct(StringComparer.Ordinal)
                .Take(RetiredTokenLimit)
                .ToArray();
        }

        _tokens = tokens;
        // A token that looks expired as soon as it is issued means the local clock is ahead of
        // the server's. Refreshing before every request would not help, so such a token is
        // refreshed only after the server rejects it.
        _refreshBeforeExpiry = !received || !tokens.AccessTokenExpiresWithin(RefreshMargin, _time.GetUtcNow());
    }

    /// <summary>
    /// Loads this session's tokens from the store. Tokens of another user or instance,
    /// expired tokens, and a store that fails are ignored, and the connector logs in instead.
    /// </summary>
    private async Task<TokenSet?> LoadStoredTokensAsync(CancellationToken cancellationToken)
    {
        if (_tokenStore == null)
            return null;

        TokenSet? stored;
        try
        {
            stored = await _tokenStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }

        return stored != null &&
               stored.Owner == _owner &&
               !stored.IsRefreshTokenExpired(_time.GetUtcNow())
            ? stored
            : null;
    }

    /// <summary>
    /// Saves the tokens to the store. When the store fails, the session continues with the
    /// tokens in memory.
    /// </summary>
    private async Task SaveTokensAsync(CancellationToken cancellationToken)
    {
        if (_tokenStore == null || _tokens is not { } tokens)
            return;

        try
        {
            await _tokenStore.SaveAsync(tokens, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Kept in memory.
        }
    }

    private static DaktelaException CreateRejectedLoginException(int status)
    {
        const string message =
            "The server rejected the login. No further login is sent, because repeating it " +
            "could lock the account; create a new client with valid credentials.";
        return status == 401
            ? new DaktelaUnauthorizedException(message, null)
            : new DaktelaException(message, status);
    }
}
