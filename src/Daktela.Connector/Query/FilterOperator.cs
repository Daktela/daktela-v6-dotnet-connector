namespace Daktela.Connector.Query;

/// <summary>
/// Filter comparison operators supported by the Daktela API.
/// </summary>
public enum FilterOperator
{
    /// <summary>
    /// Equal to (=).
    /// </summary>
    Eq,

    /// <summary>
    /// Not equal to (!=).
    /// </summary>
    Neq,

    /// <summary>
    /// Greater than.
    /// </summary>
    Gt,

    /// <summary>
    /// Greater than or equal to.
    /// </summary>
    Gte,

    /// <summary>
    /// Less than.
    /// </summary>
    Lt,

    /// <summary>
    /// Less than or equal to.
    /// </summary>
    Lte,

    /// <summary>
    /// Like pattern matching (SQL LIKE).
    /// </summary>
    Like,

    /// <summary>
    /// Value is in a list.
    /// </summary>
    In,

    /// <summary>
    /// Value is not in a list.
    /// </summary>
    NotIn
}

internal static class FilterOperatorExtensions
{
    public static string ToApiString(this FilterOperator op) => op switch
    {
        FilterOperator.Eq => "eq",
        FilterOperator.Neq => "neq",
        FilterOperator.Gt => "gt",
        FilterOperator.Gte => "gte",
        FilterOperator.Lt => "lt",
        FilterOperator.Lte => "lte",
        FilterOperator.Like => "like",
        FilterOperator.In => "in",
        FilterOperator.NotIn => "notIn",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown filter operator")
    };
}
