using System.Net;
using System.Text;
using System.Text.Json;
using Daktela.Connector.Auth;
using Daktela.Connector.Exceptions;
using Xunit;

namespace Daktela.Connector.Tests;

public class LoginSessionTests
{
    private const string Password = "p@ss \"word\"/1";
    private const string Users = """{"result":{"data":[],"total":0}}""";
    private static readonly DateTimeOffset Start = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly Clock _clock = new() { Now = Start };

    [Fact]
    public async Task FirstRequest_LogsInWithoutAToken_ThenAuthenticatesWithTheSessionToken()
    {
        var access = Jwt("access-1", Start.AddHours(1));
        var handler = Server(login: _ => Tokens(access, Jwt("refresh-1", Start.AddDays(30))));
        using var client = CreateClient(handler);

        await client.GetAsync<JsonElement>("users");
        await client.GetAsync<JsonElement>("users");

        Assert.Equal(new[] { "login.json", "users.json", "users.json" }, handler.Endpoints);
        Assert.Equal(new[] { null, access, access }, handler.AuthTokens);
        using var payload = JsonDocument.Parse(handler.Bodies[0]!);
        Assert.Equal("john", payload.RootElement.GetProperty("username").GetString());
        Assert.Equal(Password, payload.RootElement.GetProperty("password").GetString());
        Assert.Equal("true", payload.RootElement.GetProperty("only_token").GetString());
        Assert.Equal("true", payload.RootElement.GetProperty("client_side_refresh").GetString());
    }

    [Fact]
    public async Task LoginPayload_IsNotAffectedByCallerJsonOptions()
    {
        var handler = Server();
        using var client = CreateClient(handler, config => config.JsonSerializerOptions = new JsonSerializerOptions
        {
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseUpper
        });

        await client.GetAsync<JsonElement>("users");

        Assert.Contains("\"only_token\"", handler.Bodies[0]);
    }

    [Fact]
    public async Task AccessTokenAboutToExpire_IsRefreshedBeforeTheRequest()
    {
        var refresh = Jwt("refresh-1", Start.AddDays(30));
        var renewed = Jwt("access-2", Start.AddHours(2));
        var handler = Server(
            login: _ => Tokens(Jwt("access-1", Start.AddHours(1)), refresh),
            // The server does not rotate the refresh token here.
            refresh: _ => StubHandler.Response($$$"""{"result":{"accessToken":"{{{renewed}}}"}}"""));
        using var client = CreateClient(handler);

        await client.GetAsync<JsonElement>("users");
        _clock.Now = Start.AddMinutes(59).AddSeconds(30);
        await client.GetAsync<JsonElement>("users");
        _clock.Now = Start.AddMinutes(119).AddSeconds(30);
        await client.GetAsync<JsonElement>("users");

        Assert.Equal(
            new[] { "login.json", "users.json", "refreshAccessToken.json", "users.json", "refreshAccessToken.json", "users.json" },
            handler.Endpoints);
        Assert.Equal(renewed, handler.AuthTokens[3]);
        Assert.Null(handler.AuthTokens[2]);
        using var payload = JsonDocument.Parse(handler.Bodies[2]!);
        Assert.Equal(refresh, payload.RootElement.GetProperty("refresh_token").GetString());
        using var second = JsonDocument.Parse(handler.Bodies[4]!);
        Assert.Equal(refresh, second.RootElement.GetProperty("refresh_token").GetString());
    }

    [Fact]
    public async Task FailedRefresh_KeepsUsingAStillValidAccessToken()
    {
        var access = Jwt("access-1", Start.AddHours(1));
        var handler = Server(
            login: _ => Tokens(access, Jwt("refresh-1", Start.AddDays(30))),
            refresh: _ => StubHandler.Response("{}", HttpStatusCode.InternalServerError));
        using var client = CreateClient(handler);

        await client.GetAsync<JsonElement>("users");
        _clock.Now = Start.AddMinutes(59).AddSeconds(30);
        await client.GetAsync<JsonElement>("users");

        Assert.Equal(access, handler.AuthTokens[^1]);
        Assert.DoesNotContain("login.json", handler.Endpoints.Skip(1));
    }

    [Fact]
    public async Task RejectedAccessToken_IsRenewedAndTheRequestSentOnceMore()
    {
        var renewed = Jwt("access-2", Start.AddHours(2));
        var rejectFirstUse = true;
        var handler = Server(
            refresh: _ => Tokens(renewed, Jwt("refresh-2", Start.AddDays(30))),
            api: request =>
            {
                if (!rejectFirstUse)
                    return StubHandler.Response(Users);
                rejectFirstUse = false;
                return StubHandler.Response("""{"error":["expired"]}""", HttpStatusCode.Unauthorized);
            });
        using var client = CreateClient(handler);

        var response = await client.PostAsync("contacts", new { name = "a" });

        Assert.True(response.IsSuccess);
        Assert.Equal(
            new[] { "login.json", "contacts.json", "refreshAccessToken.json", "contacts.json" },
            handler.Endpoints);
        Assert.Equal(renewed, handler.AuthTokens[^1]);
    }

