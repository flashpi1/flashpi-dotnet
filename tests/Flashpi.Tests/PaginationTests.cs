using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flashpi.Models;
using Xunit;

namespace Flashpi.Tests;

public sealed class PaginationTests
{
    [Fact]
    public async Task Awaits_to_the_first_page()
    {
        var api = new FakeApi(Reply.Page([Samples.Document(), Samples.Document(Samples.OtherId)], "c2"));
        using var test = new Harness(api);
        var page = await test.Client.Documents.ListAsync(new DocumentListParams { Limit = 2 });
        Assert.Equal(2, page.Data.Count);
        Assert.True(page.HasMore);
        Assert.Equal("c2", page.NextCursor);
        Assert.True(page.HasNextPage);
        Assert.Equal("?limit=2", api.Last.Query);
        Assert.Equal("Page(Data=<2 items>, HasMore=True, NextCursor=c2)", page.ToString());
    }

    [Fact]
    public async Task Fetches_the_next_page_with_the_same_filters_and_options()
    {
        var api = new FakeApi(Reply.Page([Samples.Document()], "c 2+"), Reply.Page([Samples.Document(Samples.OtherId)]));
        using var test = new Harness(api);
        var options = new RequestOptions { Language = Language.Spanish };
        var page = await test.Client.Documents.ListAsync(new DocumentListParams { Status = DocumentStatus.Authorized, IssueDateFrom = "2026-11-01" }, options);
        var next = await page.GetNextPageAsync();
        Assert.NotNull(next);
        Assert.Equal(Samples.OtherId, next!.Data[0].Id);
        Assert.False(next.HasNextPage);
        Assert.Null(await next.GetNextPageAsync());
        Assert.Equal("?status=authorized&issueDateFrom=2026-11-01&cursor=c+2%2B", api.Last.Url.Query);
        Assert.Equal("es", api.Last.Header("Accept-Language"));
    }

    [Fact]
    public async Task Iterates_every_item_across_pages()
    {
        var api = new FakeApi(
            Reply.Page([Samples.Document("a"), Samples.Document("b")], "c2"),
            Reply.Page([Samples.Document("c")], "c3"),
            Reply.Page([]));
        using var test = new Harness(api);
        var ids = new List<string>();
        await foreach (var document in test.Client.Documents.ListAsync())
        {
            ids.Add(document.Id);
        }
        Assert.Equal(new[] { "a", "b", "c" }, ids);
        Assert.Equal(3, api.Requests.Count);
        Assert.Equal("", api.Requests[0].Query);
        Assert.Equal("?cursor=c3", api.Requests[2].Query);
    }

    [Fact]
    public async Task Iterates_from_an_awaited_page_and_reuses_the_first_page()
    {
        var api = new FakeApi(Reply.Page([Samples.Document("a")], "c2"), Reply.Page([Samples.Document("b")]));
        using var test = new Harness(api);
        var list = test.Client.Documents.ListAsync();
        var first = await list;
        Assert.Same(first, await list.GetFirstPageAsync());
        var ids = new List<string>();
        await foreach (var document in list)
        {
            ids.Add(document.Id);
        }
        Assert.Equal(new[] { "a", "b" }, ids);
        Assert.Equal(2, api.Requests.Count);
    }

    [Fact]
    public async Task Tries_a_failed_first_page_again()
    {
        var api = new FakeApi(Reply.Problem(404, "not_found"), Reply.Page([Samples.Document()]));
        using var test = new Harness(api);
        var list = test.Client.Documents.ListAsync();
        await Assert.ThrowsAsync<NotFoundException>(async () => await list);
        Assert.Single((await list).Data);
    }

    [Fact]
    public async Task Starts_from_a_given_cursor_and_encodes_query_values()
    {
        var api = new FakeApi(Reply.Page([]));
        using var test = new Harness(api);
        await test.Client.Events.ListAsync(new EventListParams { Cursor = "abc", Type = EventType.InvoiceAuthorized, DocumentId = "a&b=c d" });
        Assert.Equal("?cursor=abc&type=invoice.authorized&documentId=a%26b%3Dc+d", api.Last.Url.Query);
    }

