using Daktela.Connector.Http;
using Xunit;

namespace Daktela.Connector.Tests;

public class DaktelaClientTests
{
    [Fact]
    public void Constructor_WithNullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DaktelaClient(null!));
    }

    [Fact]
    public void Constructor_WithNullInstanceUrl_Throws()
    {
        var config = new DaktelaConfig
        {
            InstanceUrl = null!,
            AccessToken = "token"
        };

        Assert.Throws<ArgumentException>(() => new DaktelaClient(config));
    }

    [Fact]
    public void Constructor_WithEmptyInstanceUrl_Throws()
    {
        var config = new DaktelaConfig
        {
            InstanceUrl = "",
            AccessToken = "token"
        };

        Assert.Throws<ArgumentException>(() => new DaktelaClient(config));
    }

    [Fact]
    public void Constructor_WithNullAccessToken_Throws()
    {
        var config = new DaktelaConfig
        {
            InstanceUrl = "test.daktela.com",
            AccessToken = null!
        };

        Assert.Throws<ArgumentException>(() => new DaktelaClient(config));
    }

    [Fact]
    public void Constructor_WithEmptyAccessToken_Throws()
    {
        var config = new DaktelaConfig
        {
            InstanceUrl = "test.daktela.com",
            AccessToken = ""
        };

        Assert.Throws<ArgumentException>(() => new DaktelaClient(config));
    }

    [Fact]
    public void Constructor_WithValidConfig_CreatesClient()
    {
        var config = new DaktelaConfig
        {
            InstanceUrl = "test.daktela.com",
            AccessToken = "valid-token"
        };

        using var client = new DaktelaClient(config);

        Assert.NotNull(client);
    }

    [Fact]
    public void Constructor_WithInvalidRateLimitPolicy_Throws()
    {
        var config = new DaktelaConfig
        {
            InstanceUrl = "test.daktela.com",
            AccessToken = "token",
            RateLimitPolicy = new RateLimitPolicy { MaxWait = TimeSpan.FromSeconds(-1) }
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new DaktelaClient(config));
    }

    [Fact]
    public void Constructor_WithControlCharacterInUserAgentSuffix_Throws()
    {
        var config = new DaktelaConfig
        {
            InstanceUrl = "test.daktela.com",
            AccessToken = "token",
            UserAgentSuffix = "bad\r\nsuffix"
        };

        Assert.Throws<ArgumentException>(() => new DaktelaClient(config));
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var config = new DaktelaConfig
        {
            InstanceUrl = "test.daktela.com",
            AccessToken = "valid-token"
        };

        var client = new DaktelaClient(config);

        // Should not throw
        client.Dispose();
        client.Dispose();
    }
}
