using Xunit;

namespace Daktela.Connector.Tests;

public class DaktelaResponseTests
{
    [Theory]
    [InlineData(200, true)]
    [InlineData(201, true)]
    [InlineData(204, true)]
    [InlineData(299, true)]
    [InlineData(300, false)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(404, false)]
    [InlineData(500, false)]
    public void IsSuccess_ReturnsCorrectValue(int statusCode, bool expected)
    {
        var response = new DaktelaResponse(statusCode);

        Assert.Equal(expected, response.IsSuccess);
    }

    [Fact]
    public void DaktelaResponse_StoresAllProperties()
    {
        var errors = new List<DaktelaError>
        {
            new() { Code = "ERR001", Message = "Test error" }
        };

        var response = new DaktelaResponse(400, errors, "{\"error\": true}");

        Assert.Equal(400, response.StatusCode);
        Assert.NotNull(response.Errors);
        Assert.Single(response.Errors);
        Assert.Equal("ERR001", response.Errors[0].Code);
        Assert.Equal("{\"error\": true}", response.RawResponse);
    }

    [Fact]
    public void DaktelaResponseT_StoresDataAndTotal()
    {
        var data = new { Name = "Test", Value = 123 };

        var response = new DaktelaResponse<object>(200, data, 100);

        Assert.Equal(200, response.StatusCode);
        Assert.NotNull(response.Data);
        Assert.Equal(100, response.Total);
        Assert.True(response.IsSuccess);
    }

    [Fact]
    public void DaktelaResponseT_WithNullData_WorksCorrectly()
    {
        var response = new DaktelaResponse<string>(204);

        Assert.Equal(204, response.StatusCode);
        Assert.Null(response.Data);
        Assert.Null(response.Total);
        Assert.True(response.IsSuccess);
    }
}
