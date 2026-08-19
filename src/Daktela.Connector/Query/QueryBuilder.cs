using System.Collections;
using System.Globalization;
using System.Web;

namespace Daktela.Connector.Query;

/// <summary>
/// Fluent builder for constructing Daktela API queries.
/// </summary>
public class QueryBuilder
{
    private readonly List<string> _fields = new();
    private readonly List<FilterNode> _filters = new();
    private readonly List<Sort> _sorts = new();
    private readonly Dictionary<string, object?> _parameters = new(StringComparer.Ordinal);
    private int? _take;
    private int? _skip;

    private sealed class FilterNode
    {
        public string? Field { get; init; }
        public string? Operator { get; init; }
        public object? Value { get; init; }
        public string? Logic { get; init; }
        public List<FilterNode> Children { get; init; } = new();
        public bool IsGroup => Logic != null;

        public FilterNode Clone() => new()
        {
            Field = Field,
            Operator = Operator,
            Value = QueryBuilder.CloneValue(Value),
            Logic = Logic,
            Children = Children.Select(child => child.Clone()).ToList()
        };
    }

    /// <summary>
    /// Specifies which fields to return in the response.
    /// </summary>
    public QueryBuilder Fields(params string[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field))
                throw new ArgumentException("Field names cannot be null or empty.", nameof(fields));
            _fields.Add(field);
        }
        return this;
    }

    /// <summary>
    /// Adds a filter condition using AND logic with other top-level filters.
    /// </summary>
    public QueryBuilder Filter(string field, FilterOperator op, object value)
        => Filter(field, op.ToApiString(), value);

    /// <summary>
    /// Adds a filter with an API operator name. This overload supports operators added by
    /// newer Daktela versions without requiring a connector update.
    /// </summary>
    public QueryBuilder Filter(string field, string op, object? value)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field name cannot be null or empty.", nameof(field));
        if (string.IsNullOrWhiteSpace(op))
            throw new ArgumentException("Operator cannot be null or empty.", nameof(op));

        _filters.Add(new FilterNode { Field = field, Operator = op, Value = value });
        return this;
    }

    /// <summary>
    /// Adds a filter using an existing <see cref="Query.Filter"/> object.
    /// </summary>
    public QueryBuilder Filter(Filter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Filter(filter.Field, filter.Operator, filter.Value);
    }

    /// <summary>
    /// Adds a nested group of filters joined with OR logic.
    /// </summary>
    public QueryBuilder OrFilter(Action<QueryBuilder> orGroup)
        => AddFilterGroup("or", orGroup);

    /// <summary>
    /// Adds a nested group of filters joined with AND logic.
    /// </summary>
    public QueryBuilder AndFilter(Action<QueryBuilder> andGroup)
        => AddFilterGroup("and", andGroup);

    /// <summary>
    /// Adds a sort condition.
    /// </summary>
    public QueryBuilder Sort(string field, SortDirection direction = SortDirection.Asc)
    {
        _sorts.Add(new Sort(field, direction));
        return this;
    }

    /// <summary>
    /// Adds a sort using an existing <see cref="Query.Sort"/> object.
    /// </summary>
    public QueryBuilder Sort(Sort sort)
    {
        ArgumentNullException.ThrowIfNull(sort);
        _sorts.Add(sort);
        return this;
    }

    /// <summary>
    /// Adds or replaces an arbitrary top-level query parameter.
    /// Dictionaries and collections are encoded using Daktela bracket notation.
    /// </summary>
    public QueryBuilder Parameter(string name, object? value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Parameter name cannot be null or empty.", nameof(name));
        _parameters[name] = value;
        return this;
    }

    /// <summary>
    /// Adds or replaces arbitrary top-level query parameters.
    /// </summary>
    public QueryBuilder Parameters(IEnumerable<KeyValuePair<string, object?>> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        foreach (var parameter in parameters)
            Parameter(parameter.Key, parameter.Value);
        return this;
    }

    /// <summary>
    /// Sets the maximum number of records to return.
    /// </summary>
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
    public QueryBuilder Skip(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Skip count must be non-negative.");
        _skip = count;
        return this;
    }

    /// <summary>
    /// Builds the encoded query string without a leading question mark.
    /// </summary>
    public string Build()
    {
        var parts = new List<string>();

        foreach (var parameter in _parameters)
        {
            if (!IsConfiguredBuiltInParameter(parameter.Key))
                AppendParameter(parts, Encode(parameter.Key), parameter.Value);
        }

        for (var i = 0; i < _fields.Count; i++)
            parts.Add($"fields[{i}]={Encode(_fields[i])}");

        AppendFilters(parts);

        for (var i = 0; i < _sorts.Count; i++)
        {
            parts.Add($"sort[{i}][field]={Encode(_sorts[i].Field)}");
            parts.Add($"sort[{i}][dir]={_sorts[i].Direction.ToApiString()}");
        }

        if (_take.HasValue)
            parts.Add($"take={_take.Value.ToString(CultureInfo.InvariantCulture)}");
        if (_skip.HasValue)
            parts.Add($"skip={_skip.Value.ToString(CultureInfo.InvariantCulture)}");

        return string.Join("&", parts);
    }

    internal int? GetSkip() => _skip;
    internal int? GetTake() => _take;

    internal QueryBuilder WithSkip(int skip)
    {
        var clone = Clone();
        clone._skip = skip;
        return clone;
    }

    internal QueryBuilder WithTake(int take)
    {
        var clone = Clone();
        clone._take = take;
        return clone;
    }

    /// <summary>
    /// Creates an independent clone of this query.
    /// </summary>
    public QueryBuilder Clone()
    {
        var clone = new QueryBuilder();
        clone._fields.AddRange(_fields);
        clone._filters.AddRange(_filters.Select(filter => filter.Clone()));
        clone._sorts.AddRange(_sorts);
        foreach (var parameter in _parameters)
            clone._parameters.Add(parameter.Key, CloneValue(parameter.Value));
        clone._take = _take;
        clone._skip = _skip;
        return clone;
    }

    private QueryBuilder AddFilterGroup(string logic, Action<QueryBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var childBuilder = new QueryBuilder();
        configure(childBuilder);
        if (childBuilder._filters.Count == 0)
            throw new ArgumentException("A filter group must contain at least one filter.", nameof(configure));

        _filters.Add(new FilterNode
        {
            Logic = logic,
            Children = childBuilder._filters.Select(filter => filter.Clone()).ToList()
        });
        return this;
    }

    private void AppendFilters(List<string> parts)
    {
        if (_filters.Count == 0)
            return;

        if (_filters.Count == 1 && _filters[0].IsGroup)
        {
            AppendFilterGroup(parts, "filter", _filters[0]);
            return;
        }

        parts.Add("filter[logic]=and");
        for (var i = 0; i < _filters.Count; i++)
            AppendFilterNode(parts, $"filter[filters][{i}]", _filters[i]);
    }

    private bool IsConfiguredBuiltInParameter(string name) => name switch
    {
        "fields" => _fields.Count > 0,
        "filter" => _filters.Count > 0,
        "sort" => _sorts.Count > 0,
        "take" => _take.HasValue,
        "skip" => _skip.HasValue,
        _ => false
    };

    private static void AppendFilterNode(List<string> parts, string prefix, FilterNode node)
    {
        if (node.IsGroup)
        {
            AppendFilterGroup(parts, prefix, node);
            return;
        }

        parts.Add($"{prefix}[field]={Encode(node.Field!)}");
        parts.Add($"{prefix}[operator]={Encode(node.Operator!)}");
        AppendParameter(parts, $"{prefix}[value]", node.Value);
    }

    private static void AppendFilterGroup(List<string> parts, string prefix, FilterNode group)
    {
        parts.Add($"{prefix}[logic]={group.Logic}");
        for (var i = 0; i < group.Children.Count; i++)
            AppendFilterNode(parts, $"{prefix}[filters][{i}]", group.Children[i]);
    }

    private static void AppendParameter(List<string> parts, string key, object? value)
    {
        if (value is null)
        {
            parts.Add($"{key}=");
            return;
        }

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry item in dictionary)
                AppendParameter(parts, $"{key}[{Encode(Convert.ToString(item.Key, CultureInfo.InvariantCulture) ?? string.Empty)}]", item.Value);
            return;
        }

        if (value is IEnumerable enumerable and not string)
        {
            var index = 0;
            foreach (var item in enumerable)
            {
                AppendParameter(parts, $"{key}[{index}]", item);
                index++;
            }
            return;
        }

        parts.Add($"{key}={FormatValue(value)}");
    }

    private static string FormatValue(object value) => value switch
    {
        DateTime date => Encode(date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
        DateTimeOffset date => Encode(date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
        bool boolean => boolean ? "1" : "0",
        IFormattable formattable => Encode(formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty),
        _ => Encode(value.ToString() ?? string.Empty)
    };

    private static object? CloneValue(object? value)
    {
        if (value is null or string || value.GetType().IsValueType)
            return value;

        if (value is IDictionary dictionary)
        {
            var clone = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (DictionaryEntry item in dictionary)
            {
                var key = Convert.ToString(item.Key, CultureInfo.InvariantCulture) ?? string.Empty;
                clone[key] = CloneValue(item.Value);
            }
            return clone;
        }

        if (value is IEnumerable enumerable)
            return enumerable.Cast<object?>().Select(CloneValue).ToList();

        // Arbitrary objects are treated as immutable scalar parameter values.
        return value;
    }

    private static string Encode(string value) => HttpUtility.UrlEncode(value);
}
