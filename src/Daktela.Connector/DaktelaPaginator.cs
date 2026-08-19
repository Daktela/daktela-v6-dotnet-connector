using System.Runtime.CompilerServices;
using Daktela.Connector.Exceptions;
using Daktela.Connector.Query;

namespace Daktela.Connector;

/// <summary>
/// Reusable, memory-efficient pagination helper for Daktela list endpoints.
/// </summary>
public sealed class DaktelaPaginator<T> : IAsyncEnumerable<T>
{
    private readonly DaktelaClient _client;
    private readonly string _endpoint;
    private readonly QueryBuilder _query;
    private readonly int _pageSize;
    private readonly int? _maxItems;
    private readonly bool _stopOnError;

    internal DaktelaPaginator(
        DaktelaClient client,
        string endpoint,
        QueryBuilder? query,
        int pageSize,
        int? maxItems,
        bool stopOnError)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be greater than zero.");
        if (maxItems < 0)
            throw new ArgumentOutOfRangeException(nameof(maxItems), "Maximum items must be non-negative.");

        _client = client;
        _endpoint = endpoint;
        _query = query?.Clone() ?? new QueryBuilder();
        _pageSize = pageSize;
        _maxItems = maxItems ?? query?.GetTake();
        _stopOnError = stopOnError;
    }

    /// <summary>
    /// Iterates over individual records.
    /// </summary>
    public async IAsyncEnumerable<T> ItemsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var yielded = 0;
        await foreach (var page in PagesAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var item in page.Data ?? [])
            {
                yield return item;
                yielded++;
                if (_maxItems.HasValue && yielded >= _maxItems.Value)
                    yield break;
            }
        }
    }

    /// <summary>
    /// Iterates over full page responses, including total and status metadata.
    /// </summary>
    public async IAsyncEnumerable<DaktelaResponse<List<T>>> PagesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_maxItems == 0)
            yield break;

        var skip = _query.GetSkip() ?? 0;
        var returned = 0;
        var pageRequests = 0;
        while (pageRequests++ < DaktelaClient.ReadLimit)
        {
            var remaining = _maxItems.HasValue ? _maxItems.Value - returned : int.MaxValue;
            if (remaining <= 0)
                yield break;

            var requestedPageSize = Math.Min(_pageSize, remaining);
            var pageQuery = _query.WithTake(requestedPageSize).WithSkip(skip);
            DaktelaResponse<List<T>> response;
            try
            {
                response = await _client.GetAsync<T>(
                    _endpoint,
                    pageQuery,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (DaktelaException) when (_stopOnError)
            {
                yield break;
            }
            catch (DaktelaException)
            {
                skip += requestedPageSize;
                continue;
            }

            yield return response;

            var count = response.Data?.Count ?? 0;
            if (count == 0)
                yield break;

            returned += count;
            if (_maxItems.HasValue && returned >= _maxItems.Value)
                yield break;

            // Daktela defaults a missing envelope total to 1. Treat total as an
            // authoritative pagination boundary only when it matches the page position.
            var reachedTotal = response.Total.HasValue && skip + count == response.Total.Value;
            if (reachedTotal)
                yield break;
            if (!response.Total.HasValue && count < requestedPageSize)
                yield break;

            skip += count;
        }
    }

    /// <summary>
    /// Materializes all yielded items.
    /// </summary>
    public async Task<List<T>> ToListAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<T>();
        await foreach (var item in ItemsAsync(cancellationToken).ConfigureAwait(false))
            result.Add(item);
        return result;
    }

    /// <summary>
    /// Materializes all yielded items into an array.
    /// </summary>
    public async Task<T[]> ToArrayAsync(CancellationToken cancellationToken = default)
        => (await ToListAsync(cancellationToken).ConfigureAwait(false)).ToArray();

    /// <summary>
    /// Counts all yielded items.
    /// </summary>
    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        var count = 0;
        await foreach (var _ in ItemsAsync(cancellationToken).ConfigureAwait(false))
            count++;
        return count;
    }

    /// <summary>
    /// Returns the first item, or the default value when no item exists.
    /// </summary>
    public async Task<T?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var item in ItemsAsync(cancellationToken).ConfigureAwait(false))
            return item;
        return default;
    }

    /// <summary>
    /// Convenience alias for <see cref="FirstOrDefaultAsync"/>.
    /// </summary>
    public Task<T?> FirstAsync(CancellationToken cancellationToken = default)
        => FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Determines whether the result contains no items.
    /// </summary>
    public async Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var _ in ItemsAsync(cancellationToken).ConfigureAwait(false))
            return false;
        return true;
    }

    /// <summary>
    /// Invokes an action for every item and its zero-based index.
    /// </summary>
    public async Task ForEachAsync(
        Action<T, int> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        var index = 0;
        await foreach (var item in ItemsAsync(cancellationToken).ConfigureAwait(false))
            action(item, index++);
    }

    /// <summary>
    /// Convenience alias for <see cref="ForEachAsync"/>.
    /// </summary>
    public Task EachAsync(
        Action<T, int> action,
        CancellationToken cancellationToken = default)
        => ForEachAsync(action, cancellationToken);

    /// <summary>
    /// Lazily filters yielded items.
    /// </summary>
    public IAsyncEnumerable<T> Filter(Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return FilterCore(this, predicate);
    }

    /// <summary>
    /// Lazily transforms yielded items.
    /// </summary>
    public IAsyncEnumerable<TResult> Map<TResult>(Func<T, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return MapCore(this, selector);
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => ItemsAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);

    private static async IAsyncEnumerable<T> FilterCore(
        IAsyncEnumerable<T> source,
        Func<T, bool> predicate,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (predicate(item))
                yield return item;
        }
    }

    private static async IAsyncEnumerable<TResult> MapCore<TResult>(
        IAsyncEnumerable<T> source,
        Func<T, TResult> selector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return selector(item);
    }
}
