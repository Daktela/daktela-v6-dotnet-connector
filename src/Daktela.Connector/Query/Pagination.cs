namespace Daktela.Connector.Query;

/// <summary>
/// Represents pagination parameters for API queries.
/// </summary>
public class Pagination
{
    /// <summary>
    /// The number of records to skip.
    /// </summary>
    public int? Skip { get; set; }

    /// <summary>
    /// The maximum number of records to return.
    /// </summary>
    public int? Take { get; set; }

    public Pagination()
    {
    }

    public Pagination(int? skip, int? take)
    {
        Skip = skip;
        Take = take;
    }
}
