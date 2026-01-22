using Daktela.Connector.Exceptions;
using Xunit;

namespace Daktela.Connector.Tests;

public class ExceptionTests
{
    [Fact]
    public void DaktelaException_StoresAllProperties()
    {
        var ex = new DaktelaException("Test message", 500, "{\"error\": \"test\"}");

        Assert.Equal("Test message", ex.Message);
        Assert.Equal(500, ex.StatusCode);
        Assert.Equal("{\"error\": \"test\"}", ex.ResponseBody);
    }

    [Fact]
    public void DaktelaException_WithInnerException_Stores()
    {
        var inner = new InvalidOperationException("Inner");
        var ex = new DaktelaException("Test", inner);

        Assert.Equal("Test", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void DaktelaUnauthorizedException_Has401StatusCode()
    {
        var ex = new DaktelaUnauthorizedException();

        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public void DaktelaNotFoundException_Has404StatusCode()
    {
        var ex = new DaktelaNotFoundException();

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public void DaktelaRateLimitException_Has429StatusCode()
    {
        var ex = new DaktelaRateLimitException();

        Assert.Equal(429, ex.StatusCode);
    }

    [Fact]
    public void DaktelaRateLimitException_StoresRetryAfter()
    {
        var retryAfter = TimeSpan.FromSeconds(60);
        var ex = new DaktelaRateLimitException("Rate limited", retryAfter);

        Assert.Equal(429, ex.StatusCode);
        Assert.Equal(retryAfter, ex.RetryAfter);
    }

    [Fact]
    public void DaktelaValidationException_Has400StatusCode()
    {
        var ex = new DaktelaValidationException("Validation failed");

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void DaktelaValidationException_ParsesValidationErrors()
    {
        var responseBody = @"{""errors"": {""name"": [""Name is required"", ""Name too short""], ""email"": ""Invalid email""}}";

        var ex = new DaktelaValidationException("Validation failed", responseBody);

        Assert.Equal(400, ex.StatusCode);
        Assert.True(ex.ValidationErrors.ContainsKey("name"));
        Assert.Equal(2, ex.ValidationErrors["name"].Count);
        Assert.Contains("Name is required", ex.ValidationErrors["name"]);
        Assert.Contains("Name too short", ex.ValidationErrors["name"]);
        Assert.True(ex.ValidationErrors.ContainsKey("email"));
        Assert.Single(ex.ValidationErrors["email"]);
    }

    [Fact]
    public void DaktelaValidationException_WithInvalidJson_ReturnsEmptyErrors()
    {
        var ex = new DaktelaValidationException("Validation failed", "not json");

        Assert.Empty(ex.ValidationErrors);
    }

    [Fact]
    public void DaktelaConnectionException_HasNoStatusCode()
    {
        var ex = new DaktelaConnectionException();

        Assert.Null(ex.StatusCode);
    }

    [Fact]
    public void DaktelaTimeoutException_HasNoStatusCode()
    {
        var ex = new DaktelaTimeoutException();

        Assert.Null(ex.StatusCode);
    }
}
