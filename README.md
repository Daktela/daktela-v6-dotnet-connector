# Daktela V6 .NET Connector

A typed .NET client for the Daktela V6 API. The connector handles the `/api/v6/`
base path, Daktela response envelopes, canonical query encoding, pagination,
date/time values, structured API errors, cancellation, retries, rate limits, and
authenticated health checks.

Protocol details are documented in the
[Daktela V6 API reference](https://customer.daktela.com/external/apihelp/v6/).

## Requirements

- .NET 8 or later (the package contains .NET 8 and .NET 10 targets)

## Installation

```bash
dotnet add package Daktela.Connector
```

## Upgrading from 1.2.0

Version 1.3.0 removes no public members, but it stops hiding several failures. Check
these if your application relied on the previous behavior:

- **Paginator failures now throw.** `Paginate` and `IteratePagesAsync` previously ended
  the enumeration silently when a page request failed, so an expired token looked like an
  empty result. They now throw by default. Pass `PaginationErrorMode.Stop` or
  `PaginationErrorMode.Skip` to opt back in. The overloads taking `bool stopOnError`
  still work when every argument is supplied and are marked obsolete; a call that names
  `stopOnError:` while omitting earlier arguments no longer compiles and should switch to
  `errorMode:`.
- **The page limit now throws.** Reaching the 999-page limit used to end a read as if it
  were complete. It now throws `DaktelaPaginationLimitException`; raise
  `DaktelaConfig.MaxPages` for larger reads.
- **Configuration is captured at construction.** Changing `AccessToken`, `AuthMethod`, or
  `UserAgentSuffix` on a `DaktelaConfig` after creating a client no longer affects that
  client. Create a new client to rotate a token.
- **Invalid header values fail at construction.** A `UserAgentSuffix` or access token that
  cannot be written as an HTTP header throws `ArgumentException` from the constructor
  instead of `FormatException` from every request.
- **HTTP 403 throws `DaktelaForbiddenException`**, which derives from `DaktelaException`.
- **The request timeout covers the response body**, and a connection failure while
  reading the body throws `DaktelaConnectionException` and follows the retry policy.
- **The connector's own HTTP transport no longer stores cookies** and recycles pooled
  connections every five minutes. An injected `HttpClient` is unaffected.
- `Filter.In` / `Filter.NotIn` given one collection (`int[]`, `List<string>`, ...) now
  encode its elements instead of a nested list.
- Empty-string and `0000-00-00` date values deserialize to null for `DateTime?` and
  `DateTimeOffset?` properties.

- `DaktelaConfig.AccessToken` is no longer a `required` member, because a client can log
  in with `DaktelaConfig.Login` instead. Exactly one of the two must be set.
- Secrets are redacted from exceptions; see [Secrets in errors](#secrets-in-errors).

New options are `Login`, `ServerTimeZone`, `IncludeNullValues`, and `MaxPages`, and the
client now implements `IDaktelaClient`. The full list is in the
[changelog](https://github.com/Daktela/daktela-v6-dotnet-connector/blob/main/CHANGELOG.md).

## Upgrading from 1.1.0

Version 1.2.0 substantially expands the connector without removing or renaming
existing .NET members. Two corrected wire behaviors are worth checking if your
application worked around them:

- Filters now always use Daktela's canonical
  `filter[logic]` / `filter[filters][n]` structure, including a single filter.
- The first character of an endpoint is lowercased, matching the Daktela examples
  (`CampaignsRecords` becomes `campaignsRecords`).

The release also adds untyped POST/PUT results, combined-path PUT/DELETE overloads,
reusable paginator helpers, detailed health checks, dedicated rate-limit handling,
connection-retry control, a User-Agent suffix, response helpers, and phone-number
normalization. The connector retains async streams, typed DTOs, cancellation, and
`HttpClient` ownership conventions throughout the expanded API.

## Upgrading from 1.0.1

Version 1.1.0 does not intentionally remove or rename public API members, but it
corrects several behaviors that applications may have worked around. Review these
changes before upgrading:

- `InstanceUrl` is treated as the Daktela host or API root, and requests always use
  the fixed `/api/v6/` base path.
- Daktela response envelopes are unwrapped from `result.data`; the nested `total`
  value is also exposed through `DaktelaResponse.Total`.
- A successful HTTP response containing malformed or incompatible JSON now throws
  `DaktelaException` instead of silently returning an empty/default result.
- GET, HEAD, and OPTIONS can be retried. POST, PUT, DELETE, and other unsafe methods
  are no longer retried unless `RetryUnsafeHttpMethods` is explicitly enabled.
- Endpoint arguments must be relative endpoint paths and cannot contain a query
  string. Add query values with `QueryBuilder` or its `Parameter` method.
- Configuration and endpoint validation is stricter, so invalid URLs, timeouts,
  retry settings, record identifiers, and path traversal fail before a request is
  sent.
- Query encoding now uses Daktela's `dir` sort key, lowercase `notin` operator, and
  bracket notation for nested AND/OR filters and structured parameters.
- Daktela date/time strings without an offset represent the Daktela server's local
  time and deserialize with `DateTimeKind.Unspecified`; they are not converted to
  UTC automatically.

If an application depended on one of the previous behaviors, update that code when
moving from 1.0.1 to 1.1.0.

## Quick start

```csharp
using Daktela.Connector;
using Daktela.Connector.Query;

var config = new DaktelaConfig
{
    // A host URL or a URL ending in /api/v6 are both accepted.
    InstanceUrl = "https://your-instance.daktela.com",
    AccessToken = "your-access-token"
};

using var client = new DaktelaClient(config);

if (await client.PingAsync())
{
    var query = new QueryBuilder()
        .Fields("name", "email", "created")
        .Filter("status", FilterOperator.Eq, "active")
        .Sort("created", SortDirection.Desc)
        .Take(50);

    var response = await client.GetAsync<User>("users", query);
    foreach (var user in response.Data ?? [])
        Console.WriteLine(user.Name);
}
```

Endpoint names are relative to the Daktela API root. Pass `users` or `users.json`,
not a full URL; the connector adds `.json` and `/api/v6/` as needed.

## Configuration

```csharp
using Daktela.Connector.Http;

var config = new DaktelaConfig
{
    InstanceUrl = "https://your-instance.daktela.com/api/v6/",
    AccessToken = "your-access-token",
    AuthMethod = AuthMethod.Header,
    Timeout = TimeSpan.FromSeconds(30),
    VerifySsl = true,
    RetryPolicy = RetryPolicy.Default,
    RateLimitPolicy = new RateLimitPolicy(),
    UserAgentSuffix = "MyApplication/2.0",
    ServerTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague"),
    IncludeNullValues = false,
    MaxPages = 999
};
```

The configuration is read once, when the client is created. `UserAgentSuffix` must be a
valid User-Agent value made of ASCII product tokens such as `MyApp/2.0`.

The authentication methods are:

- `Header` (default): `X-AUTH-TOKEN: {token}`
- `QueryParam`: `?accessToken={token}`
- `Cookie`: `Cookie: c_user={token}`

Keep `VerifySsl` enabled in production. It applies when the connector creates its
own HTTP transport.

### Logging in with a username and password

Use a static access token for integrations where you can. Where you cannot, the connector
can start a session with a username and password instead:

```csharp
using Daktela.Connector.Auth;

var config = new DaktelaConfig
{
    InstanceUrl = "https://your-instance.daktela.com",
    Login = new DaktelaLogin("john.doe", password)
};

using var client = new DaktelaClient(config);
```

Set either `AccessToken` or `Login`, not both. The connector logs in on the first request,
refreshes the short-lived access token a minute before it expires, and logs in again when
the session has ended. When the server answers HTTP 401, the token is renewed and the
request is sent once more; the server answers 401 before it processes a request, so the
request is not executed twice.

Each login starts a new session on the Daktela server. To reuse one session across
clients or processes, pass an `ITokenStore`. `InMemoryTokenStore` shares a session within
a process; implement the interface over your cache or database to share it further:

```csharp
public sealed class FileTokenStore(string path) : ITokenStore
{
    private sealed record Saved(string AccessToken, string RefreshToken, string? Owner);

    public async ValueTask<TokenSet?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            return null;
        var saved = JsonSerializer.Deserialize<Saved>(await File.ReadAllTextAsync(path, cancellationToken));
        return saved == null ? null : new TokenSet(saved.AccessToken, saved.RefreshToken, saved.Owner);
    }

    public async ValueTask SaveAsync(TokenSet tokens, CancellationToken cancellationToken = default)
        => await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(new Saved(tokens.AccessToken, tokens.RefreshToken, tokens.Owner)),
            cancellationToken);
}

config.Login = new DaktelaLogin("john.doe", password, new FileTokenStore("/var/lib/myapp/daktela-tokens.json"));
```

- The password is kept in memory to log in again; the connector never stores it.
- Saved tokens record the user and instance they belong to (`TokenSet.Owner`). Tokens of
  another user or instance are ignored and the connector logs in, so one store can be
  shared safely. A store that throws does not fail the request: the connector logs in when
  loading fails and keeps the tokens in memory when saving fails.
- Refresh tokens are long-lived credentials. Store them as securely as passwords, and use
  an `https://` instance URL with `VerifySsl` enabled.
- After the server rejects the credentials (HTTP 400 or 401), the client sends no further
  login, which could lock the account. Its requests then fail with the status of the
  rejected login; create a new client with valid credentials.
- A client that logs in does not follow redirects on its own transport, because a redirect
  would forward the credentials to its target. Disable automatic redirects on an injected
  `HttpClient` yourself.
- Accounts with two-factor authentication cannot log in this way. Use a static token.

### Secrets in errors

The connector removes the access token, the session's access and refresh tokens, and
(while a login is sent) the password from exception messages, `ResponseBody`, and
`Errors`, in their plain, URL-encoded, and JSON-escaped forms. An underlying exception
that quotes one of them is not chained as `InnerException`. Successful response data is
never altered.

### HttpClientFactory and dependency injection

Supply a managed `HttpClient` when your application already uses
`IHttpClientFactory`. The caller retains ownership, so disposing the Daktela client
does not dispose the supplied HTTP client.

```csharp
var httpClient = httpClientFactory.CreateClient("daktela");
using var client = new DaktelaClient(config, httpClient);
```

`DaktelaConfig.Timeout` still applies to an injected client.

`DaktelaClient` implements `IDaktelaClient`. Register and depend on the interface so the
client can be substituted in tests:

```csharp
services.AddSingleton<IDaktelaClient>(sp =>
    new DaktelaClient(config, sp.GetRequiredService<IHttpClientFactory>().CreateClient("daktela")));
```

## CRUD operations

```csharp
// One record: GET /api/v6/users/john.doe.json
var one = await client.GetAsync<User>("users", "john.doe");

// A list: GET /api/v6/users.json
var users = await client.GetAsync<User>("users");
Console.WriteLine($"Returned: {users.Data?.Count}, total: {users.Total}");

var created = await client.PostAsync<Contact>("contacts", new
{
    firstname = "John",
    lastname = "Doe",
    email = "john.doe@example.com"
});

var updated = await client.PutAsync<Contact>("contacts", "12345", new
{
    email = "john.updated@example.com"
});

var deleted = await client.DeleteAsync("contacts", "12345");
```

Response DTOs are optional for writes. Combined relative paths match the style used
by Daktela's API examples:

```csharp
var rawCreated = await client.PostAsync("contacts", new { name = "John Doe" });
var newName = rawCreated.Data.GetProperty("name").GetString();

var rawUpdated = await client.PutAsync("contacts/12345", new { email = "new@example.com" });
var removed = await client.DeleteAsync("contacts/12345");
```

`PostAsync`, `PutAsync`, and `DeleteAsync` also accept a `QueryBuilder` before the
cancellation token when an endpoint needs query options. The split endpoint/ID PUT
and DELETE overloads URI-escape the ID; a combined path is treated as an already
structured relative API path.

## Query builder

### Fields, filters, sorting, and pagination

```csharp
var query = new QueryBuilder()
    .Fields("name", "email", "status", "created")
    .Filter("status", FilterOperator.Eq, "active")
    .Filter("created", FilterOperator.Gte, DateTime.Today.AddMonths(-1))
    .Filter("role", FilterOperator.In, new[] { "admin", "manager" })
    .Sort("created", SortDirection.Desc)
    .Skip(0)
    .Take(100);

var response = await client.GetAsync<User>("users", query);
```

Supported operators are `Eq`, `Neq`, `Gt`, `Gte`, `Lt`, `Lte`, `Like`,
`Contains`, `StartsWith`, `EndsWith`, `NotLike`, `DoesNotContain`, `IsNull`,
`IsNotNull`, `In`, and `NotIn`. A string overload is also available for operators
introduced by newer server versions:

```csharp
query.Filter("custom_field", "future_operator", "value");
```

Filter helpers provide the same typed operators:

```csharp
var query = new QueryBuilder()
    .Filter(Filter.Eq("status", "active"))
    .Filter(Filter.Like("name", "%john%"))
    .Filter(Filter.In("role", "admin", "manager"));
```

`Filter.In` and `Filter.NotIn` accept either inline values or one collection, such as
`Filter.In("id", ids)` for an `int[]` or `List<int>`.

### AND and OR groups

Top-level filters use AND logic. Use groups for explicit nested logic:

```csharp
var query = new QueryBuilder()
    .Filter("active", FilterOperator.Eq, true)
    .OrFilter(or => or
        .Filter("team", FilterOperator.Eq, "sales")
        .Filter("team", FilterOperator.Eq, "support"));
```

`AndFilter` is available for nested AND groups.

### Arbitrary query parameters

Use `Parameter` for endpoint-specific options. Dictionaries and collections are
encoded with Daktela bracket notation.

```csharp
var query = new QueryBuilder()
    .Parameter("custom", "value")
    .Parameter("options", new Dictionary<string, object?>
    {
        ["active"] = true,
        ["ids"] = new[] { 10, 20 }
    });

var typed = await client.GetAsync<User>("users", query);
```

For an endpoint without a known response model, pass a parameter dictionary to the
untyped overload. Its data is returned as a detached `JsonElement`:

```csharp
var raw = await client.GetAsync("custom/action", new Dictionary<string, object?>
{
    ["custom"] = "value"
});

var property = raw.Data.GetProperty("property").GetString();
```

## Automatic pagination

```csharp
var query = new QueryBuilder()
    .Filter("status", FilterOperator.Eq, "active")
    .Sort("created", SortDirection.Desc);

await foreach (var user in client.IterateAsync<User>("users", query, pageSize: 100))
    Console.WriteLine(user.Name);
```

`IterateAsync` respects an existing `Skip` and `Take`, handles Daktela instances
that cap pages below the requested size, stops at the response total, and accepts a
cancellation token. A failed page request throws.

One pagination operation makes at most `DaktelaConfig.MaxPages` page requests (999 by
default). If the limit is reached while more records may remain, a
`DaktelaPaginationLimitException` is thrown rather than returning a truncated result.

For reusable collection and page helpers, create a paginator:

```csharp
var paginator = client.Paginate<User>(
    "users",
    query,
    pageSize: 100,
    maxItems: 1_000,
    errorMode: PaginationErrorMode.Throw);

var users = await paginator.ToListAsync();
var count = await paginator.CountAsync();
var first = await paginator.FirstOrDefaultAsync();
var empty = await paginator.IsEmptyAsync();

await paginator.ForEachAsync((user, index) => Console.WriteLine($"{index}: {user.Name}"));

await foreach (var active in paginator.Filter(user => user.Status == "active"))
    Console.WriteLine(active.Name);

await foreach (var name in paginator.Map(user => user.Name))
    Console.WriteLine(name);

await foreach (var page in paginator.PagesAsync())
    Console.WriteLine($"Page returned {page.Data?.Count}; total {page.Total}");
```

`ToArrayAsync`, `FirstAsync`, and `EachAsync` are convenience aliases. Each
enumeration performs a fresh set of requests; paginator results are not cached.

`errorMode` decides what happens when a page request fails:

- `Throw` (default) propagates the exception.
- `Stop` ends the enumeration silently, so a failure is indistinguishable from a
  complete result.
- `Skip` skips the failed page and continues. Authentication failures still throw.

## Health checks

`PingAsync` returns a simple boolean. `HealthCheckAsync` returns authenticated
status, latency, HTTP status (when available), and an error description:

```csharp
var health = await client.HealthCheckAsync(cancellationToken);
Console.WriteLine($"Healthy: {health.Healthy}; latency: {health.Latency.TotalMilliseconds:N0} ms");
```

Caller cancellation is propagated rather than converted into an unhealthy result.

## JSON and Daktela date/time values

The client reads both ISO 8601 values and Daktela's `yyyy-MM-dd HH:mm:ss` format.
It writes `DateTime` and `DateTimeOffset` values in Daktela format. Property names
are matched case-insensitively and request properties default to camel case.
Daktela values without an explicit offset are server-local wall-clock values and
deserialize with `DateTimeKind.Unspecified`. An empty string or a `0000-00-00` value
deserializes to null for a nullable `DateTime?` / `DateTimeOffset?` property.

Daktela does not transmit a time zone, so set `ServerTimeZone` when you work with
instants rather than wall-clock values:

```csharp
config.ServerTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
```

With it set, `DateTimeOffset` values and `DateTime` values of kind `Utc` or `Local` are
converted to the server's wall-clock time in request bodies and query filters, and
offset-less values read into a `DateTimeOffset` receive the server's offset. `DateTime`
values of kind `Unspecified` are always sent unchanged. Without it, values are sent as
written and an offset-less value read into a `DateTimeOffset` assumes the time zone of
the machine running the connector, which is wrong whenever that differs from the server.

Null properties of a request body are omitted by default. To clear a field by sending an
explicit `null`, enable `IncludeNullValues`:

```csharp
config.IncludeNullValues = true;
await client.PutAsync("contacts", "12345", new { email = (string?)null });
```

You can supply serializer options; the connector copies them and adds its Daktela
converters:

```csharp
config.JsonSerializerOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
};
```

## Retry behavior

```csharp
config.RetryPolicy = new RetryPolicy
{
    MaxRetries = 3,
    InitialDelay = TimeSpan.FromSeconds(1),
    MaxDelay = TimeSpan.FromSeconds(30),
    BackoffMultiplier = 2.0,
    RetryOnTimeout = true,
    RetryOnConnectionError = true
};
```

Retries honor `Retry-After` for rate limits and cap it at `MaxDelay`. GET, HEAD,
and OPTIONS may be retried. POST, PUT, DELETE, and other potentially unsafe methods
are not retried by default, which prevents accidental duplicate writes. Only enable
`RetryUnsafeHttpMethods` if your operation is idempotent or uses an idempotency key.

```csharp
config.RetryPolicy.RetryUnsafeHttpMethods = true;
```

Use `RetryPolicy.NoRetry` or leave `RetryPolicy` null to disable retries.
`RetryPolicy.Aggressive` provides a high-resilience preset.

### Dedicated rate-limit handling

Use `RateLimitPolicy` when HTTP 429 responses should follow `Retry-After`
independently from ordinary transient-failure retries:

```csharp
config.RateLimitPolicy = new RateLimitPolicy
{
    AutoRetry = true,
    MaxRetries = 3,
    MaxWait = TimeSpan.FromSeconds(60),
    DefaultWait = TimeSpan.FromSeconds(5)
};
```

Configuring this policy explicitly permits a rate-limited POST/PUT/DELETE to be
retried. Only enable automatic retry where repeating the write is acceptable. If
the server's wait exceeds `MaxWait`, or retries are disabled/exhausted,
`DaktelaRateLimitException` is thrown without sleeping.

## Error handling

Non-success HTTP responses throw typed exceptions. Both Daktela's singular `error`
and plural `errors` payloads are parsed into `DaktelaException.Errors`.

```csharp
using Daktela.Connector.Exceptions;

try
{
    await client.GetAsync<User>("users", "non-existent");
}
catch (DaktelaUnauthorizedException)
{
    // HTTP 401
}
catch (DaktelaForbiddenException)
{
    // HTTP 403
}
catch (DaktelaNotFoundException)
{
    // HTTP 404
}
catch (DaktelaRateLimitException ex)
{
    Console.WriteLine($"Retry after: {ex.RetryAfter}");
}
catch (DaktelaValidationException ex)
{
    // HTTP 400 or 422. Non-field errors use the _global key.
    foreach (var (field, messages) in ex.ValidationErrors)
        Console.WriteLine($"{field}: {string.Join(", ", messages)}");
}
catch (DaktelaTimeoutException)
{
    // The configured request timeout elapsed.
}
catch (DaktelaConnectionException ex)
{
    Console.WriteLine(ex.Message);
}
catch (DaktelaException ex)
{
    Console.WriteLine($"HTTP {ex.StatusCode}: {ex.Message}");
    Console.WriteLine(ex.ResponseBody);
}
```

Successful calls return `DaktelaResponse<T>`, containing `Data`, `Total`,
`StatusCode`, `IsSuccess`, `Errors`, and the original `RawResponse`. Convenience
helpers are exposed as `HasErrors`, `FirstError`, and `IsEmpty`.

## Phone-number normalization

Phone normalization is available under `Daktela.Connector.Utils`:

```csharp
using Daktela.Connector.Utils;

var international = FormatHelper.GetNormalizedPhoneNumber("773 794 604");
// 00420773794604

var withPlus = FormatHelper.GetNormalizedPhoneNumber("773794604", plusSign: true);
// +420773794604
```

Use `intlPrefix` and `intlLength` for a country other than the Czech Republic.

## Feature coverage

The connector covers typed and raw CRUD, single/list/all and relational paths,
fields/filters/sorts/additional query parameters, reusable pagination, header/query
authentication, timeouts, SSL verification, custom HTTP transport, retry and
rate-limit policies, health checks,
User-Agent customization, structured responses/errors, and phone formatting.

The API is idiomatic for .NET: direct async methods, `IAsyncEnumerable<T>`, typed
DTOs, cancellation tokens, and an injected `HttpClient`/delegating-handler pipeline
for custom transport behavior and standard application logging.

## Cancellation

```csharp
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var response = await client.GetAsync<User>(
    "users",
    cancellationToken: cancellation.Token);
```

Caller cancellation remains an `OperationCanceledException`; expiration of the
configured request timeout throws `DaktelaTimeoutException`.

## Models

Define models matching the fields returned by your Daktela instance:

```csharp
public sealed class User
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Status { get; set; }
    public DateTime Created { get; set; }
}

public sealed class Contact
{
    public string? Name { get; set; }
    public string? Firstname { get; set; }
    public string? Lastname { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}
```

## License

[MIT](https://github.com/Daktela/daktela-v6-dotnet-connector/blob/main/LICENSE)
