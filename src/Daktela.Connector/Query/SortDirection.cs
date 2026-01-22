namespace Daktela.Connector.Query;

/// <summary>
/// Sort direction for query results.
/// </summary>
public enum SortDirection
{
    /// <summary>
    /// Ascending order.
    /// </summary>
    Asc,

    /// <summary>
    /// Descending order.
    /// </summary>
    Desc
}

internal static class SortDirectionExtensions
{
    public static string ToApiString(this SortDirection direction) => direction switch
    {
        SortDirection.Asc => "asc",
        SortDirection.Desc => "desc",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown sort direction")
    };
}
