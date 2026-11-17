using System.Net;
using System.Text.Json;
using Daktela.Connector.Exceptions;
using Xunit;

namespace Daktela.Connector.Tests;

public class PaginationTests
{
    private const string Unauthorized = """{"error":["bad token"]}""";

    [Fact]
    public async Task Paginator_ThrowsOnFailedFirstPageByDefault()
    {
        var handler = StubHandler.Json(Unauthorized, HttpStatusCode.Unauthorized);
        using var client = handler.CreateClient();

        await Assert.ThrowsAsync<DaktelaUnauthorizedException>(
            () => client.Paginate<JsonElement>("users").ToListAsync());
        Assert.Single(handler.Urls);
    }

    [Fact]
    public async Task Paginator_ThrowsOnMidStreamFailureInsteadOfReturningPartialData()
    {
        var handler = new StubHandler(request => request == 2
            ? StubHandler.Response("{}", HttpStatusCode.InternalServerError)
            : StubHandler.Response("""{"result":{"data":[1,2],"total":6}}"""));
        using var client = handler.CreateClient();
        var received = new List<int>();

        var exception = await Assert.ThrowsAsync<DaktelaException>(async () =>
        {
            await foreach (var item in client.Paginate<int>("users", pageSize: 2))
                received.Add(item);
        });

        Assert.Equal(500, exception.StatusCode);
        Assert.Equal(new[] { 1, 2 }, received);
    }

    [Fact]
    public async Task IteratePagesAsync_ThrowsOnFailedPageByDefault()
    {
        var handler = StubHandler.Json("{}", HttpStatusCode.InternalServerError);
        using var client = handler.CreateClient();

        await Assert.ThrowsAsync<DaktelaException>(async () =>
        {
            await foreach (var _ in client.IteratePagesAsync<int>("users"))
            {
            }
        });
    }

    [Fact]
    public async Task SkipMode_StillThrowsOnAuthenticationFailure()
    {
        var handler = StubHandler.Json(Unauthorized, HttpStatusCode.Unauthorized);
        using var client = handler.CreateClient();

        await Assert.ThrowsAsync<DaktelaUnauthorizedException>(
            () => client.Paginate<int>("users", errorMode: PaginationErrorMode.Skip).ToListAsync());
        Assert.Single(handler.Urls);
    }

    [Fact]
    public async Task ObsoleteBooleanOverloads_KeepTheirSilentBehavior()
    {
        var handler = StubHandler.Json("{}", HttpStatusCode.InternalServerError);
        using var client = handler.CreateClient(config => config.MaxPages = 3);

#pragma warning disable CS0618 // Verifying the obsolete overloads still behave as before.
        Assert.Empty(await client.Paginate<int>("users", null, 100, null, stopOnError: true).ToListAsync());
        Assert.Single(handler.Urls);

        var pages = 0;
        await foreach (var _ in client.IteratePagesAsync<int>("users", null, 100, null, stopOnError: true))
            pages++;
        Assert.Equal(0, pages);
#pragma warning restore CS0618
    }

    [Fact]
    public async Task PageLimit_ThrowsInsteadOfTruncatingSilently()
    {
        var handler = StubHandler.Json("""{"result":{"data":[1],"total":5000}}""");
        using var client = handler.CreateClient(config => config.MaxPages = 3);
        var received = 0;

        var exception = await Assert.ThrowsAsync<DaktelaPaginationLimitException>(async () =>
        {
            await foreach (var _ in client.IterateAsync<int>("users", pageSize: 1))
                received++;
        });

        Assert.Equal(3, exception.MaxPages);
        Assert.Equal(3, received);
        Assert.Equal(3, handler.Urls.Count);
    }

    [Fact]
    public async Task PageLimit_DoesNotThrowWhenResultCompletesWithinLimit()
    {
        var handler = new StubHandler(request =>
            StubHandler.Response("{\"result\":{\"data\":[" + request + "],\"total\":3}}"));
        using var client = handler.CreateClient(config => config.MaxPages = 3);

        var items = await client.Paginate<int>("users", pageSize: 1).ToListAsync();

        Assert.Equal(new[] { 1, 2, 3 }, items);
    }

    [Fact]
    public async Task PageLimit_DoesNotApplyWhenMaxItemsEndsTheRead()
    {
        var handler = StubHandler.Json("""{"result":{"data":[1],"total":5000}}""");
        using var client = handler.CreateClient(config => config.MaxPages = 3);

        var items = await client.Paginate<int>("users", pageSize: 1, maxItems: 3).ToListAsync();

        Assert.Equal(3, items.Count);
    }

    [Fact]
    public void MaxPages_MustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => StubHandler.Json("{}").CreateClient(config => config.MaxPages = 0));
    }

    [Fact]
    public async Task Paginator_CanBeBuiltOverTheClientInterface()
    {
        var handler = StubHandler.Json("""{"result":{"data":[1,2],"total":2}}""");
        using IDaktelaClient client = handler.CreateClient();

        var paginator = new DaktelaPaginator<int>(client, "users");

        Assert.Equal(new[] { 1, 2 }, await paginator.ToListAsync());
    }
}
