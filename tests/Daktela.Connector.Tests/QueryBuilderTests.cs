using Daktela.Connector.Query;
using System.Globalization;
using Xunit;

namespace Daktela.Connector.Tests;

public class QueryBuilderTests
{
    [Fact]
    public void Fields_BuildsCorrectQueryString()
    {
        var query = new QueryBuilder()
            .Fields("name", "email", "phone");

        var result = query.Build();

        Assert.Contains("fields[0]=name", result);
        Assert.Contains("fields[1]=email", result);
        Assert.Contains("fields[2]=phone", result);
    }

    [Fact]
    public void Filter_BuildsCorrectQueryString()
    {
        var query = new QueryBuilder()
            .Filter("status", FilterOperator.Eq, "active");

        var result = query.Build();

        Assert.Contains("filter[0][field]=status", result);
        Assert.Contains("filter[0][operator]=eq", result);
        Assert.Contains("filter[0][value]=active", result);
    }

    [Fact]
    public void MultipleFilters_BuildsCorrectQueryString()
    {
        var query = new QueryBuilder()
            .Filter("status", FilterOperator.Eq, "active")
            .Filter("age", FilterOperator.Gte, 18);

        var result = query.Build();

        Assert.Contains("filter[0][field]=status", result);
        Assert.Contains("filter[0][operator]=eq", result);
        Assert.Contains("filter[0][value]=active", result);
        Assert.Contains("filter[1][field]=age", result);
        Assert.Contains("filter[1][operator]=gte", result);
        Assert.Contains("filter[1][value]=18", result);
    }

    [Fact]
    public void Sort_BuildsCorrectQueryString()
    {
        var query = new QueryBuilder()
            .Sort("created", SortDirection.Desc);

        var result = query.Build();

        Assert.Contains("sort[0][field]=created", result);
        Assert.Contains("sort[0][dir]=desc", result);
    }

    [Fact]
    public void MultipleSorts_BuildsCorrectQueryString()
    {
        var query = new QueryBuilder()
            .Sort("status", SortDirection.Asc)
            .Sort("created", SortDirection.Desc);

        var result = query.Build();

        Assert.Contains("sort[0][field]=status", result);
        Assert.Contains("sort[0][dir]=asc", result);
        Assert.Contains("sort[1][field]=created", result);
        Assert.Contains("sort[1][dir]=desc", result);
    }

    [Fact]
    public void Pagination_BuildsCorrectQueryString()
    {
        var query = new QueryBuilder()
            .Take(50)
            .Skip(100);

        var result = query.Build();

        Assert.Contains("take=50", result);
        Assert.Contains("skip=100", result);
    }

    [Fact]
    public void CompleteQuery_BuildsCorrectQueryString()
    {
        var query = new QueryBuilder()
            .Fields("name", "email")
            .Filter("status", FilterOperator.Eq, "active")
            .Sort("created", SortDirection.Desc)
            .Take(50)
            .Skip(0);

        var result = query.Build();

        Assert.Contains("fields[0]=name", result);
        Assert.Contains("fields[1]=email", result);
        Assert.Contains("filter[0][field]=status", result);
        Assert.Contains("sort[0][field]=created", result);
        Assert.Contains("take=50", result);
        Assert.Contains("skip=0", result);
    }

    [Fact]
    public void EmptyQuery_ReturnsEmptyString()
    {
        var query = new QueryBuilder();
        var result = query.Build();
        Assert.Empty(result);
    }

    [Fact]
    public void Filter_WithDateTime_FormatsCorrectly()
    {
        var date = new DateTime(2024, 1, 15, 10, 30, 0);
        var query = new QueryBuilder()
            .Filter("created", FilterOperator.Gte, date);

        var result = query.Build();

        Assert.Contains("filter[0][value]=2024-01-15+10%3a30%3a00", result);
    }

    [Fact]
    public void Filter_WithBoolean_FormatsCorrectly()
    {
        var query = new QueryBuilder()
            .Filter("active", FilterOperator.Eq, true)
            .Filter("deleted", FilterOperator.Eq, false);

        var result = query.Build();

        Assert.Contains("filter[0][value]=1", result);
        Assert.Contains("filter[1][value]=0", result);
    }

