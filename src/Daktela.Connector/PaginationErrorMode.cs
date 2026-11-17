namespace Daktela.Connector;

/// <summary>
/// Controls what a <see cref="DaktelaPaginator{T}"/> does when a page request fails.
/// </summary>
public enum PaginationErrorMode
{
    /// <summary>
    /// Propagate the failure to the caller. This is the default.
    /// </summary>
    Throw,

    /// <summary>
    /// End the enumeration silently. The caller cannot distinguish a failure from a
    /// complete result, so use this only where partial data is acceptable.
    /// </summary>
    Stop,

    /// <summary>
    /// Skip the failed page and continue with the next one. Authentication failures are
    /// still propagated because they are not specific to a page.
    /// </summary>
    Skip
}
