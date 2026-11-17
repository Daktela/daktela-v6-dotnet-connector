using System.Net;
using System.Text.Json;
using Daktela.Connector.Exceptions;
using Daktela.Connector.Query;
using Daktela.Connector.Serialization;
using Xunit;

namespace Daktela.Connector.Tests;

public class HardeningTests
{
    private const string EmptyList = """{"result":{"data":[],"total":0}}""";
    private static readonly TimeZoneInfo Prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

    [Theory]
    [InlineData("/users")]
    [InlineData("/users/")]
    [InlineData("//users.json")]
    public async Task Endpoint_WithLeadingSlash_IsAccepted(string endpoint)
    {
        var handler = StubHandler.Json(EmptyList);
        using var client = handler.CreateClient();

        await client.GetAsync<JsonElement>(endpoint);

        Assert.Equal("https://tenant.daktela.test/api/v6/users.json", handler.Urls[0].AbsoluteUri);
    }

    [Theory]
    [InlineData("My App (beta")]
    [InlineData("Jiří/1.0")]
    [InlineData("a,b")]
    public void InvalidUserAgentSuffix_FailsAtConstruction(string suffix)
    {
        Assert.Throws<ArgumentException>(
            () => StubHandler.Json("{}").CreateClient(config => config.UserAgentSuffix = suffix));
    }

    [Fact]
    public async Task ValidUserAgentSuffix_IsSent()
    {
        var handler = StubHandler.Json("{}");
        using var client = handler.CreateClient(config => config.UserAgentSuffix = "MyApp/2.0 (staging)");

        await client.PingAsync();

        Assert.EndsWith(" MyApp/2.0 (staging)", handler.UserAgents[0]);
    }

    [Fact]
    public void AccessTokenThatCannotBeAHeader_FailsAtConstructionWithoutEchoingIt()
    {
        const string token = "tok\nen";
        var exception = Assert.Throws<ArgumentException>(
            () => StubHandler.Json("{}").CreateClient(config => config.AccessToken = token));

        Assert.DoesNotContain(token, exception.ToString());
    }

    [Fact]
    public async Task ConfigChangesAfterConstruction_DoNotAffectTheClient()
    {
        var handler = StubHandler.Json(EmptyList);
        DaktelaConfig? captured = null;
        using var client = handler.CreateClient(config => captured = config);

        captured!.AuthMethod = AuthMethod.QueryParam;
        captured.AccessToken = "other";
        await client.GetAsync<JsonElement>("users");

        Assert.Equal(string.Empty, handler.Urls[0].Query);
    }

    [Fact]
    public void FilterIn_FlattensASingleCollectionArgument()
    {
        const string expected = "filter[filters][0][value][0]=1&filter[filters][0][value][1]=2";

        Assert.EndsWith(expected, Decode(new QueryBuilder().Filter(Filter.In("id", new[] { 1, 2 }))));
        Assert.EndsWith(expected, Decode(new QueryBuilder().Filter(Filter.In("id", new List<int> { 1, 2 }))));
        Assert.EndsWith(expected, Decode(new QueryBuilder().Filter(Filter.NotIn("id", new List<int> { 1, 2 }))));
        Assert.EndsWith(expected, Decode(new QueryBuilder().Filter(Filter.In("id", 1, 2))));
        Assert.EndsWith(
            "[value][0]=a&filter[filters][0][value][1]=b",
            Decode(new QueryBuilder().Filter(Filter.In("id", new List<string> { "a", "b" }))));
        Assert.EndsWith(
            "[value][0]=only",
            Decode(new QueryBuilder().Filter(Filter.In("id", "only"))));
    }

    [Fact]
    public async Task NullProperties_AreOmittedByDefault_AndSentWhenEnabled()
    {
        var body = new { email = (string?)null, name = "a" };

        var omitting = StubHandler.Json("""{"result":{}}""");
        using (var client = omitting.CreateClient())
            await client.PutAsync("contacts", "1", body);
        Assert.Equal("""{"name":"a"}""", omitting.Bodies[0]);

        var including = StubHandler.Json("""{"result":{}}""");
        using (var client = including.CreateClient(config => config.IncludeNullValues = true))
            await client.PutAsync("contacts", "1", body);
        Assert.Equal("""{"email":null,"name":"a"}""", including.Bodies[0]);
    }

    [Fact]
    public async Task ServerTimeZone_ConvertsInstantsToServerWallClockWhenWriting()
    {
        var handler = StubHandler.Json("""{"result":{}}""");
        using var client = handler.CreateClient(config => config.ServerTimeZone = Prague);
        var noonUtc = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

        await client.PostAsync("activities", new
        {
            offset = noonUtc,
            utc = noonUtc.UtcDateTime,
            unspecified = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Unspecified)
        }, new QueryBuilder().Filter("time", FilterOperator.Gte, noonUtc));