    [Fact]
    public async Task RejectedRefresh_LogsInAgain()
    {
        var logins = 0;
        var second = Jwt("access-2", Start.AddHours(2));
        var handler = Server(
            login: _ => ++logins == 1
                ? Tokens(Jwt("access-1", Start.AddHours(1)), Jwt("refresh-1", Start.AddDays(30)))
                : Tokens(second, Jwt("refresh-2", Start.AddDays(30))),
            refresh: _ => StubHandler.Response("""{"error":["ended"]}""", HttpStatusCode.Unauthorized));
        using var client = CreateClient(handler);

        await client.GetAsync<JsonElement>("users");
        _clock.Now = Start.AddMinutes(59).AddSeconds(30);
        await client.GetAsync<JsonElement>("users");

        Assert.Equal(
            new[] { "login.json", "users.json", "refreshAccessToken.json", "login.json", "users.json" },
            handler.Endpoints);
        Assert.Equal(second, handler.AuthTokens[^1]);
    }

    [Fact]
    public async Task ExpiredRefreshToken_LogsInWithoutTryingToRefresh()
    {
        var handler = Server(
            login: _ => Tokens(Jwt("access-1", Start.AddHours(1)), Jwt("refresh-1", Start.AddHours(2))));
        using var client = CreateClient(handler);

        await client.GetAsync<JsonElement>("users");
        _clock.Now = Start.AddHours(3);
        await client.GetAsync<JsonElement>("users");

        Assert.Equal(new[] { "login.json", "users.json", "login.json", "users.json" }, handler.Endpoints);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task RejectedLogin_IsNotRepeated(HttpStatusCode status)
    {
        var handler = Server(login: _ => StubHandler.Response("""{"error":["Wrong credentials"]}""", status));
        using var client = CreateClient(handler);

        var first = await Assert.ThrowsAnyAsync<DaktelaException>(() => client.GetAsync<JsonElement>("users"));
        var second = await Assert.ThrowsAnyAsync<DaktelaException>(() => client.GetAsync<JsonElement>("users"));

        Assert.Equal((int)status, first.StatusCode);
        Assert.Equal((int)status, second.StatusCode);
        Assert.Contains("lock the account", second.Message);
        Assert.Single(handler.Urls);
        Assert.False(await client.PingAsync());
        Assert.Single(handler.Urls);
    }

    [Fact]
    public async Task TemporaryLoginFailure_IsRetriedByTheNextRequest()
    {
        var logins = 0;
        var handler = Server(login: _ => ++logins == 1
            ? StubHandler.Response("{}", HttpStatusCode.ServiceUnavailable)
            : Tokens(Jwt("access-1", Start.AddHours(1)), Jwt("refresh-1", Start.AddDays(30))));
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<DaktelaException>(() => client.GetAsync<JsonElement>("users"));
        await client.GetAsync<JsonElement>("users");

        Assert.Equal(new[] { "login.json", "login.json", "users.json" }, handler.Endpoints);
    }

    [Fact]
    public async Task LoginResponseWithoutTokens_FailsWithoutExposingTheBody()
    {
        var handler = Server(login: _ => StubHandler.Response("""{"result":{"accessToken":"only-one"}}"""));
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<DaktelaException>(() => client.GetAsync<JsonElement>("users"));

        Assert.Contains("does not contain an access token and a refresh token", exception.Message);
        Assert.Null(exception.ResponseBody);
    }

    [Fact]
    public async Task ConcurrentFirstRequests_LogInOnce()
    {
        var handler = Server();
        using var client = CreateClient(handler);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => client.GetAsync<JsonElement>("users")));

