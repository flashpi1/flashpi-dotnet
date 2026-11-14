using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Flashpi.Internal;

namespace Flashpi;

/// <summary>
/// One page of a list, newest first (establishments and emission points: by code). Walk this page
/// and every following one with <c>await foreach</c>, which fetches them as needed.
/// </summary>
/// <typeparam name="T">The item.</typeparam>
public sealed class Page<T> : IAsyncEnumerable<T>
{
    private readonly Func<string, CancellationToken, Task<Page<T>>> _fetchNext;
    private readonly CancellationToken _listToken;

    internal Page(IReadOnlyList<T>? data, Models.Pagination? pagination, Func<string, CancellationToken, Task<Page<T>>> fetchNext, CancellationToken listToken)
    {
        if (data is null || pagination is null)
        {
            throw new FlashpiException("The API answered a list without data and pagination");
        }
        Data = data;
        HasMore = pagination.HasMore;
        NextCursor = pagination.NextCursor;
        _fetchNext = fetchNext;
        _listToken = listToken;
    }

    /// <summary>The items of this page.</summary>
    public IReadOnlyList<T> Data { get; }

    /// <summary>Whether more items follow this page.</summary>
    public bool HasMore { get; }

    /// <summary>The cursor of the next page, or null on the last one.</summary>
    public string? NextCursor { get; }

    /// <summary>Whether <see cref="GetNextPageAsync"/> returns a page.</summary>
    public bool HasNextPage => HasMore && NextCursor is not null;

    /// <summary>The next page (same filters and options), or null after the last page.</summary>
    /// <param name="cancellationToken">Cancels the request, together with the list's token.</param>
    public async Task<Page<T>?> GetNextPageAsync(CancellationToken cancellationToken = default)
    {
        if (!HasNextPage)
        {
            return null;
        }
        using var linked = Tokens.Link(_listToken, cancellationToken, out var token);
        return await _fetchNext(NextCursor!, token).ConfigureAwait(false);
    }

    /// <summary>Walks this page and every following one, fetching them as needed.</summary>
    /// <param name="cancellationToken">Cancels the walk, together with the list's token.</param>
    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        for (var page = this; page is not null; page = await page.GetNextPageAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var item in page.Data)
            {
                yield return item;
            }
        }
    }

    /// <summary>A summary of the page.</summary>
    public override string ToString() => $"Page(Data=<{Data.Count} items>, HasMore={HasMore}, NextCursor={NextCursor})";
}

/// <summary>
/// The result of a list method. Walk every item across pages with <c>await foreach</c>, or
/// <c>await</c> it for the first page:
/// <code>
/// await foreach (var document in flashpi.Documents.ListAsync(new() { Status = DocumentStatus.Authorized }))
/// {
///     Console.WriteLine(document.AccessKey);
/// }
///
/// var page = await flashpi.Documents.ListAsync(new() { Limit = 50 });
/// </code>
/// </summary>
/// <typeparam name="T">The item.</typeparam>
public sealed class Paginator<T> : IAsyncEnumerable<T>
{
    private readonly Func<CancellationToken, Task<Page<T>>> _fetchFirst;
    private readonly CancellationToken _listToken;
    private Page<T>? _first;

    internal Paginator(Func<CancellationToken, Task<Page<T>>> fetchFirst, CancellationToken listToken)
    {
        _fetchFirst = fetchFirst;
        _listToken = listToken;
    }

    /// <summary>The first page, fetched once (a failed fetch is tried again on the next call).</summary>
    /// <param name="cancellationToken">Cancels the request, together with the list's token.</param>
    public async Task<Page<T>> GetFirstPageAsync(CancellationToken cancellationToken = default)
    {
        if (_first is { } page)
        {
            return page;
        }
        using var linked = Tokens.Link(_listToken, cancellationToken, out var token);
        page = await _fetchFirst(token).ConfigureAwait(false);
        _first = page;
        return page;
    }

    /// <summary>Makes <c>await flashpi.Documents.ListAsync()</c> return the first page.</summary>
    public TaskAwaiter<Page<T>> GetAwaiter() => GetFirstPageAsync().GetAwaiter();

    /// <summary>Walks every item across pages, fetching them as needed.</summary>
    /// <param name="cancellationToken">Cancels the walk, together with the list's token.</param>
    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        var page = await GetFirstPageAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var item in page.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }
}
