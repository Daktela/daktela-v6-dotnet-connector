using Daktela.Connector.Query;
using Xunit;

namespace Daktela.Connector.Tests;

public class FilterTests
{
    [Fact]
    public void Constructor_WithNullField_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Filter(null!, FilterOperator.Eq, "value"));
    }

    [Fact]
    public void Constructor_WithEmptyField_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Filter("", FilterOperator.Eq, "value"));
    }

    [Fact]
    public void Constructor_WithWhitespaceField_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Filter("   ", FilterOperator.Eq, "value"));
    }

    [Fact]
    public void Constructor_WithNullValue_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Filter("field", FilterOperator.Eq, null!));
    }

    [Fact]
    public void Eq_CreatesCorrectFilter()
    {
        var filter = Filter.Eq("status", "active");

        Assert.Equal("status", filter.Field);
        Assert.Equal(FilterOperator.Eq, filter.Operator);
        Assert.Equal("active", filter.Value);
    }

    [Fact]
    public void Neq_CreatesCorrectFilter()
    {
        var filter = Filter.Neq("status", "deleted");

        Assert.Equal("status", filter.Field);
        Assert.Equal(FilterOperator.Neq, filter.Operator);
        Assert.Equal("deleted", filter.Value);
    }

    [Fact]
    public void Gt_CreatesCorrectFilter()
    {
        var filter = Filter.Gt("age", 18);

        Assert.Equal("age", filter.Field);
        Assert.Equal(FilterOperator.Gt, filter.Operator);
        Assert.Equal(18, filter.Value);
    }

    [Fact]
    public void Gte_CreatesCorrectFilter()
    {
        var filter = Filter.Gte("age", 18);

        Assert.Equal("age", filter.Field);
        Assert.Equal(FilterOperator.Gte, filter.Operator);
        Assert.Equal(18, filter.Value);
    }

    [Fact]
    public void Lt_CreatesCorrectFilter()
    {
        var filter = Filter.Lt("age", 65);

        Assert.Equal("age", filter.Field);
        Assert.Equal(FilterOperator.Lt, filter.Operator);
        Assert.Equal(65, filter.Value);
    }

    [Fact]
    public void Lte_CreatesCorrectFilter()
    {
        var filter = Filter.Lte("age", 65);

        Assert.Equal("age", filter.Field);
        Assert.Equal(FilterOperator.Lte, filter.Operator);
        Assert.Equal(65, filter.Value);
    }

    [Fact]
    public void Like_CreatesCorrectFilter()
    {
        var filter = Filter.Like("name", "%john%");

        Assert.Equal("name", filter.Field);
        Assert.Equal(FilterOperator.Like, filter.Operator);
        Assert.Equal("%john%", filter.Value);
    }

    [Fact]
    public void In_CreatesCorrectFilter()
    {
        var filter = Filter.In("status", "active", "pending", "new");

        Assert.Equal("status", filter.Field);
        Assert.Equal(FilterOperator.In, filter.Operator);
        Assert.IsType<object[]>(filter.Value);
        var values = (object[])filter.Value;
        Assert.Equal(3, values.Length);
        Assert.Equal("active", values[0]);
        Assert.Equal("pending", values[1]);
        Assert.Equal("new", values[2]);
    }

    [Fact]
    public void NotIn_CreatesCorrectFilter()
    {
        var filter = Filter.NotIn("status", "deleted", "archived");

        Assert.Equal("status", filter.Field);
        Assert.Equal(FilterOperator.NotIn, filter.Operator);
        Assert.IsType<object[]>(filter.Value);
        var values = (object[])filter.Value;
        Assert.Equal(2, values.Length);
    }
}