        Assert.Single(handler.Endpoints, endpoint => endpoint == "login.json");
    }

    [Fact]
    public async Task TokenStore_SharesOneSessionBetweenClients()
    {
        var store = new InMemoryTokenStore();
        var handler = Server();
        using var first = CreateClient(handler, store: store);
        using var second = CreateClient(handler, store: store);

        await first.GetAsync<JsonElement>("users");
        await second.GetAsync<JsonElement>("users");

        Assert.Single(handler.Endpoints, endpoint => endpoint == "login.json");
        var saved = await store.LoadAsync();
        Assert.Equal("john@https://tenant.daktela.test/api/v6/", saved!.Owner);
        Assert.Equal(Start.AddHours(1), saved.AccessTokenExpiresAt);
    }

    [Fact]
    public async Task TokenStore_TokensOfAnotherUserAreNotUsed()
    {
        var store = new InMemoryTokenStore();
        await store.SaveAsync(new TokenSet(
            Jwt("foreign", Start.AddHours(1)),
            Jwt("foreign-refresh", Start.AddDays(30)),
            "jane@https://tenant.daktela.test/api/v6/"));
        var handler = Server();
        using var client = CreateClient(handler, store: store);

        await client.GetAsync<JsonElement>("users");

        Assert.Equal("login.json", handler.Endpoints.First());
        Assert.StartsWith("john@", (await store.LoadAsync())!.Owner);
    }

    [Fact]
    public async Task TokenStore_ThatFails_DoesNotFailTheRequest()
    {
        var handler = Server();
        using var client = CreateClient(handler, store: new FailingStore());

        var response = await client.GetAsync<JsonElement>("users");

        Assert.True(response.IsSuccess);
    }

    [Fact]
    public async Task LoginErrorEchoingThePassword_IsRedacted()
    {
        var echoed = JsonSerializer.Serialize(Password);
        var handler = Server(login: _ => StubHandler.Response(
            $$$"""{"error":{"password":"Invalid value {{{echoed[1..^1]}}}"}}""",
            HttpStatusCode.UnprocessableEntity));
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAnyAsync<DaktelaException>(() => client.GetAsync<JsonElement>("users"));

        Assert.DoesNotContain(Password, exception.ToString());
        Assert.DoesNotContain(echoed[1..^1], exception.ResponseBody);
        Assert.Contains("***", exception.ResponseBody);
        Assert.All(exception.Errors, error => Assert.DoesNotContain(Password, error.Message));
    }

    [Fact]
    public async Task ErrorEchoingSessionTokens_IsRedacted_IncludingReplacedOnes()
    {
        var firstAccess = Jwt("access-1", Start.AddHours(1));
        var refresh = Jwt("refresh-2", Start.AddDays(30));
        var requests = 0;
        var handler = Server(
            refresh: _ => Tokens(Jwt("access-2", Start.AddHours(2)), refresh),
            api: _ => ++requests == 1
                ? StubHandler.Response(Users)
                : StubHandler.Response(
                    $$$"""{"error":["token {{{firstAccess}}} and {{{refresh}}} denied"]}""",
                    HttpStatusCode.Forbidden));
        using var client = CreateClient(handler);

        await client.GetAsync<JsonElement>("users");
        _clock.Now = Start.AddMinutes(59).AddSeconds(30);
        var exception = await Assert.ThrowsAsync<DaktelaForbiddenException>(
            () => client.GetAsync<JsonElement>("users"));

        Assert.DoesNotContain(firstAccess, exception.ToString() + exception.ResponseBody);
        Assert.DoesNotContain(refresh, exception.ToString() + exception.ResponseBody);
        Assert.Contains("token *** and *** denied", exception.Message);
    }

    [Fact]
    public void Config_RequiresExactlyOneOfTokenAndLogin()
    {
        var handler = Server();

        Assert.Throws<ArgumentException>(() => handler.CreateClient(config => config.AccessToken = ""));
        Assert.Throws<ArgumentException>(() => handler.CreateClient(
            config => config.Login = new DaktelaLogin("john", Password)));
    }

    [Fact]
    public void Secrets_AreNotPrintedByToString()
    {
        var login = new DaktelaLogin("john", Password);
        var tokens = new TokenSet(Jwt("access-1", Start), "refresh-not-a-jwt");

        Assert.DoesNotContain(Password, login.ToString());
        Assert.Contains("john", login.ToString());
        Assert.DoesNotContain("access-1", tokens.ToString());
        Assert.DoesNotContain("refresh-not-a-jwt", tokens.ToString());
        Assert.Equal(Start, tokens.AccessTokenExpiresAt);
        Assert.Null(tokens.RefreshTokenExpiresAt);
    }

    private DaktelaClient CreateClient(
        StubHandler handler,
        Action<DaktelaConfig>? configure = null,
        ITokenStore? store = null)
        => handler.CreateClient(
            config =>
            {
                config.AccessToken = string.Empty;
                config.Login = new DaktelaLogin("john", Password, store);
                configure?.Invoke(config);
            },
            _clock);

    /// <summary>
    /// A Daktela stand-in that routes login, refresh and API requests.
    /// </summary>
    private static StubHandler Server(
        Func<HttpRequestMessage, HttpResponseMessage>? login = null,
        Func<HttpRequestMessage, HttpResponseMessage>? refresh = null,
        Func<HttpRequestMessage, HttpResponseMessage>? api = null)
        => new((_, request) => request.RequestUri!.Segments[^1] switch
        {
            "login.json" => (login ?? (_ => Tokens(
                Jwt("access-1", Start.AddHours(1)),
                Jwt("refresh-1", Start.AddDays(30)))))(request),
            "refreshAccessToken.json" => (refresh ?? (_ => Tokens(
                Jwt("access-2", Start.AddHours(2)),
                Jwt("refresh-2", Start.AddDays(30)))))(request),
            _ => (api ?? (_ => StubHandler.Response(Users)))(request)
        });

    private static HttpResponseMessage Tokens(string accessToken, string refreshToken)
        => StubHandler.Response(
            $$$"""{"result":{"accessToken":"{{{accessToken}}}","refreshToken":"{{{refreshToken}}}"},"error":[]}""");

    private static string Jwt(string subject, DateTimeOffset expires)
    {
        static string Encode(string json)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{Encode("""{"alg":"HS256"}""")}." +
               $"{Encode($$$"""{"sub":"{{{subject}}}","exp":{{{expires.ToUnixTimeSeconds()}}}}""")}.signature";
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FailingStore : ITokenStore
    {
        public ValueTask<TokenSet?> LoadAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Store is unavailable.");

        public ValueTask SaveAsync(TokenSet tokens, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Store is unavailable.");
    }
}