    [Fact]
    public async Task Does_not_follow_a_page_that_says_has_more_without_a_cursor()
    {
        var api = new FakeApi(Reply.Json(new { data = new[] { Samples.Document() }, pagination = new { nextCursor = (string?)null, hasMore = true } }));
        using var test = new Harness(api);
        var count = 0;
        await foreach (var _ in test.Client.Documents.ListAsync())
        {
            count++;
        }
        Assert.Equal(1, count);
        Assert.Single(api.Requests);
    }

    [Fact]
    public async Task Exposes_the_response_of_a_page_with_WithResponse()
    {
        var api = new FakeApi(Reply.Page([Samples.Document()], "c2"), Reply.Page([]));
        using var test = new Harness(api);
        var result = await test.Client.Documents.WithResponse.ListAsync(new DocumentListParams { Limit = 1 });
        Assert.Equal(Reply.RequestId, result.RequestId);
        Assert.Single(result.Data.Data);
        Assert.NotNull(await result.Data.GetNextPageAsync());
        Assert.Equal("?limit=1&cursor=c2", api.Last.Query);
    }

    [Fact]
    public async Task Refuses_a_list_answer_without_data_and_pagination()
    {
        var api = new FakeApi(Reply.Json(new { items = Array.Empty<object>() }));
        using var test = new Harness(api);
        var error = await Assert.ThrowsAsync<FlashpiException>(async () => await test.Client.Documents.ListAsync());
        Assert.Equal("The API answered a list without data and pagination", error.Message);
    }

    [Fact]
    public async Task Stops_when_the_caller_breaks_out_of_the_loop()
    {
        var api = new FakeApi(Reply.Page([Samples.Document("a"), Samples.Document("b")], "c2"));
        using var test = new Harness(api);
        await foreach (var document in test.Client.Documents.ListAsync())
        {
            Assert.Equal("a", document.Id);
            break;
        }
        Assert.Single(api.Requests);
    }

    [Fact]
    public async Task Cancels_the_walk_with_the_enumerators_token_or_the_lists()
    {
        var api = new FakeApi(Reply.Page([Samples.Document("a")], "c2"), Reply.Page([Samples.Document("b")], "c3"));
        using var test = new Harness(api);
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in test.Client.Documents.ListAsync().WithCancellation(cancel.Token))
            {
                cancel.Cancel();
            }
        });
        Assert.InRange(api.Requests.Count, 1, 2);

        using var listCancel = new CancellationTokenSource();
        api.Enqueue(Reply.Page([Samples.Document("a")], "c2"), Reply.Page([Samples.Document("b")], "c3"), Reply.Page([Samples.Document("c")], "c4"));
        var list = test.Client.Documents.ListAsync(cancellationToken: listCancel.Token);
        var page = await list;
        listCancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => page.GetNextPageAsync());
    }

    [Fact]
    public async Task Lists_events_of_every_kind()
    {
        var api = new FakeApi(Reply.Page(
        [
            new { id = "e1", type = "invoice.authorized", environment = "test", createdAt = "2026-11-13T12:00:00Z", data = Samples.Document() },
            new { id = "e2", type = "certificate.revoked", environment = "live", createdAt = "2026-11-13T12:00:00Z", data = new { id = Samples.Id } },
            new { id = "e3", type = "account.email_paused", environment = "live", createdAt = "2026-11-13T12:00:00Z", data = new { status = "paused" } },
            new { id = "e4", type = "something.new", environment = "test", createdAt = "2026-11-13T12:00:00Z", data = new { a = 1 } },
        ]));
        using var test = new Harness(api);
        var events = new List<Event>();
        await foreach (var item in test.Client.Events.ListAsync())
        {
            events.Add(item);
        }
        Assert.IsType<DocumentEvent>(events[0]);
        Assert.Equal(Samples.Id, ((DocumentEvent)events[0]).Data.Id);
        Assert.IsType<CertificateEvent>(events[1]);
        Assert.Equal(FlashpiEnvironment.Live, events[1].Environment);
        Assert.IsType<AccountEvent>(events[2]);
        var unknown = Assert.IsType<UnknownEvent>(events[3]);
        Assert.Equal("something.new", unknown.Type.Value);
        Assert.Equal(1, unknown.AdditionalProperties!["data"].GetProperty("a").GetInt32());
        Assert.Equal(new[] { "e1", "e2", "e3", "e4" }, events.Select(item => item.Id));
    }
}
