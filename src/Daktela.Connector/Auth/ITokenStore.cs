namespace Daktela.Connector.Auth;

/// <summary>
/// Keeps the tokens of a login session between requests and processes, so that every process
/// does not log in again: each login starts a new session on the Daktela server. Implement it
/// with your cache or database. Refresh tokens are long-lived credentials, so store them as
/// securely as passwords.
/// </summary>
/// <remarks>
/// A store that throws does not fail the request: when loading fails the connector logs in,
/// and when saving fails it keeps the tokens in memory.
/// </remarks>
public interface ITokenStore
{
    /// <summary>
    /// Returns the saved tokens, or null when none are saved.
    /// </summary>
    ValueTask<TokenSet?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves new tokens after a login or a refresh.
    /// </summary>
    ValueTask SaveAsync(TokenSet tokens, CancellationToken cancellationToken = default);
}

/// <summary>
/// Keeps the tokens in memory only. Share one instance between clients of the same user and
/// instance to share their session within a process.
/// </summary>
public sealed class InMemoryTokenStore : ITokenStore
{
    private volatile TokenSet? _tokens;

    public ValueTask<TokenSet?> LoadAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_tokens);

    public ValueTask SaveAsync(TokenSet tokens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        _tokens = tokens;
        return ValueTask.CompletedTask;
    }
}
