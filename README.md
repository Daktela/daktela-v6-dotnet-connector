# Daktela V6 .NET Connector

Official .NET connector for the Daktela V6 API.

## Installation

```bash
dotnet add package Daktela.Connector
```

## Quick Start

```csharp
using Daktela.Connector;
using Daktela.Connector.Query;

// Create a client
var config = new DaktelaConfig
{
    InstanceUrl = "your-instance.daktela.com",
    AccessToken = "your-access-token"
};

using var client = new DaktelaClient(config);

// Check connection
if (await client.PingAsync())
{
    Console.WriteLine("Connected to Daktela API");
}
```

## Configuration Options

```csharp
var config = new DaktelaConfig
{
    // Required
    InstanceUrl = "your-instance.daktela.com",
    AccessToken = "your-access-token",

    // Optional
    AuthMethod = AuthMethod.Header,          // Header (default), QueryParam, or Cookie
    Timeout = TimeSpan.FromSeconds(30),      // Request timeout
    VerifySsl = true,                        // SSL certificate verification
    RetryPolicy = RetryPolicy.Default        // Retry with exponential backoff
};
```

### Authentication Methods

The connector supports three authentication methods:

```csharp
// Header authentication (recommended)
config.AuthMethod = AuthMethod.Header;    // X-AUTH-TOKEN: {token}

// Query parameter authentication
config.AuthMethod = AuthMethod.QueryParam; // ?accessToken={token}

// Cookie authentication
config.AuthMethod = AuthMethod.Cookie;     // Cookie: c_user={token}
```

## CRUD Operations

### Get a Single Record

```csharp
var response = await client.GetAsync<User>("users", "john.doe");

if (response.IsSuccess)
{
    Console.WriteLine($"User: {response.Data.Name}");
}
```

### Get Multiple Records

```csharp
var response = await client.GetAsync<User>("users");

foreach (var user in response.Data)
{
    Console.WriteLine($"User: {user.Name}");
}

Console.WriteLine($"Total users: {response.Total}");
```

### Create a Record

```csharp
var newContact = new
{
    firstname = "John",
    lastname = "Doe",
    email = "john.doe@example.com"
};

var response = await client.PostAsync<Contact>("contacts", newContact);

if (response.IsSuccess)
{
    Console.WriteLine($"Created contact: {response.Data.Name}");
}
```

### Update a Record

```csharp
var updates = new
{
    email = "john.updated@example.com"
};

var response = await client.PutAsync<Contact>("contacts", "12345", updates);
```

### Delete a Record

```csharp
var response = await client.DeleteAsync("contacts", "12345");

if (response.IsSuccess)
{
    Console.WriteLine("Contact deleted");
}
```

## Query Builder

The fluent query builder allows you to construct complex queries:

### Field Selection

```csharp
var query = new QueryBuilder()
    .Fields("name", "email", "phone", "created");

var response = await client.GetAsync<User>("users", query);
```

### Filtering

```csharp
var query = new QueryBuilder()
    .Filter("status", FilterOperator.Eq, "active")
    .Filter("created", FilterOperator.Gte, DateTime.Today.AddDays(-7))
    .Filter("role", FilterOperator.In, "admin", "manager");

var response = await client.GetAsync<User>("users", query);
```

Available filter operators:
- `Eq` - Equal
- `Neq` - Not equal
- `Gt` - Greater than
- `Gte` - Greater than or equal
- `Lt` - Less than
- `Lte` - Less than or equal
- `Like` - Pattern matching (SQL LIKE)
- `In` - Value in list
- `NotIn` - Value not in list

### Sorting

```csharp
var query = new QueryBuilder()
    .Sort("created", SortDirection.Desc)
    .Sort("name", SortDirection.Asc);

var response = await client.GetAsync<User>("users", query);
```

### Pagination

```csharp
var query = new QueryBuilder()
    .Skip(0)
    .Take(50);

var response = await client.GetAsync<User>("users", query);
```

### Complete Example

