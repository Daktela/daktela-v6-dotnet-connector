using Daktela.Connector.Utils;
using Xunit;

namespace Daktela.Connector.Tests;

public class FormatHelperTests
{
    [Fact]
    public void NullNumber_ReturnsNull()
        => Assert.Null(FormatHelper.GetNormalizedPhoneNumber(null));

    [Theory]
    [InlineData("00420773794604")]
    [InlineData("+420773794604")]
    [InlineData("773794604")]
    [InlineData("420773794604")]
    [InlineData("773 794 604")]
    public void CzechNumber_UsesInternationalDoubleZeroForm(string input)
        => Assert.Equal("00420773794604", FormatHelper.GetNormalizedPhoneNumber(input));

    [Theory]
    [InlineData("00420773794604")]
    [InlineData("+420773794604")]
    [InlineData("773794604")]
    [InlineData("420773794604")]
    public void CzechNumber_CanUsePlusForm(string input)
        => Assert.Equal("+420773794604", FormatHelper.GetNormalizedPhoneNumber(input, plusSign: true));

    [Theory]
    [InlineData("+421123456789")]
    [InlineData("00421123456789")]
    [InlineData("421123456789")]
    public void CustomCountryPrefix_IsSupported(string input)
        => Assert.Equal(
            "00421123456789",
            FormatHelper.GetNormalizedPhoneNumber(input, intlPrefix: "421", intlLength: 12));

    [Fact]
    public void CustomCountryPrefix_CanUsePlusForm()
        => Assert.Equal(
            "+421123456789",
            FormatHelper.GetNormalizedPhoneNumber(
                "00421123456789",
                plusSign: true,
                intlPrefix: "421",
                intlLength: 12));
}
