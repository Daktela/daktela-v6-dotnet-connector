using System.Net;
using System.Text.Json;
using Daktela.Connector.Exceptions;
using Xunit;

namespace Daktela.Connector.Tests;

public class RedactionTests
{
    private const string Token = "s3cr3t/tok+en value";

    [Fact]
    public async Task ErrorResponseEchoingTheAccessToken_IsRedactedEverywhereOnTheException()
    {
        var urlEncoded = Uri.EscapeDataString(Token);
        var handler = StubHandler.Json(
            $$$"""{"error":{"token":"Token {{{Token}}} is invalid","{{{Token}}}":"see /x?accessToken={{{urlEncoded}}}"}}""",
            HttpStatusCode.Unauthorized);
        using var client = handler.CreateClient(config => config.AccessToken = Token);

        var exception = await Assert.ThrowsAsync<DaktelaUnauthorizedException>(
            () => client.GetAsync<JsonElement>("users"));

        var everything = exception + exception.ResponseBody +
                         string.Concat(exception.Errors.Select(error => error.Field + error.Message + error.Code));
        Assert.DoesNotContain(Token, everything);
        Assert.DoesNotContain(urlEncoded, everything);
        Assert.Contains("Token *** is invalid", exception.Message);
    }

    [Fact]
    public async Task ValidationErrors_AreRedacted()
    {
        var handler = StubHandler.Json(
            $$$"""{"error":{"name":"bad value {{{Token}}}"}}""",
            HttpStatusCode.BadRequest);
        using var client = handler.CreateClient(config => config.AccessToken = Token);

        var exception = await Assert.ThrowsAsync<DaktelaValidationException>(
            () => client.PostAsync("contacts", new { name = "a" }));

        Assert.Equal("bad value ***", Assert.Single(exception.ValidationErrors["name"]));
    }

    [Fact]
    public async Task SuccessfulResponse_IsNotAltered()
    {
        var handler = StubHandler.Json($$$"""{"result":{"token":"{{{Token}}}"}}""");
        using var client = handler.CreateClient(config => config.AccessToken = Token);

        var response = await client.GetAsync("apiToken");

        Assert.Equal(Token, response.Data.GetProperty("token").GetString());
    }

    [Fact]
    public async Task ConnectionErrorQuotingTheRequestUrl_IsRedactedAndNotChained()
    {
        var handler = new StubHandler((_, request) =>
            throw new HttpRequestException($"Could not reach {request.RequestUri}"));
        using var client = handler.CreateClient(config =>
        {
            config.AccessToken = Token;
            config.AuthMethod = AuthMethod.QueryParam;
        });

        var exception = await Assert.ThrowsAsync<DaktelaConnectionException>(
            () => client.GetAsync<JsonElement>("users"));
        var health = await client.HealthCheckAsync();

        Assert.DoesNotContain(Uri.EscapeDataString(Token), exception.ToString());
        Assert.DoesNotContain("tok", exception.ToString());
        Assert.Contains("accessToken=***", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(Uri.EscapeDataString(Token), health.Error);
    }

    [Fact]
    public async Task RedirectResponse_IsAnErrorNamingTheTargetWithoutTheToken()
    {
        var handler = new StubHandler((_, request) =>
        {
            var response = StubHandler.Response(string.Empty, HttpStatusCode.MovedPermanently);
            response.Headers.Location = new Uri("https://other.test" + request.RequestUri!.PathAndQuery);
            return response;
        });
        using var client = handler.CreateClient(config =>
        {
            config.AccessToken = Token;
            config.AuthMethod = AuthMethod.QueryParam;
        });

        var exception = await Assert.ThrowsAsync<DaktelaException>(() => client.GetAsync<JsonElement>("users"));

        Assert.Equal(301, exception.StatusCode);
        Assert.Contains("redirected the request to https://other.test/api/v6/users.json?accessToken=***", exception.Message);
        Assert.DoesNotContain("s3cr3t", exception.ToString());
        Assert.Single(handler.Urls);
    }

    [Fact]
    public async Task RedirectFollowedToAnotherOriginByAnInjectedClient_FailsInsteadOfUsingTheResponse()
    {
        var handler = new StubHandler((_, _) =>
        {
            var response = StubHandler.Response("""{"result":{"data":[],"total":0}}""");
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.test/api/v6/users.json");
            return response;
        });
        using var client = handler.CreateClient();

        var exception = await Assert.ThrowsAsync<DaktelaException>(() => client.GetAsync<JsonElement>("users"));

        Assert.Contains("https://evil.test", exception.Message);
        Assert.Contains("Disable automatic redirects", exception.Message);
        Assert.False(await client.PingAsync());
    }

    [Fact]
    public async Task RedirectFollowedWithinTheSameOrigin_IsAccepted()
    {
        var handler = new StubHandler((_, _) =>
        {
            var response = StubHandler.Response("""{"result":{"data":[],"total":0}}""");
            response.RequestMessage = new HttpRequestMessage(
                HttpMethod.Get, "https://TENANT.daktela.test/api/v6/users.json?moved=1");
            return response;
        });
        using var client = handler.CreateClient();

        Assert.True((await client.GetAsync<JsonElement>("users")).IsSuccess);
    }

    [Fact]
    public async Task ConnectionErrorWithoutSecrets_KeepsItsInnerException()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("Connection refused"));
        using var client = handler.CreateClient();

        var exception = await Assert.ThrowsAsync<DaktelaConnectionException>(
            () => client.GetAsync<JsonElement>("users"));

        Assert.IsType<HttpRequestException>(exception.InnerException);
    }
}
