using System.Net;
using System.Text;
using System.Text.Json;
using Daktela.Connector.Exceptions;
using Daktela.Connector.Http;
using Daktela.Connector.Query;
using Xunit;

namespace Daktela.Connector.Tests;

public class ProtocolTests
{
    [Fact]
    public async Task GetList_UsesApiV6PathAndReadsDaktelaEnvelope()
    {
        using var handler = RecordingHandler.ReturnJson(
            """{"result":{"data":[{"name":"alice","created":"2026-01-15 14:34:58"}],"total":"12"}}""");
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var response = await client.GetAsync<UserDto>("users");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://tenant.daktela.test/api/v6/users.json", request.Uri.AbsoluteUri);
        Assert.Equal("secret-token", Assert.Single(request.Headers["X-AUTH-TOKEN"]));
        Assert.Equal("application/json", Assert.Single(request.Headers["Accept"]));
        var user = Assert.Single(Assert.IsType<List<UserDto>>(response.Data));
        Assert.Equal("alice", user.Name);
        Assert.Equal(new DateTime(2026, 1, 15, 14, 34, 58), user.Created);
        Assert.Equal(12, response.Total);
        Assert.Equal(200, response.StatusCode);
        Assert.Empty(Assert.IsType<List<DaktelaError>>(response.Errors));
    }

    [Fact]
    public async Task BaseUrlAlreadyContainingApiPath_IsNotDuplicated()
    {
        using var handler = RecordingHandler.ReturnJson("""{"result":{"data":[]}}""");
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient, "https://tenant.daktela.test/api/v6/");

        await client.GetAsync<UserDto>("users");

