using Daktela.Connector.Query;
using Xunit;

namespace Daktela.Connector.Tests;

public class SortTests
{
    [Fact]
    public void Constructor_WithNullField_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Sort(null!));
    }

    [Fact]
    public void Constructor_WithEmptyField_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Sort(""));
    }

    [Fact]
    public void Constructor_WithWhitespaceField_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Sort("   "));
    }

    [Fact]
    public void Constructor_DefaultsToAsc()
    {
        var sort = new Sort("created");

        Assert.Equal("created", sort.Field);
        Assert.Equal(SortDirection.Asc, sort.Direction);
    }

    [Fact]
    public void Constructor_WithDirection_SetsCorrectly()
    {
        var sort = new Sort("created", SortDirection.Desc);

        Assert.Equal("created", sort.Field);
        Assert.Equal(SortDirection.Desc, sort.Direction);
    }

    [Fact]
    public void Asc_CreatesCorrectSort()
    {
        var sort = Sort.Asc("created");

        Assert.Equal("created", sort.Field);
        Assert.Equal(SortDirection.Asc, sort.Direction);
    }

    [Fact]
    public void Desc_CreatesCorrectSort()
    {
        var sort = Sort.Desc("created");

        Assert.Equal("created", sort.Field);
        Assert.Equal(SortDirection.Desc, sort.Direction);
    }
}
