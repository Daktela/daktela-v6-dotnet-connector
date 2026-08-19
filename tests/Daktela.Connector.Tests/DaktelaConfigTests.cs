using Xunit;

namespace Daktela.Connector.Tests;

public class DaktelaConfigTests
{
    [Theory]
    [InlineData("my.daktela.com", "https://my.daktela.com/api/v6/")]
    [InlineData("https://my.daktela.com", "https://my.daktela.com/api/v6/")]
    [InlineData("http://my.daktela.com", "http://my.daktela.com/api/v6/")]
    [InlineData("my.daktela.com/", "https://my.daktela.com/api/v6/")]
    [InlineData("https://my.daktela.com/", "https://my.daktela.com/api/v6/")]
    [InlineData("https://my.daktela.com/api/v6", "https://my.daktela.com/api/v6/")]
    [InlineData("https://my.daktela.com/api/v6/", "https://my.daktela.com/api/v6/")]
    public void GetBaseUrl_ReturnsCorrectUrl(string instanceUrl, string expected)
    {
        var config = new DaktelaConfig
        {
            InstanceUrl = instanceUrl,
            AccessToken = "test-token"
        };

        var result = config.GetBaseUrl();

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("ftp://my.daktela.com")]
    [InlineData("https://my.daktela.com?tenant=test")]
    [InlineData("https://my.daktela.com#fragment")]
    public void GetBaseUrl_WithInvalidUrl_Throws(string instanceUrl)
    {
        var config = new DaktelaConfig
        {
            InstanceUrl = instanceUrl,
            AccessToken = "test-token"
        };

        Assert.Throws<ArgumentException>(() => config.GetBaseUrl());
    }

    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var config = new DaktelaConfig
        {
            InstanceUrl = "test.daktela.com",
            AccessToken = "test-token"
        };

        Assert.Equal(AuthMethod.Header, config.AuthMethod);
        Assert.Equal(TimeSpan.FromSeconds(30), config.Timeout);
        Assert.True(config.VerifySsl);
        Assert.Null(config.RetryPolicy);
    }
}
