using Daktela.Connector.Http;
using Xunit;

namespace Daktela.Connector.Tests;

public class RetryPolicyTests
{
    [Fact]
    public void Default_HasCorrectValues()
    {
        var policy = RetryPolicy.Default;

        Assert.Equal(3, policy.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), policy.InitialDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), policy.MaxDelay);
        Assert.Equal(2.0, policy.BackoffMultiplier);
        Assert.False(policy.RetryUnsafeHttpMethods);
        Assert.True(policy.RetryOnTimeout);
    }

    [Fact]
    public void NoRetry_HasZeroMaxRetries()
    {
        var policy = RetryPolicy.NoRetry;

        Assert.Equal(0, policy.MaxRetries);
    }

    [Fact]
    public void Default_ContainsExpectedStatusCodes()
    {
        var policy = RetryPolicy.Default;

        Assert.Contains(408, policy.RetryableStatusCodes);
        Assert.Contains(429, policy.RetryableStatusCodes);
        Assert.Contains(500, policy.RetryableStatusCodes);
        Assert.Contains(502, policy.RetryableStatusCodes);
        Assert.Contains(503, policy.RetryableStatusCodes);
        Assert.Contains(504, policy.RetryableStatusCodes);
    }

    [Fact]
    public void GetDelay_FirstAttempt_ReturnsInitialDelay()
    {
        var policy = new RetryPolicy
        {
            InitialDelay = TimeSpan.FromSeconds(1),
            BackoffMultiplier = 2.0
        };

        var delay = policy.GetDelay(0);

        Assert.Equal(TimeSpan.FromSeconds(1), delay);
    }

    [Fact]
    public void GetDelay_SecondAttempt_ReturnsDoubledDelay()
    {
        var policy = new RetryPolicy
        {
            InitialDelay = TimeSpan.FromSeconds(1),
            BackoffMultiplier = 2.0
        };

        var delay = policy.GetDelay(1);

        Assert.Equal(TimeSpan.FromSeconds(2), delay);
    }

    [Fact]
    public void GetDelay_ThirdAttempt_ReturnsQuadrupledDelay()
    {
        var policy = new RetryPolicy
        {
            InitialDelay = TimeSpan.FromSeconds(1),
            BackoffMultiplier = 2.0
        };

        var delay = policy.GetDelay(2);

        Assert.Equal(TimeSpan.FromSeconds(4), delay);
    }

    [Fact]
    public void GetDelay_CapsAtMaxDelay()
    {
        var policy = new RetryPolicy
        {
            InitialDelay = TimeSpan.FromSeconds(10),
            MaxDelay = TimeSpan.FromSeconds(30),
            BackoffMultiplier = 2.0
        };

        var delay = policy.GetDelay(5); // Would be 320 seconds without cap

        Assert.Equal(TimeSpan.FromSeconds(30), delay);
    }

    [Fact]
    public void GetDelay_WithHugeAttempt_CapsWithoutOverflowing()
    {
        var policy = new RetryPolicy
        {
            InitialDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(30),
            BackoffMultiplier = 2
        };

        Assert.Equal(TimeSpan.FromSeconds(30), policy.GetDelay(int.MaxValue));
    }

    [Theory]
    [InlineData(-1, 0, 1)]
    [InlineData(1, -1, 1)]
    [InlineData(1, 1, 0.5)]
    public void InvalidSettings_Throw(int maxRetries, int initialDelayMilliseconds, double multiplier)
    {
        var policy = new RetryPolicy
        {
            MaxRetries = maxRetries,
            InitialDelay = TimeSpan.FromMilliseconds(initialDelayMilliseconds),
            BackoffMultiplier = multiplier
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => policy.GetDelay(0));
    }

    [Theory]
    [InlineData("60", 60)]
    [InlineData("0", 0)]
    [InlineData("-10", 0)]
    public void ParseRetryAfter_WithSeconds_ReturnsNonNegativeDelay(string value, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), RateLimitHandler.ParseRetryAfter(value));
    }

    [Fact]
    public void ParseRetryAfter_WithPastDate_ReturnsZero()
    {
        var value = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("R");

        Assert.Equal(TimeSpan.Zero, RateLimitHandler.ParseRetryAfter(value));
    }

    [Fact]
    public void ParseRetryAfter_WithInvalidValue_ReturnsNull()
    {
        Assert.Null(RateLimitHandler.ParseRetryAfter("not-a-delay"));
    }
}