        Assert.Equal(
            """{"offset":"2026-01-15 13:00:00","utc":"2026-01-15 13:00:00","unspecified":"2026-01-15 12:00:00"}""",
            handler.Bodies[0]);
        Assert.Contains("2026-01-15 13:00:00", Uri.UnescapeDataString(handler.Urls[0].Query).Replace('+', ' '));
    }

    [Fact]
    public void ServerTimeZone_GivesOffsetlessValuesTheServerOffsetWhenReading()
    {
        var options = DaktelaJson.CreateOptions(null, Prague);

        var winter = JsonSerializer.Deserialize<DateTimeOffset>("\"2026-01-15 12:00:00\"", options);
        var summer = JsonSerializer.Deserialize<DateTimeOffset>("\"2026-07-15 12:00:00\"", options);
        var explicitOffset = JsonSerializer.Deserialize<DateTimeOffset>("\"2026-01-15 12:00:00 +05:00\"", options);

        Assert.Equal(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.FromHours(1)), winter);
        Assert.Equal(TimeSpan.FromHours(1), winter.Offset);
        Assert.Equal(TimeSpan.FromHours(2), summer.Offset);
        Assert.Equal(TimeSpan.FromHours(5), explicitOffset.Offset);
    }

    [Fact]
    public void WithoutServerTimeZone_DateTimesAreWrittenUnchanged()
    {
        var options = DaktelaJson.CreateOptions(null);
        var value = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.FromHours(5));

        Assert.Equal("\"2026-01-15 12:00:00\"", JsonSerializer.Serialize(value, options));
        Assert.Equal("\"2026-01-15 12:00:00\"", JsonSerializer.Serialize(value.UtcDateTime.AddHours(5), options));
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"0000-00-00 00:00:00\"")]
    [InlineData("\"0000-00-00\"")]
    [InlineData("null")]
    public void NullableDates_ReadDaktelaEmptyPlaceholdersAsNull(string json)
    {
        var options = DaktelaJson.CreateOptions(null);

        Assert.Null(JsonSerializer.Deserialize<DateTime?>(json, options));
        Assert.Null(JsonSerializer.Deserialize<DateTimeOffset?>(json, options));
    }

    [Fact]
    public void NullableDates_StillReadAndWriteRealValues()
    {
        var options = DaktelaJson.CreateOptions(null, includeNullValues: true);
        var dto = JsonSerializer.Deserialize<Dates>(
            """{"edited":"2026-01-15 12:00:00","closed":""}""", options)!;

        Assert.Equal(new DateTime(2026, 1, 15, 12, 0, 0), dto.Edited);
        Assert.Null(dto.Closed);
        Assert.Equal(
            """{"edited":"2026-01-15 12:00:00","closed":null}""",
            JsonSerializer.Serialize(dto, options));
    }

    [Fact]
    public void NonNullableDate_StillRejectsEmptyPlaceholder()
    {
        var options = DaktelaJson.CreateOptions(null);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTime>("\"\"", options));
    }

    [Fact]
    public async Task Forbidden_ThrowsDedicatedException()
    {
        var handler = StubHandler.Json("""{"error":["no rights"]}""", HttpStatusCode.Forbidden);
        using var client = handler.CreateClient();

        var exception = await Assert.ThrowsAsync<DaktelaForbiddenException>(
            () => client.GetAsync<JsonElement>("users"));

        Assert.Equal(403, exception.StatusCode);
        Assert.Contains("no rights", exception.Message);
    }

    [Fact]
    public async Task StalledResponseBody_TimesOut()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StalledStream())
        });
        using var client = handler.CreateClient(config => config.Timeout = TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAsync<DaktelaTimeoutException>(() => client.GetAsync<JsonElement>("users"));
    }

    [Fact]
    public async Task BrokenResponseBody_IsReportedAsConnectionError()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new BrokenStream())
        });
        using var client = handler.CreateClient();

        await Assert.ThrowsAsync<DaktelaConnectionException>(() => client.GetAsync<JsonElement>("users"));
    }

    [Fact]
    public async Task BrokenResponseBody_IsRetriedForSafeMethods()
    {
        var handler = new StubHandler(request => request == 1
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream()) }
            : StubHandler.Response(EmptyList));
        using var client = handler.CreateClient(config => config.RetryPolicy = new Http.RetryPolicy
        {
            MaxRetries = 1,
            InitialDelay = TimeSpan.Zero,
            MaxDelay = TimeSpan.Zero
        });

        var response = await client.GetAsync<JsonElement>("users");

        Assert.True(response.IsSuccess);
        Assert.Equal(2, handler.Urls.Count);
    }

    private static string Decode(QueryBuilder query) => Uri.UnescapeDataString(query.Build());

    private sealed class Dates
    {
        public DateTime? Edited { get; set; }
        public DateTime? Closed { get; set; }
    }

    private abstract class ReadOnlyStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class StalledStream : ReadOnlyStream
    {
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    private sealed class BrokenStream : ReadOnlyStream
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
            => throw new IOException("Connection reset.");
    }
}
