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
}
