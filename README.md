# Daktela V6 .NET Connector

A typed .NET client for the Daktela V6 API. The connector handles the `/api/v6/`
base path, Daktela response envelopes, query encoding, pagination, date/time values,
structured API errors, cancellation, and optional retries.

Protocol details are documented in the
[Daktela V6 API reference](https://customer.daktela.com/external/apihelp/v6/).

## Requirements

- .NET 8 or later (the package contains .NET 8 and .NET 10 targets)

## Installation

```bash
dotnet add package Daktela.Connector
```

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
    RetryPolicy = RetryPolicy.Default
};
```

The authentication methods are:

- `Header` (default): `X-AUTH-TOKEN: {token}`
- `QueryParam`: `?accessToken={token}`
- `Cookie`: `Cookie: c_user={token}`

Keep `VerifySsl` enabled in production. It applies when the connector creates its
own HTTP transport.

### HttpClientFactory and dependency injection

Supply a managed `HttpClient` when your application already uses
`IHttpClientFactory`. The caller retains ownership, so disposing the Daktela client
does not dispose the supplied HTTP client.

```csharp
var httpClient = httpClientFactory.CreateClient("daktela");
using var client = new DaktelaClient(config, httpClient);
```

`DaktelaConfig.Timeout` still applies to an injected client.

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

`PostAsync`, `PutAsync`, and `DeleteAsync` also accept a `QueryBuilder` before the
cancellation token when an endpoint needs query options.

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
encoded with Daktela/PHP bracket notation.

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

`IterateAsync` respects an existing `Skip` and `Take`, stops at the response total,
and accepts a cancellation token.

## JSON and Daktela date/time values

The client reads both ISO 8601 values and Daktela's `yyyy-MM-dd HH:mm:ss` format.
It writes `DateTime` and `DateTimeOffset` values in Daktela format. Property names
are matched case-insensitively and request properties default to camel case.
Daktela values without an explicit offset are server-local wall-clock values and
deserialize with `DateTimeKind.Unspecified`.

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
    RetryOnTimeout = true
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
`StatusCode`, `IsSuccess`, `Errors`, and the original `RawResponse`.

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
