using System.Globalization;
using System.Text;
using System.Web;

namespace Daktela.Connector.Query;

/// <summary>
/// Fluent builder for constructing Daktela API queries.
/// </summary>
public class QueryBuilder
{
    private readonly List<string> _fields = new();
    private readonly List<FilterGroup> _filterGroups = new();
    private readonly List<Sort> _sorts = new();
    private int? _take;
    private int? _skip;

    private class FilterGroup
    {
        public List<Filter> Filters { get; } = new();
        public bool IsOr { get; set; }
    }

    /// <summary>
    /// Specifies which fields to return in the response.
    /// </summary>
    /// <param name="fields">The field names to include.</param>
    public QueryBuilder Fields(params string[] fields)
    {
        _fields.AddRange(fields);
        return this;
    }

    /// <summary>
    /// Adds a filter condition (AND logic with other filters).
    /// </summary>
    /// <param name="field">The field name to filter on.</param>
    /// <param name="op">The comparison operator.</param>
    /// <param name="value">The value to compare against.</param>
    public QueryBuilder Filter(string field, FilterOperator op, object value)
    {
        var filter = new Filter(field, op, value);

        // Add to the last AND group or create a new one
        var lastGroup = _filterGroups.LastOrDefault(g => !g.IsOr);
        if (lastGroup == null)
        {
            lastGroup = new FilterGroup { IsOr = false };
            _filterGroups.Add(lastGroup);
        }
        lastGroup.Filters.Add(filter);

        return this;
    }

    /// <summary>
    /// Adds a filter using an existing Filter object.
    /// </summary>
    /// <param name="filter">The filter to add.</param>
    public QueryBuilder Filter(Filter filter)
    {
        return Filter(filter.Field, filter.Operator, filter.Value);
    }

    /// <summary>
    /// Adds a group of filters with OR logic between them.
    /// </summary>
    /// <param name="orGroup">Action to configure the OR group.</param>
    public QueryBuilder OrFilter(Action<QueryBuilder> orGroup)
    {
        var subBuilder = new QueryBuilder();
        orGroup(subBuilder);

        var group = new FilterGroup { IsOr = true };
        foreach (var filterGroup in subBuilder._filterGroups)
        {
            group.Filters.AddRange(filterGroup.Filters);
        }
        _filterGroups.Add(group);

        return this;
    }

    /// <summary>
    /// Adds a sort condition.
    /// </summary>
    /// <param name="field">The field name to sort by.</param>
    /// <param name="direction">The sort direction.</param>
    public QueryBuilder Sort(string field, SortDirection direction = SortDirection.Asc)
    {
        _sorts.Add(new Sort(field, direction));
        return this;
    }

    /// <summary>
    /// Adds a sort using an existing Sort object.
    /// </summary>
    /// <param name="sort">The sort to add.</param>
    public QueryBuilder Sort(Sort sort)
    {
        _sorts.Add(sort);
        return this;
    }

    /// <summary>
    /// Sets the maximum number of records to return.
    /// </summary>
    /// <param name="count">The maximum number of records.</param>
    public QueryBuilder Take(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Take count must be non-negative.");

        _take = count;
        return this;
    }

    /// <summary>
    /// Sets the number of records to skip.
    /// </summary>
    /// <param name="count">The number of records to skip.</param>
    public QueryBuilder Skip(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Skip count must be non-negative.");

        _skip = count;
        return this;
    }

    /// <summary>
    /// Builds the query string for the API request.
    /// </summary>
    /// <returns>The encoded query string (without leading ?).</returns>
    public string Build()
    {
        var parts = new List<string>();

        // Fields
        if (_fields.Count > 0)
        {
            for (int i = 0; i < _fields.Count; i++)
            {
                parts.Add($"fields[{i}]={HttpUtility.UrlEncode(_fields[i])}");
            }
        }

        // Filters
        int filterIndex = 0;
        foreach (var group in _filterGroups)
        {
            foreach (var filter in group.Filters)
            {
                var fieldEncoded = HttpUtility.UrlEncode(filter.Field);
                var opEncoded = filter.Operator.ToApiString();
                var valueEncoded = FormatValue(filter.Value);

                parts.Add($"filter[{filterIndex}][field]={fieldEncoded}");
                parts.Add($"filter[{filterIndex}][operator]={opEncoded}");

                if (filter.Value is Array arr)
                {
                    for (int i = 0; i < arr.Length; i++)
                    {
                        parts.Add($"filter[{filterIndex}][value][{i}]={FormatValue(arr.GetValue(i))}");
                    }
                }
                else
                {
                    parts.Add($"filter[{filterIndex}][value]={valueEncoded}");
                }

                filterIndex++;
            }
        }

        // Sorts
        for (int i = 0; i < _sorts.Count; i++)
        {
            var sort = _sorts[i];
            parts.Add($"sort[{i}][field]={HttpUtility.UrlEncode(sort.Field)}");
            parts.Add($"sort[{i}][direction]={sort.Direction.ToApiString()}");
        }

        // Pagination
        if (_take.HasValue)
        {
            parts.Add($"take={_take.Value}");
        }

        if (_skip.HasValue)
        {
            parts.Add($"skip={_skip.Value}");
        }

        return string.Join("&", parts);
    }

    /// <summary>
    /// Gets the current skip value.
    /// </summary>
    internal int? GetSkip() => _skip;

    /// <summary>
    /// Gets the current take value.
    /// </summary>
    internal int? GetTake() => _take;

    /// <summary>
    /// Creates a new QueryBuilder with updated skip value for pagination.
    /// </summary>
    internal QueryBuilder WithSkip(int skip)
    {
        var clone = Clone();
        clone._skip = skip;
        return clone;
    }

    /// <summary>
    /// Creates a clone of this QueryBuilder.
    /// </summary>
    public QueryBuilder Clone()
    {
        var clone = new QueryBuilder();
        clone._fields.AddRange(_fields);
        clone._filterGroups.AddRange(_filterGroups);
        clone._sorts.AddRange(_sorts);
        clone._take = _take;
        clone._skip = _skip;
        return clone;
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "",
            DateTime dt => HttpUtility.UrlEncode(dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            DateTimeOffset dto => HttpUtility.UrlEncode(dto.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            bool b => b ? "1" : "0",
            _ => HttpUtility.UrlEncode(value.ToString() ?? "")
        };
    }
}