        Assert.Equal(
            "https://tenant.daktela.test/api/v6/users.json",
            Assert.Single(handler.Requests).Uri.AbsoluteUri);
    }

    [Fact]
    public async Task Endpoint_FirstCharacterIsLowercased()
    {
        using var handler = RecordingHandler.ReturnJson("""{"result":{"data":[]}}""");
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        await client.GetAsync<UserDto>("CampaignsRecords");

        Assert.EndsWith(
            "/api/v6/campaignsRecords.json",
            Assert.Single(handler.Requests).Uri.AbsoluteUri);
    }

    [Fact]
    public async Task GetSingle_ReadsNestedDataAndEscapesRecordId()
    {
        using var handler = RecordingHandler.ReturnJson(
            """{"result":{"data":{"name":"alice"}}}""");
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var response = await client.GetAsync<UserDto>("users.json", "alice@example.com");

        Assert.Equal("alice", response.Data?.Name);
        Assert.EndsWith(
            "/api/v6/users/alice%40example.com.json",
            Assert.Single(handler.Requests).Uri.AbsoluteUri);
    }

    [Fact]
    public async Task SuccessfulUnwrappedObject_RemainsSupported()
    {
        using var handler = RecordingHandler.ReturnJson("""{"name":"unwrapped"}""");
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var response = await client.GetAsync<UserDto>("users", "one");

        Assert.Equal("unwrapped", response.Data?.Name);
    }

    [Fact]
    public async Task Post_SerializesCamelCaseAndDaktelaDate()
    {
        using var handler = RecordingHandler.ReturnJson(
            """{"result":{"data":{"name":"created"}}}""");
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var response = await client.PostAsync<UserDto>("contacts", new
        {
            DisplayName = "created",
            Created = new DateTime(2026, 2, 3, 4, 5, 6)
        });

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://tenant.daktela.test/api/v6/contacts.json", request.Uri.AbsoluteUri);
        Assert.Equal("application/json; charset=utf-8", request.ContentType);
        Assert.Contains("\"displayName\":\"created\"", request.Body);
        Assert.Contains("\"created\":\"2026-02-03 04:05:06\"", request.Body);
        Assert.Equal("created", response.Data?.Name);
    }

    [Fact]
    public async Task Post_SerializesDateTimeOffsetWithoutUnsupportedOffsetSuffix()
    {
        using var handler = RecordingHandler.ReturnJson("""{"result":{"data":{}}}""");
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        await client.PostAsync<UserDto>("contacts", new
        {
            Scheduled = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.FromHours(1))
        });

        Assert.Contains(
            "\"scheduled\":\"2026-02-03 04:05:06\"",
            Assert.Single(handler.Requests).Body);
    }

    [Fact]
    public async Task RawPost_ReturnsDetachedJsonWithoutResponseDto()
    {
        using var handler = RecordingHandler.ReturnJson(
            """{"result":{"data":{"id":"created"}}}""");
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var response = await client.PostAsync("Contacts", new { Name = "Alice" });

        Assert.Equal("created", response.Data.GetProperty("id").GetString());
        Assert.Equal(HttpMethod.Post, Assert.Single(handler.Requests).Method);
        Assert.EndsWith("/api/v6/contacts.json", handler.Requests[0].Uri.AbsoluteUri);
    }

    [Fact]
    public async Task Put_UsesRecordPathAndReturnsNestedData()
    {
        using var handler = RecordingHandler.ReturnJson(
            """{"result":{"data":{"name":"updated"}}}""");
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var response = await client.PutAsync<UserDto>(
            "contacts",
            "record/with/slash",
            new { Name = "updated" });

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.EndsWith(
            "/api/v6/contacts/record%2Fwith%2Fslash.json",
            request.Uri.AbsoluteUri);
        Assert.Equal("updated", response.Data?.Name);
    }

    [Fact]
    public async Task RawPut_SupportsCombinedAndSplitRecordPaths()
    {
        using var handler = RecordingHandler.ReturnJson(
            """{"result":{"data":{"name":"updated"}}}""");
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var combined = await client.PutAsync("Contacts/record-1", new { Name = "updated" });
        var split = await client.PutAsync("Contacts", "record-2", new { Name = "updated" });

        Assert.Equal("updated", combined.Data.GetProperty("name").GetString());
        Assert.Equal("updated", split.Data.GetProperty("name").GetString());
        Assert.EndsWith("/api/v6/contacts/record-1.json", handler.Requests[0].Uri.AbsoluteUri);
        Assert.EndsWith("/api/v6/contacts/record-2.json", handler.Requests[1].Uri.AbsoluteUri);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Put, request.Method));
    }

    [Fact]
    public async Task Delete_HandlesEmptySuccessBodyAndDisposesResponse()
    {
        var content = new TrackingContent(string.Empty);
        using var handler = new RecordingHandler((_, _, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.NoContent) { Content = content }));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var response = await client.DeleteAsync("contacts", "123");

        Assert.Equal(204, response.StatusCode);
        Assert.True(response.IsSuccess);
        Assert.True(content.WasDisposed);
        Assert.Equal(HttpMethod.Delete, Assert.Single(handler.Requests).Method);
    }

    [Fact]
    public async Task Delete_SupportsCombinedRecordPath()
    {
        using var handler = new RecordingHandler((_, _, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.NoContent)));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var response = await client.DeleteAsync("Contacts/record-1");

        Assert.True(response.IsSuccess);
        Assert.Equal(HttpMethod.Delete, Assert.Single(handler.Requests).Method);
        Assert.EndsWith("/api/v6/contacts/record-1.json", handler.Requests[0].Uri.AbsoluteUri);
    }

    [Fact]
    public async Task QueryBuilder_IsSentUsingDocumentedWireNames()
    {
        using var handler = RecordingHandler.ReturnJson("""{"result":{"data":[]}}""");
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);
        var query = new QueryBuilder()
            .Filter("status", FilterOperator.NotIn, new[] { "deleted", "archived" })
            .Sort("created", SortDirection.Desc)
            .Take(10);

        await client.GetAsync<UserDto>("users", query);

        var queryString = Uri.UnescapeDataString(Assert.Single(handler.Requests).Uri.Query);
        Assert.Contains("filter[logic]=and", queryString);
        Assert.Contains("filter[filters][0][operator]=notin", queryString);
        Assert.Contains("filter[filters][0][value][0]=deleted", queryString);
        Assert.Contains("sort[0][dir]=desc", queryString);
        Assert.Contains("take=10", queryString);
    }

    [Fact]
    public async Task UntypedGet_SendsArbitraryNestedParameters()
    {
        using var handler = RecordingHandler.ReturnJson(
            """{"result":{"data":{"accepted":true},"total":1}}""");
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);
        IReadOnlyDictionary<string, object?> parameters = new Dictionary<string, object?>
        {
            ["custom"] = "hello world",
            ["options"] = new Dictionary<string, object?>
            {
                ["active"] = true,
                ["ids"] = new[] { 4, 7 }
            }
        };

        var response = await client.GetAsync("custom/action", parameters);

        var queryString = Uri.UnescapeDataString(Assert.Single(handler.Requests).Uri.Query)
            .Replace('+', ' ');
        Assert.Contains("custom=hello world", queryString);
        Assert.Contains("options[active]=1", queryString);
        Assert.Contains("options[ids][0]=4", queryString);
        Assert.True(response.Data.GetProperty("accepted").GetBoolean());
        Assert.Equal(1, response.Total);
    }

    [Theory]
    [InlineData(AuthMethod.QueryParam)]
    [InlineData(AuthMethod.Cookie)]
    public async Task AlternativeAuthenticationMethods_AreApplied(AuthMethod authMethod)
    {
        using var handler = RecordingHandler.ReturnJson("""{"result":{"data":[]}}""");
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient, authMethod: authMethod);

        await client.GetAsync<UserDto>("users");

        var request = Assert.Single(handler.Requests);
        Assert.False(request.Headers.ContainsKey("X-AUTH-TOKEN"));
        if (authMethod == AuthMethod.QueryParam)
        {
            Assert.Contains("accessToken=secret-token", request.Uri.Query);
        }
        else
        {
            Assert.Equal("c_user=secret-token", Assert.Single(request.Headers["Cookie"]));
        }
    }

    [Fact]
    public async Task UserAgent_UsesAssemblyVersionAndConfiguredSuffix()
    {
        using var handler = RecordingHandler.ReturnJson("""{"result":{"data":[]}}""");
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient, userAgentSuffix: "MyApp/2.0");

        await client.GetAsync<UserDto>("users");

        var version = typeof(DaktelaClient).Assembly.GetName().Version!;
        Assert.Equal(
            $"Daktela.Connector/{version.Major}.{version.Minor}.{version.Build} MyApp/2.0",
            string.Join(' ', handler.Requests[0].Headers["User-Agent"]));
    }

    [Fact]
    public async Task Ping_UsesAuthenticatedWhoimEndpoint()
    {
        using var handler = RecordingHandler.ReturnJson("""{"result":{"data":{"name":"agent"}}}""");
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var healthy = await client.PingAsync();

        Assert.True(healthy);
        Assert.EndsWith("/api/v6/whoim.json", Assert.Single(handler.Requests).Uri.AbsoluteUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public async Task HealthCheck_ReturnsStatusAndLatency(HttpStatusCode statusCode, bool healthy)
    {
        using var handler = RecordingHandler.ReturnJson("{}", statusCode);
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var result = await client.HealthCheckAsync();

        Assert.Equal(healthy, result.Healthy);
        Assert.Equal((int)statusCode, result.StatusCode);
        Assert.True(result.Latency >= TimeSpan.Zero);
        Assert.Equal(healthy, result.Error is null);
        Assert.EndsWith("/api/v6/whoim.json", Assert.Single(handler.Requests).Uri.AbsoluteUri);
    }

    [Fact]
    public async Task HealthCheck_ReportsConnectionFailureWithoutThrowing()
    {
        using var handler = new RecordingHandler((_, _, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("offline")));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var result = await client.HealthCheckAsync();

        Assert.False(result.Healthy);
        Assert.Null(result.StatusCode);
        Assert.Contains("offline", result.Error);
    }

    [Fact]
    public async Task SingularErrorArray_PopulatesValidationException()
    {
        using var handler = RecordingHandler.ReturnJson(
            """{"result":false,"error":["Name is required"]}""",
            HttpStatusCode.BadRequest);
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<DaktelaValidationException>(
            () => client.PostAsync<UserDto>("contacts", new { }));

        var error = Assert.Single(exception.Errors);
        Assert.Equal("Name is required", error.Message);
        Assert.Equal("Name is required", Assert.Single(exception.ValidationErrors["_global"]));
        Assert.Contains("Name is required", exception.Message);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task NestedErrors_AreFlattenedWithFieldNames()
    {
        using var handler = RecordingHandler.ReturnJson(
            """{"errors":{"contact":{"email":["Invalid email"]}}}""",
            HttpStatusCode.UnprocessableEntity);
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<DaktelaValidationException>(
            () => client.PostAsync<UserDto>("contacts", new { }));

        Assert.Equal("Invalid email", Assert.Single(exception.ValidationErrors["contact.email"]));
        Assert.Equal(422, exception.StatusCode);
    }

    [Fact]
    public async Task RateLimitError_ParsesRetryAfterAndStructuredError()
    {
        using var handler = new RecordingHandler((_, _, _) =>
        {
            var response = JsonResponse(
                """{"error":{"message":"Slow down","code":"RATE_LIMIT"}}""",
                HttpStatusCode.TooManyRequests);
            response.Headers.TryAddWithoutValidation("Retry-After", "15");
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<DaktelaRateLimitException>(
            () => client.GetAsync<UserDto>("users"));

        Assert.Equal(TimeSpan.FromSeconds(15), exception.RetryAfter);
        Assert.Equal("RATE_LIMIT", Assert.Single(exception.Errors).Code);
        Assert.Equal("Slow down", Assert.Single(exception.Errors).Message);
    }

    [Fact]
    public async Task RateLimitPolicy_RetriesExplicitlyConfiguredPost()
    {
        using var handler = new RecordingHandler((attempt, _, _) =>
        {
            if (attempt > 1)
                return Task.FromResult(JsonResponse("""{"result":{"data":{"name":"created"}}}"""));

            var response = JsonResponse("{}", HttpStatusCode.TooManyRequests);
            response.Headers.TryAddWithoutValidation("Retry-After", "0");
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient, rateLimitPolicy: ImmediateRateLimitPolicy());

        var response = await client.PostAsync<UserDto>("contacts", new { Name = "created" });

        Assert.Equal("created", response.Data?.Name);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task RateLimitPolicy_DoesNotWaitPastConfiguredMaximum()
    {
        using var handler = new RecordingHandler((_, _, _) =>
        {
            var response = JsonResponse("{}", HttpStatusCode.TooManyRequests);
            response.Headers.TryAddWithoutValidation("Retry-After", "10");
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient, rateLimitPolicy: ImmediateRateLimitPolicy());

        var exception = await Assert.ThrowsAsync<DaktelaRateLimitException>(
            () => client.GetAsync<UserDto>("users"));

        Assert.Equal(TimeSpan.FromSeconds(10), exception.RetryAfter);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, typeof(DaktelaUnauthorizedException))]
    [InlineData(HttpStatusCode.NotFound, typeof(DaktelaNotFoundException))]
    public async Task StatusErrors_MapToTypedExceptions(
        HttpStatusCode statusCode,
        Type exceptionType)
    {
        using var handler = RecordingHandler.ReturnJson(
            """{"error":"Request rejected"}""",
            statusCode);
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAnyAsync<DaktelaException>(
            () => client.GetAsync<UserDto>("users"));

        Assert.IsType(exceptionType, exception);
        Assert.Equal("Request rejected", Assert.Single(exception.Errors).Message);
    }

    [Fact]
    public async Task MalformedSuccessJson_ThrowsInsteadOfReturningEmptyData()
    {
        using var handler = RecordingHandler.ReturnJson("not-json");
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<DaktelaException>(
            () => client.GetAsync<UserDto>("users"));

        Assert.Contains("malformed JSON", exception.Message);
        Assert.Equal("not-json", exception.ResponseBody);
    }

    [Fact]
    public async Task RetryPolicy_RetriesGetAndDisposesFailedResponse()
    {
        var failedContent = new TrackingContent("""{"error":"temporary"}""");
        using var handler = new RecordingHandler((attempt, _, _) => Task.FromResult(
            attempt == 1
                ? JsonResponse(failedContent, HttpStatusCode.ServiceUnavailable)
                : JsonResponse("""{"result":{"data":[]}}""")));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient, retryPolicy: ImmediateRetryPolicy());

        var response = await client.GetAsync<UserDto>("users");

        Assert.True(response.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
        Assert.True(failedContent.WasDisposed);
    }

    [Fact]
    public async Task UnsafeRequest_IsNotRetriedByDefault()
    {
        using var handler = new RecordingHandler((_, _, _) => Task.FromResult(
            JsonResponse("""{"error":"temporary"}""", HttpStatusCode.ServiceUnavailable)));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient, retryPolicy: ImmediateRetryPolicy());

        await Assert.ThrowsAsync<DaktelaException>(
            () => client.PostAsync<UserDto>("contacts", new { Name = "duplicate risk" }));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task UnsafeRequest_CanBeRetriedExplicitly()
    {
        using var handler = new RecordingHandler((attempt, _, _) => Task.FromResult(
            attempt == 1
                ? JsonResponse("""{"error":"temporary"}""", HttpStatusCode.ServiceUnavailable)
                : JsonResponse("""{"result":{"data":{"name":"created"}}}""")));
        using var httpClient = new HttpClient(handler);
        var retryPolicy = ImmediateRetryPolicy();
        retryPolicy.RetryUnsafeHttpMethods = true;
        using var client = CreateClient(httpClient, retryPolicy: retryPolicy);

        var response = await client.PostAsync<UserDto>("contacts", new { Name = "created" });

        Assert.Equal("created", response.Data?.Name);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task NetworkFailure_IsRetriedForGet()
    {
        using var handler = new RecordingHandler((attempt, _, _) =>
            attempt == 1
                ? Task.FromException<HttpResponseMessage>(new HttpRequestException("temporary"))
                : Task.FromResult(JsonResponse("""{"result":{"data":[]}}""")));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient, retryPolicy: ImmediateRetryPolicy());

        var response = await client.GetAsync<UserDto>("users");

        Assert.True(response.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task NetworkFailure_IsNotRetriedWhenDisabled()
    {
        using var handler = new RecordingHandler((_, _, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("offline")));
        using var httpClient = new HttpClient(handler);
        var policy = ImmediateRetryPolicy();
        policy.RetryOnConnectionError = false;
        using var client = CreateClient(httpClient, retryPolicy: policy);

        await Assert.ThrowsAsync<DaktelaConnectionException>(
            () => client.GetAsync<UserDto>("users"));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Timeout_IsRetriedForGetWhenEnabled()
    {
        using var handler = new RecordingHandler(async (attempt, _, cancellationToken) =>
        {
            if (attempt == 1)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse("""{"result":{"data":[]}}""");
        });
        using var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var client = CreateClient(
            httpClient,
            retryPolicy: ImmediateRetryPolicy(),
            timeout: TimeSpan.FromMilliseconds(25));

        var response = await client.GetAsync<UserDto>("users");

        Assert.True(response.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ConfiguredTimeout_AppliesToInjectedHttpClient()
    {
        using var handler = new RecordingHandler(async (_, _, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse("{}");
        });
        using var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var client = CreateClient(httpClient, timeout: TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAsync<DaktelaTimeoutException>(
            () => client.GetAsync<UserDto>("users"));
    }

    [Fact]
    public async Task CallerCancellation_IsNotReportedAsTimeout()
    {
        using var handler = new RecordingHandler(async (_, _, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse("{}");
        });
        using var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var client = CreateClient(httpClient, timeout: TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetAsync<UserDto>("users", cancellationToken: cancellation.Token));
    }

    [Fact]
    public void DisposingClient_DoesNotDisposeInjectedHttpClient()
    {
        var handler = RecordingHandler.ReturnJson("{}");
        var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        client.Dispose();

        Assert.False(handler.WasDisposed);
        httpClient.Dispose();
        Assert.True(handler.WasDisposed);
    }

    [Fact]
    public async Task Iterator_PagesUsingEnvelopeTotalWithoutOverfetching()
    {
        using var handler = new RecordingHandler((attempt, _, _) => Task.FromResult(
            attempt == 1
                ? JsonResponse("""{"result":{"data":[{"name":"one"},{"name":"two"}],"total":3}}""")
                : JsonResponse("""{"result":{"data":[{"name":"three"}],"total":3}}""")));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);
        var names = new List<string?>();

        await foreach (var item in client.IterateAsync<UserDto>("users", pageSize: 2))
            names.Add(item.Name);

        Assert.Equal(new[] { "one", "two", "three" }, names);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("take=2", handler.Requests[0].Uri.Query);
        Assert.Contains("skip=0", handler.Requests[0].Uri.Query);
        Assert.Contains("take=2", handler.Requests[1].Uri.Query);
        Assert.Contains("skip=2", handler.Requests[1].Uri.Query);
    }

    [Fact]
    public async Task Iterator_ContinuesWhenServerCapsPageBelowRequestedTake()
    {
        using var handler = new RecordingHandler((attempt, _, _) => Task.FromResult(
            attempt == 1
                ? JsonResponse("""{"result":{"data":[{"name":"one"}],"total":3}}""")
                : JsonResponse("""{"result":{"data":[{"name":"two"},{"name":"three"}],"total":3}}""")));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);
        var names = new List<string?>();

        await foreach (var item in client.IterateAsync<UserDto>("users", pageSize: 2))
            names.Add(item.Name);

        Assert.Equal(new[] { "one", "two", "three" }, names);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("skip=1", handler.Requests[1].Uri.Query);
    }

    [Fact]
    public async Task Iterator_DoesNotTreatDefaultTotalAsPaginationBoundary()
    {
        using var handler = new RecordingHandler((attempt, _, _) => Task.FromResult(
            attempt == 1
                ? JsonResponse("""{"result":{"data":[{"name":"one"},{"name":"two"}]}}""")
                : JsonResponse("""{"result":{"data":[]}}""")));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);
        var names = new List<string?>();

        await foreach (var item in client.IterateAsync<UserDto>("users", pageSize: 2))
            names.Add(item.Name);

        Assert.Equal(new[] { "one", "two" }, names);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Paginator_ProvidesCollectionHelpers()
    {
        using var handler = RecordingHandler.ReturnJson(
            """{"result":{"data":[{"name":"one"},{"name":"two"}],"total":2}}""");
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);
        var paginator = client.Paginate<UserDto>("Users", pageSize: 2);

        Assert.Equal(2, (await paginator.ToListAsync()).Count);
        Assert.Equal(2, await paginator.CountAsync());
        Assert.Equal("one", (await paginator.FirstOrDefaultAsync())?.Name);
        Assert.False(await paginator.IsEmptyAsync());

        var visited = new List<string?>();
        await paginator.ForEachAsync((item, index) => visited.Add($"{index}:{item.Name}"));
        Assert.Equal(new[] { "0:one", "1:two" }, visited);

        var filtered = new List<string?>();
        await foreach (var item in paginator.Filter(item => item.Name == "two"))
            filtered.Add(item.Name);
        Assert.Equal(new[] { "two" }, filtered);

        var mapped = new List<string?>();
        await foreach (var name in paginator.Map(item => item.Name))
            mapped.Add(name);
        Assert.Equal(new[] { "one", "two" }, mapped);
    }

    [Fact]
    public async Task Paginator_CanSkipFailedPageWhenConfigured()
    {
        using var handler = new RecordingHandler((attempt, _, _) => Task.FromResult(
            attempt == 1
                ? JsonResponse("{}", HttpStatusCode.InternalServerError)
                : JsonResponse("""{"result":{"data":[{"name":"recovered"}],"total":3}}""")));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var result = await client.Paginate<UserDto>(
            "users",
            pageSize: 2,
            stopOnError: false).ToListAsync();

        Assert.Equal("recovered", Assert.Single(result).Name);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("skip=2", handler.Requests[1].Uri.Query);
    }

    [Fact]
    public async Task Paginator_StopsOnFailedPageByDefault()
    {
        using var handler = RecordingHandler.ReturnJson("{}", HttpStatusCode.InternalServerError);
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var result = await client.Paginate<UserDto>("users").ToListAsync();

        Assert.Empty(result);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("https://evil.example/users")]
    [InlineData("../users")]
    [InlineData("%2e%2e/users")]
    [InlineData("users?take=1")]
    [InlineData("users#fragment")]
    public async Task InvalidEndpoint_IsRejected(string endpoint)
    {
        using var handler = RecordingHandler.ReturnJson("{}");
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetAsync<UserDto>(endpoint));
        Assert.Empty(handler.Requests);
    }

    private static DaktelaClient CreateClient(
        HttpClient httpClient,
        string instanceUrl = "https://tenant.daktela.test",
        AuthMethod authMethod = AuthMethod.Header,
        RetryPolicy? retryPolicy = null,
        TimeSpan? timeout = null,
        RateLimitPolicy? rateLimitPolicy = null,
        string? userAgentSuffix = null)
        => new(new DaktelaConfig
        {
            InstanceUrl = instanceUrl,
            AccessToken = "secret-token",
            AuthMethod = authMethod,
            RetryPolicy = retryPolicy,
            RateLimitPolicy = rateLimitPolicy,
            UserAgentSuffix = userAgentSuffix,
            Timeout = timeout ?? TimeSpan.FromSeconds(2)
        }, httpClient);

    private static RetryPolicy ImmediateRetryPolicy() => new()
    {
        MaxRetries = 1,
        InitialDelay = TimeSpan.Zero,
        MaxDelay = TimeSpan.Zero
    };

    private static RateLimitPolicy ImmediateRateLimitPolicy() => new()
    {
        MaxRetries = 1,
        MaxWait = TimeSpan.Zero,
        DefaultWait = TimeSpan.Zero
    };

    private static HttpResponseMessage JsonResponse(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK)
        => JsonResponse(new StringContent(json, Encoding.UTF8, "application/json"), statusCode);

    private static HttpResponseMessage JsonResponse(
        HttpContent content,
        HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(statusCode) { Content = content };

    private sealed class UserDto
    {
        public string? Name { get; set; }
        public DateTime Created { get; set; }
    }

    private sealed class TrackingContent(string content)
        : StringContent(content, Encoding.UTF8, "application/json")
    {
        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        IReadOnlyDictionary<string, string[]> Headers,
        string Body,
        string? ContentType);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<int, RecordedRequest, CancellationToken, Task<HttpResponseMessage>> _responder;
        private int _attempt;

        public RecordingHandler(
            Func<int, RecordedRequest, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        public List<RecordedRequest> Requests { get; } = new();
        public bool WasDisposed { get; private set; }

        public static RecordingHandler ReturnJson(
            string json,
            HttpStatusCode statusCode = HttpStatusCode.OK)
            => new((_, _, _) => Task.FromResult(JsonResponse(json, statusCode)));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var headers = request.Headers
                .ToDictionary(header => header.Key, header => header.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
            var recorded = new RecordedRequest(
                request.Method,
                request.RequestUri!,
                headers,
                body,
                request.Content?.Headers.ContentType?.ToString());
            Requests.Add(recorded);
            return await _responder(Interlocked.Increment(ref _attempt), recorded, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
