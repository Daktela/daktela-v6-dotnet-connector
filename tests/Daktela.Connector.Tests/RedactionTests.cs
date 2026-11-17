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
    public async Task ConnectionErrorWithoutSecrets_KeepsItsInnerException()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("Connection refused"));
        using var client = handler.CreateClient();

        var exception = await Assert.ThrowsAsync<DaktelaConnectionException>(
            () => client.GetAsync<JsonElement>("users"));

        Assert.IsType<HttpRequestException>(exception.InnerException);
    }
}
