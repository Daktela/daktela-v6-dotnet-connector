namespace Daktela.Connector.Query;

/// <summary>
/// Represents a sort condition for API queries.
/// </summary>
public class Sort
{
    /// <summary>
    /// The field name to sort by.
    /// </summary>
    public string Field { get; }

    /// <summary>
    /// The sort direction.
    /// </summary>
    public SortDirection Direction { get; }

    public Sort(string field, SortDirection direction = SortDirection.Asc)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field name cannot be null or empty.", nameof(field));

        Field = field;
        Direction = direction;
    }

    /// <summary>
    /// Creates an ascending sort.
    /// </summary>
    public static Sort Asc(string field) => new(field, SortDirection.Asc);

    /// <summary>
    /// Creates a descending sort.
    /// </summary>
    public static Sort Desc(string field) => new(field, SortDirection.Desc);
}