    [Theory]
    [InlineData(FilterOperator.Eq, "eq")]
    [InlineData(FilterOperator.Neq, "neq")]
    [InlineData(FilterOperator.Gt, "gt")]
    [InlineData(FilterOperator.Gte, "gte")]
    [InlineData(FilterOperator.Lt, "lt")]
    [InlineData(FilterOperator.Lte, "lte")]
    [InlineData(FilterOperator.Like, "like")]
    [InlineData(FilterOperator.Contains, "contains")]
    [InlineData(FilterOperator.StartsWith, "startswith")]
    [InlineData(FilterOperator.EndsWith, "endswith")]
    [InlineData(FilterOperator.NotLike, "notlike")]
    [InlineData(FilterOperator.DoesNotContain, "doesnotcontain")]
    [InlineData(FilterOperator.IsNull, "isnull")]
    [InlineData(FilterOperator.IsNotNull, "isnotnull")]
    [InlineData(FilterOperator.In, "in")]
    [InlineData(FilterOperator.NotIn, "notin")]
    public void FilterOperator_MapsCorrectly(FilterOperator op, string expected)
    {
        var query = new QueryBuilder()
            .Filter("field", op, "value");

        var result = query.Build();

        Assert.Contains($"filter[0][operator]={expected}", result);
    }

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        var original = new QueryBuilder()
            .Fields("name")
            .Filter("status", FilterOperator.Eq, "active")
            .Take(10);

        var clone = original.Clone();
        clone.Fields("email").Take(20);

        var originalResult = original.Build();
        var cloneResult = clone.Build();

        Assert.Contains("take=10", originalResult);
        Assert.DoesNotContain("fields[1]=email", originalResult);

        Assert.Contains("take=20", cloneResult);
        Assert.Contains("fields[1]=email", cloneResult);
    }

    [Fact]
    public void OrFilter_BuildsNestedFilterTree()
    {
        var query = new QueryBuilder()
            .OrFilter(group => group
                .Filter("status", FilterOperator.Eq, "new")
                .Filter("status", FilterOperator.Eq, "open"));

        var result = query.Build();

        Assert.Contains("filter[logic]=or", result);
        Assert.Contains("filter[filters][0][field]=status", result);
        Assert.Contains("filter[filters][0][value]=new", result);
        Assert.Contains("filter[filters][1][value]=open", result);
    }

    [Fact]
    public void MixedFilters_BuildTopLevelAndTree()
    {
        var query = new QueryBuilder()
            .Filter("active", FilterOperator.Eq, true)
            .OrFilter(group => group
                .Filter("team", FilterOperator.Eq, "sales")
                .Filter("team", FilterOperator.Eq, "support"));

        var result = query.Build();

        Assert.Contains("filter[logic]=and", result);
        Assert.Contains("filter[filters][0][field]=active", result);
        Assert.Contains("filter[filters][1][logic]=or", result);
        Assert.Contains("filter[filters][1][filters][1][value]=support", result);
    }

    [Fact]
    public void CustomOperator_IsPassedThrough()
    {
        var result = new QueryBuilder()
            .Filter("custom", "future_operator", "value")
            .Build();

        Assert.Contains("filter[0][operator]=future_operator", result);
    }

    [Fact]
    public void Parameter_EncodesNestedDictionariesAndLists()
    {
        var query = new QueryBuilder().Parameter("custom", new Dictionary<string, object?>
        {
            ["enabled"] = true,
            ["ids"] = new[] { 10, 20 }
        });

        var result = query.Build();

        Assert.Contains("custom[enabled]=1", result);
        Assert.Contains("custom[ids][0]=10", result);
        Assert.Contains("custom[ids][1]=20", result);
    }

    [Fact]
    public void NumericValues_UseInvariantCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("cs-CZ");

            var result = new QueryBuilder()
                .Filter("price", FilterOperator.Gte, 12.5m)
                .Build();

            Assert.Contains("filter[0][value]=12.5", result);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Clone_DeepCopiesMutableFilterAndParameterValues()
    {
        var filterValues = new[] { "new", "open" };
        var parameterValues = new List<int> { 1, 2 };
        var original = new QueryBuilder()
            .Filter("status", FilterOperator.In, filterValues)
            .Parameter("ids", parameterValues);

        var clone = original.Clone();
        filterValues[0] = "changed";
        parameterValues[0] = 99;

        Assert.Contains("filter[0][value][0]=new", clone.Build());
        Assert.Contains("ids[0]=1", clone.Build());
        Assert.Contains("filter[0][value][0]=changed", original.Build());
        Assert.Contains("ids[0]=99", original.Build());
    }

    [Fact]
    public void EmptyFilterGroup_Throws()
    {
        var query = new QueryBuilder();

        Assert.Throws<ArgumentException>(() => query.OrFilter(_ => { }));
    }

    [Fact]
    public void Take_WithNegativeValue_Throws()
    {
        var query = new QueryBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => query.Take(-1));
    }

    [Fact]
    public void Skip_WithNegativeValue_Throws()
    {
        var query = new QueryBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => query.Skip(-1));
    }
}