```csharp
var query = new QueryBuilder()
    .Fields("name", "email", "status", "created")
    .Filter("status", FilterOperator.Eq, "active")
    .Filter("created", FilterOperator.Gte, DateTime.Today.AddMonths(-1))
    .Sort("created", SortDirection.Desc)
    .Take(100);

var response = await client.GetAsync<User>("users", query);
```

### Using Filter Helper Methods

```csharp
var query = new QueryBuilder()
    .Filter(Filter.Eq("status", "active"))
    .Filter(Filter.Gte("age", 18))
    .Filter(Filter.Like("name", "%john%"))
    .Filter(Filter.In("role", "admin", "manager", "user"));
```

## Automatic Pagination with IAsyncEnumerable

For large datasets, use `IterateAsync` to automatically handle pagination:

```csharp
var query = new QueryBuilder()
    .Filter("status", FilterOperator.Eq, "active")
    .Sort("created", SortDirection.Desc);

await foreach (var user in client.IterateAsync<User>("users", query, pageSize: 100))
{
    Console.WriteLine($"Processing user: {user.Name}");
}
```

## Retry Policy

Configure automatic retries with exponential backoff:

```csharp
var config = new DaktelaConfig
{
    InstanceUrl = "your-instance.daktela.com",
    AccessToken = "your-access-token",
    RetryPolicy = new RetryPolicy
    {
        MaxRetries = 3,
        InitialDelay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromSeconds(30),
        BackoffMultiplier = 2.0
    }
};
```

Or use the built-in defaults:

```csharp
config.RetryPolicy = RetryPolicy.Default;  // 3 retries with exponential backoff
config.RetryPolicy = RetryPolicy.NoRetry;  // Disable retries
```

## Error Handling

The connector throws specific exceptions for different error conditions:

```csharp
try
{
    var response = await client.GetAsync<User>("users", "non-existent");
}
catch (DaktelaUnauthorizedException ex)
{
    // HTTP 401 - Invalid or expired access token
    Console.WriteLine("Authentication failed");
}
catch (DaktelaNotFoundException ex)
{
    // HTTP 404 - Resource not found
    Console.WriteLine("User not found");
}
catch (DaktelaRateLimitException ex)
{
    // HTTP 429 - Rate limit exceeded
    Console.WriteLine($"Rate limited. Retry after: {ex.RetryAfter}");
}
catch (DaktelaValidationException ex)
{
    // HTTP 400/422 - Validation errors
    foreach (var (field, errors) in ex.ValidationErrors)
    {
        Console.WriteLine($"{field}: {string.Join(", ", errors)}");
    }
}
catch (DaktelaTimeoutException ex)
{
    // Request timed out
    Console.WriteLine("Request timed out");
}
catch (DaktelaConnectionException ex)
{
    // Network connection error
    Console.WriteLine($"Connection error: {ex.Message}");
}
catch (DaktelaException ex)
{
    // Other API errors
    Console.WriteLine($"API error {ex.StatusCode}: {ex.Message}");
}
```

## Response Object

All API methods return a `DaktelaResponse<T>` object:

```csharp
var response = await client.GetAsync<User>("users");

// Check if request was successful (2xx status code)
if (response.IsSuccess)
{
    // Access the data
    var users = response.Data;

    // Total count (for list responses)
    var total = response.Total;
}
else
{
    // Access errors
    var errors = response.Errors;
}

// HTTP status code
var statusCode = response.StatusCode;

// Raw JSON response
var rawJson = response.RawResponse;
```

## Model Classes

Define your own model classes to deserialize API responses:

```csharp
public class User
{
    public string Name { get; set; }
    public string Email { get; set; }
    public string Status { get; set; }
    public DateTime Created { get; set; }
}

public class Contact
{
    public string Name { get; set; }
    public string Firstname { get; set; }
    public string Lastname { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
}
```

## Cancellation Support

All async methods support cancellation tokens:

```csharp
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

try
{
    var response = await client.GetAsync<User>("users", cancellationToken: cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Operation was cancelled");
}
```

## Requirements

- .NET 8.0 or later

## License

MIT License
