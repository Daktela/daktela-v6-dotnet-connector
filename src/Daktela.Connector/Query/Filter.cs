namespace Daktela.Connector.Query;

/// <summary>
/// Represents a filter condition for API queries.
/// </summary>
public class Filter
{
    /// <summary>
    /// The field name to filter on.
    /// </summary>
    public string Field { get; }

    /// <summary>
    /// The comparison operator.
    /// </summary>
    public FilterOperator Operator { get; }

    /// <summary>
    /// The value to compare against.
    /// </summary>
    public object Value { get; }

    public Filter(string field, FilterOperator op, object value)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field name cannot be null or empty.", nameof(field));

        Field = field;
        Operator = op;
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Creates an equality filter.
    /// </summary>
    public static Filter Eq(string field, object value) => new(field, FilterOperator.Eq, value);

    /// <summary>
    /// Creates a not-equal filter.
    /// </summary>
    public static Filter Neq(string field, object value) => new(field, FilterOperator.Neq, value);

    /// <summary>
    /// Creates a greater-than filter.
    /// </summary>
    public static Filter Gt(string field, object value) => new(field, FilterOperator.Gt, value);

    /// <summary>
    /// Creates a greater-than-or-equal filter.
    /// </summary>
    public static Filter Gte(string field, object value) => new(field, FilterOperator.Gte, value);

    /// <summary>
    /// Creates a less-than filter.
    /// </summary>
    public static Filter Lt(string field, object value) => new(field, FilterOperator.Lt, value);

    /// <summary>
    /// Creates a less-than-or-equal filter.
    /// </summary>
    public static Filter Lte(string field, object value) => new(field, FilterOperator.Lte, value);

    /// <summary>
    /// Creates a LIKE pattern filter.
    /// </summary>
    public static Filter Like(string field, string pattern) => new(field, FilterOperator.Like, pattern);

    /// <summary>
    /// Creates a contains filter.
    /// </summary>
    public static Filter Contains(string field, string value) => new(field, FilterOperator.Contains, value);

    /// <summary>
    /// Creates a starts-with filter.
    /// </summary>
    public static Filter StartsWith(string field, string value) => new(field, FilterOperator.StartsWith, value);

    /// <summary>
    /// Creates an ends-with filter.
    /// </summary>
    public static Filter EndsWith(string field, string value) => new(field, FilterOperator.EndsWith, value);

    /// <summary>
    /// Creates a not-like filter.
    /// </summary>
    public static Filter NotLike(string field, string pattern) => new(field, FilterOperator.NotLike, pattern);

    /// <summary>
    /// Creates a does-not-contain filter.
    /// </summary>
    public static Filter DoesNotContain(string field, string value) => new(field, FilterOperator.DoesNotContain, value);

    /// <summary>
    /// Creates an is-null filter.
    /// </summary>
    public static Filter IsNull(string field) => new(field, FilterOperator.IsNull, string.Empty);

    /// <summary>
    /// Creates an is-not-null filter.
    /// </summary>
    public static Filter IsNotNull(string field) => new(field, FilterOperator.IsNotNull, string.Empty);

    /// <summary>
    /// Creates an IN filter.
    /// </summary>
    public static Filter In(string field, params object[] values) => new(field, FilterOperator.In, values);

    /// <summary>
    /// Creates a NOT IN filter.
    /// </summary>
    public static Filter NotIn(string field, params object[] values) => new(field, FilterOperator.NotIn, values);
}
