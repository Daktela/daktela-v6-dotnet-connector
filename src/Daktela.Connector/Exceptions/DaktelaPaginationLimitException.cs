namespace Daktela.Connector.Exceptions;

/// <summary>
/// Exception thrown when a pagination operation reaches the configured page limit
/// (<see cref="DaktelaConfig.MaxPages"/>) while more records may remain.
/// </summary>
public class DaktelaPaginationLimitException : DaktelaException
{
    /// <summary>
    /// The page limit that was reached.
    /// </summary>
    public int MaxPages { get; }

    public DaktelaPaginationLimitException(int maxPages)
        : base(
            $"Pagination stopped after {maxPages} page requests and more records may remain. " +
            "Narrow the query, raise the page size, or raise DaktelaConfig.MaxPages.")
    {
        MaxPages = maxPages;
    }
}
