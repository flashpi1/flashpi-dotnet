using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Flashpi.Models;
using Xunit;

namespace Flashpi.Tests;

public sealed class RetriesTests
{
    [Fact]
    public async Task Retries_a_500_with_the_same_key_waiting_half_a_second_then_one()
    {
        var api = new FakeApi(Reply.Text("bad gateway", 502), Reply.Problem(500, "internal_error"), Reply.Json(Samples.Document(), 202));
        using var test = new Harness(api);
        var document = await test.Client.Invoices.CreateAsync(Samples.Invoice());
        Assert.Equal(Samples.Id, document.Id);
        Assert.Equal(3, api.Requests.Count);
        Assert.Single(api.Requests.Select(sent => sent.Header("Idempotency-Key")).Distinct());
        Assert.Equal(new[] { 0.5, 1.0 }, Seconds.All(test.Waits));
        Assert.Equal(api.Requests[0].BodyText, api.Requests[2].BodyText);
    }

    [Fact]
    public async Task Throws_the_last_error_once_the_retries_run_out()
    {
        var api = new FakeApi(Reply.Problem(503, "service_unavailable"), Reply.Problem(503, "service_unavailable"), Reply.Problem(500, "internal_error"));
        using var test = new Harness(api);
        var error = await Assert.ThrowsAsync<InternalServerException>(() => test.Client.Documents.GetAsync(Samples.Id));
        Assert.Equal(500, error.StatusCode);
        Assert.Equal(3, api.Requests.Count);
    }

    [Fact]
    public async Task Honors_max_retries_of_the_client_and_of_the_call()
    {
        var api = new FakeApi(Reply.Problem(500, "internal_error"), Reply.Problem(500, "internal_error"), Reply.Problem(500, "internal_error"));
        using var test = new Harness(api, options => { options.MaxRetries = 0; return options; });
        await Assert.ThrowsAsync<InternalServerException>(() => test.Client.Documents.GetAsync(Samples.Id));
        Assert.Single(api.Requests);
        await Assert.ThrowsAsync<InternalServerException>(() => test.Client.Documents.GetAsync(Samples.Id, new RequestOptions { MaxRetries = 1 }));
        Assert.Equal(3, api.Requests.Count);
    }

    [Fact]
    public async Task Retries_network_errors_and_wraps_the_last_one()
    {
        var api = new FakeApi(Reply.NetworkError(), Reply.NetworkError(), Reply.NetworkError("No such host"));
        using var test = new Harness(api);
        var error = await Assert.ThrowsAsync<ApiConnectionException>(() => test.Client.Invoices.CreateAsync(Samples.Invoice()));
        Assert.Equal("POST https://api.flashpi.com.ec/v1/invoices failed: No such host", error.Message);
        Assert.IsType<HttpRequestException>(error.InnerException);
        Assert.NotNull(error.IdempotencyKey);
        Assert.Equal(error.IdempotencyKey, api.Requests[0].Header("Idempotency-Key"));
    }

    [Fact]
    public async Task Times_out_a_slow_attempt_and_retries_it()
    {
        var api = new FakeApi(SlowAnswer(), Reply.Json(Samples.Document()));
        using var test = new Harness(api, options => { options.Timeout = TimeSpan.FromMilliseconds(50); return options; });
        var document = await test.Client.Documents.GetAsync(Samples.Id);
        Assert.Equal(Samples.Id, document.Id);
        Assert.Equal(2, api.Requests.Count);
    }

    [Fact]
    public async Task Throws_a_timeout_error_when_every_attempt_times_out()
    {
        var api = new FakeApi(SlowAnswer(), SlowAnswer(), SlowAnswer());
        using var test = new Harness(api, options => { options.Timeout = TimeSpan.FromMilliseconds(30); return options; });
        var error = await Assert.ThrowsAsync<ApiTimeoutException>(() => test.Client.Documents.GetAsync(Samples.Id));
        Assert.Equal($"GET https://api.flashpi.com.ec/v1/documents/{Samples.Id} timed out after 0.03 seconds", error.Message);
        Assert.Equal(3, api.Requests.Count);
    }

    [Fact]
    public async Task Never_retries_a_call_cancelled_by_its_token()
    {
        using var cancel = new CancellationTokenSource();
        var api = new FakeApi(new Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>(async (_, token) =>
        {
            cancel.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return Reply.Empty(200);
        }));
        using var test = new Harness(api);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test.Client.Invoices.CreateAsync(Samples.Invoice(), cancellationToken: cancel.Token));
        Assert.Equal(cancel.Token, error.CancellationToken);
        Assert.Single(api.Requests);
    }

    [Fact]
    public async Task Waits_a_short_retry_after_of_a_429()
    {
        var api = new FakeApi(Reply.Problem(429, "rate_limited", null, ("Retry-After", "3")), Reply.Json(Samples.Document()));
        using var test = new Harness(api);
        await test.Client.Documents.GetAsync(Samples.Id);
        Assert.Equal(new[] { 3.0 }, Seconds.All(test.Waits));
    }

    [Fact]
    public async Task Does_not_wait_for_a_daily_quota_and_reports_when_it_resets()
    {
        var api = new FakeApi(Reply.Problem(429, "demo_issuer_quota_exceeded", new { limit = 50, resetsAt = "2026-11-14T05:00:00Z" }, ("Retry-After", "61")));
        using var test = new Harness(api);
        var error = await Assert.ThrowsAsync<RateLimitException>(() => test.Client.Invoices.CreateAsync(Samples.Invoice()));
        Assert.Equal(TimeSpan.FromSeconds(61), error.RetryAfter);
        Assert.Equal(50, error.Problem!.Limit);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 5, 0, 0, TimeSpan.Zero), error.Problem.ResetsAt);
        Assert.Single(api.Requests);
        Assert.Empty(test.Waits);
    }

    [Fact]
    public async Task Reads_a_retry_after_given_as_an_http_date()
    {
        var api = new FakeApi(Reply.Problem(503, "service_unavailable", null, ("Retry-After", "Fri, 13 Nov 2026 12:00:05 GMT")), Reply.Json(Samples.Document()));
        using var test = new Harness(api);
        await test.Client.Documents.GetAsync(Samples.Id);
        Assert.Equal(new[] { 5.0 }, Seconds.All(test.Waits));
    }

    [Fact]
    public async Task Retries_a_409_in_progress_after_at_least_a_second()
    {
        var api = new FakeApi(Reply.Problem(409, "idempotency_key_in_progress"), Reply.Json(Samples.Document(), 202));
        using var test = new Harness(api);
        await test.Client.Invoices.CreateAsync(Samples.Invoice());
        Assert.Equal(new[] { 1.0 }, Seconds.All(test.Waits));
    }

    [Theory]
    [InlineData(409, "sequential_already_used")]
    [InlineData(503, "demo_issuer_unavailable")]
    [InlineData(501, "not_implemented")]
    [InlineData(422, "validation_failed")]
    [InlineData(400, "malformed_json")]
    public async Task Does_not_retry(int status, string code)
    {
        var api = new FakeApi(Reply.Problem(status, code));
        using var test = new Harness(api);
        var error = await Assert.ThrowsAnyAsync<ApiException>(() => test.Client.Invoices.CreateAsync(Samples.Invoice()));
        Assert.Equal(code, error.Code?.Value);
        Assert.Single(api.Requests);
    }

    [Fact]
    public async Task Retries_a_408()
    {
        var api = new FakeApi(Reply.Empty(408), Reply.Json(Samples.Document()));
        using var test = new Harness(api);
        await test.Client.Documents.GetAsync(Samples.Id);
        Assert.Equal(2, api.Requests.Count);
    }

    [Fact]
    public async Task Caps_the_backoff_at_8_seconds_and_takes_off_up_to_a_quarter_of_jitter()
    {
        var replies = Enumerable.Range(0, 7).Select(_ => (object)Reply.Problem(500, "internal_error")).Append(Reply.Json(Samples.Document())).ToArray();
        var api = new FakeApi(replies);
        using var test = new Harness(api, options => { options.MaxRetries = 7; return options; }, random: 0.2);
        await test.Client.Documents.GetAsync(Samples.Id);
        Assert.Equal(new[] { 0.475, 0.95, 1.9, 3.8, 7.6, 7.6, 7.6 }, Seconds.All(test.Waits));
    }

    [Fact]
    public async Task Retries_patch_and_delete_too()
    {
        var api = new FakeApi(Reply.NetworkError(), Reply.Json(new { id = Samples.Id }), Reply.NetworkError(), Reply.Empty(204));
        using var test = new Harness(api);
        await test.Client.Issuers.UpdateAsync(Samples.Id, new UpdateIssuer { LegalName = "X" });
        await test.Client.Issuers.DeleteAsync(Samples.Id);
        Assert.Equal(new[] { "PATCH", "PATCH", "DELETE", "DELETE" }, api.Requests.Select(sent => sent.Method));
    }

    [Fact]
    public async Task Sends_the_same_body_on_a_retry_when_the_caller_changes_its_lists()
    {
        var items = new System.Collections.Generic.List<DocumentItem>(Samples.Invoice().Items);
        var body = Samples.Invoice() with { Items = items };
        var api = new FakeApi(new Func<HttpRequestMessage, HttpResponseMessage>(_ =>
        {
            items.Clear();
            return Reply.Problem(500, "internal_error");
        }), Reply.Json(Samples.Document(), 202));
        using var test = new Harness(api);
        await test.Client.Invoices.CreateAsync(body);
        Assert.Equal(api.Requests[0].BodyText, api.Requests[1].BodyText);
    }

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> SlowAnswer() => async (_, token) =>
    {
        await Task.Delay(Timeout.Infinite, token);
        return Reply.Empty(200);
    };
}

public sealed class RecoveryTests
{
    [Fact]
    public async Task Returns_the_document_when_a_retry_finds_its_key_used_by_it()
    {
        var api = new FakeApi(
            Reply.NetworkError(),
            Reply.Problem(422, "idempotency_key_reused", new { documentId = Samples.OtherId }),
            Reply.Json(Samples.Document(Samples.OtherId)));
        using var test = new Harness(api);
        var result = await test.Client.Invoices.WithResponse.CreateAsync(Samples.Invoice(), new RequestOptions { IdempotencyKey = "k-1", Language = Language.Spanish });
        Assert.Equal(Samples.OtherId, result.Data.Id);
        Assert.Equal(200, result.StatusCode);
        var read = api.Last;
        Assert.Equal("GET", read.Method);
        Assert.Equal($"/v1/documents/{Samples.OtherId}", read.Path);
        Assert.Null(read.Header("Idempotency-Key"));
        Assert.Equal("es", read.Header("Accept-Language"));
    }

    [Fact]
    public async Task Throws_the_reuse_on_the_first_attempt_or_without_a_document_id()
    {
        var api = new FakeApi(
            Reply.Problem(422, "idempotency_key_reused", new { documentId = Samples.OtherId }),
            Reply.NetworkError(),
            Reply.Problem(422, "idempotency_key_reused"));
        using var test = new Harness(api);
        await Assert.ThrowsAsync<UnprocessableEntityException>(() => test.Client.Invoices.CreateAsync(Samples.Invoice()));
        await Assert.ThrowsAsync<UnprocessableEntityException>(() => test.Client.Invoices.CreateAsync(Samples.Invoice()));
        Assert.Equal(3, api.Requests.Count);
    }

    [Fact]
    public async Task Does_not_apply_to_other_posts()
    {
        var api = new FakeApi(Reply.NetworkError(), Reply.Problem(422, "idempotency_key_reused", new { documentId = Samples.OtherId }));
        using var test = new Harness(api);
        await Assert.ThrowsAsync<UnprocessableEntityException>(() => test.Client.WebhookEndpoints.CreateAsync(
            new CreateWebhookEndpoint { Url = "https://example.com/hook", EnabledEvents = [EventType.All] }));
        Assert.Equal(2, api.Requests.Count);
    }

    [Fact]
    public async Task Keeps_the_key_when_the_recovered_document_cannot_be_read()
    {
        var api = new FakeApi(
            Reply.NetworkError(),
            Reply.Problem(422, "idempotency_key_reused", new { documentId = Samples.OtherId }),
            Reply.Problem(404, "not_found"));
        using var test = new Harness(api);
        var error = await Assert.ThrowsAsync<NotFoundException>(() => test.Client.Invoices.CreateAsync(Samples.Invoice(), new RequestOptions { IdempotencyKey = "k-1" }));
        Assert.Equal("k-1", error.IdempotencyKey);
    }

    [Fact]
    public async Task Keeps_the_key_when_the_recovered_id_is_not_a_path_id()
    {
        var api = new FakeApi(Reply.NetworkError(), Reply.Problem(422, "idempotency_key_reused", new { documentId = ".." }));
        using var test = new Harness(api);
        var error = await Assert.ThrowsAsync<FlashpiException>(() => test.Client.Invoices.CreateAsync(Samples.Invoice(), new RequestOptions { IdempotencyKey = "k-1" }));
        Assert.Equal("k-1", error.IdempotencyKey);
    }

    [Fact]
    public async Task Treats_a_404_on_a_retried_delete_as_done_with_a_204_built_from_it()
    {
        var api = new FakeApi(Reply.NetworkError(), Reply.Problem(404, "not_found", null, ("X-Trace", "t1")));
        using var test = new Harness(api);
        var result = await test.Client.Issuers.WithResponse.DeleteAsync(Samples.Id);
        Assert.Equal(204, result.StatusCode);
        Assert.Equal("t1", result.GetHeader("X-Trace"));
        Assert.Equal(Reply.RequestId, result.GetHeader("x-request-id"));
        Assert.Equal("req_problem", result.RequestId);
    }

    [Fact]
    public async Task Throws_a_404_on_the_first_attempt_of_a_delete()
    {
        var api = new FakeApi(Reply.Problem(404, "not_found"));
        using var test = new Harness(api);
        await Assert.ThrowsAsync<NotFoundException>(() => test.Client.Issuers.DeleteAsync(Samples.Id));
    }
}

public sealed class InProgressTests
{
    [Fact]
    public async Task Waits_for_an_earlier_attempt_with_the_same_key_without_using_up_the_retries()
    {
        var api = new FakeApi(
            Reply.NetworkError(),
            Reply.Problem(409, "idempotency_key_in_progress"),
            Reply.Problem(409, "idempotency_key_in_progress"),
            Reply.Problem(409, "idempotency_key_in_progress"),
            Reply.Problem(409, "idempotency_key_in_progress"),
            Reply.Json(Samples.Document()));
        using var test = new Harness(api, options => { options.MaxRetries = 1; return options; });
        await test.Client.Invoices.CreateAsync(Samples.Invoice());
        Assert.Equal(new[] { 0.5, 1.0, 2.0, 4.0, 5.0 }, Seconds.All(test.Waits));
        Assert.Single(api.Requests.Select(sent => sent.Header("Idempotency-Key")).Distinct());
    }

    [Fact]
    public async Task Stops_when_the_calls_time_is_up_and_hands_back_the_key()
    {
        var replies = new object[] { Reply.NetworkError() }
            .Concat(Enumerable.Range(0, 10).Select(_ => (object)Reply.Problem(409, "idempotency_key_in_progress")))
            .ToArray();
        var api = new FakeApi(replies);
        using var test = new Harness(api, options => { options.Timeout = TimeSpan.FromSeconds(10); options.MaxRetries = 1; return options; });
        var error = await Assert.ThrowsAsync<ConflictException>(() => test.Client.Invoices.CreateAsync(Samples.Invoice(), new RequestOptions { IdempotencyKey = "k-1" }));
        Assert.Equal("k-1", error.IdempotencyKey);
        // 0.5 s backoff, then 1 + 2 + 4 s of waits (7.5 s), then the time is up: the retry rules
        // apply, and the only retry was used.
        Assert.Equal(new[] { 0.5, 1.0, 2.0, 4.0 }, Seconds.All(test.Waits));
    }

    [Fact]
    public async Task Follows_the_normal_retry_rules_on_the_first_attempt()
    {
        var api = new FakeApi(Reply.Problem(409, "idempotency_key_in_progress"), Reply.Problem(409, "idempotency_key_in_progress"), Reply.Problem(409, "idempotency_key_in_progress"));
        using var test = new Harness(api);
        await Assert.ThrowsAsync<ConflictException>(() => test.Client.Invoices.CreateAsync(Samples.Invoice()));
        Assert.Equal(3, api.Requests.Count);
        Assert.Equal(new[] { 1.0, 1.0 }, Seconds.All(test.Waits));
    }

    [Fact]
    public async Task Recovers_the_document_if_the_wait_ends_in_idempotency_key_reused()
    {
        var api = new FakeApi(
            Reply.NetworkError(),
            Reply.Problem(409, "idempotency_key_in_progress"),
            Reply.Problem(422, "idempotency_key_reused", new { documentId = Samples.OtherId }),
            Reply.Json(Samples.Document(Samples.OtherId)));
        using var test = new Harness(api);
        var document = await test.Client.Invoices.CreateAsync(Samples.Invoice());
        Assert.Equal(Samples.OtherId, document.Id);
    }

    [Fact]
    public async Task Waits_after_a_5xx_without_problem_details_but_not_with_them()
    {
        var api = new FakeApi(Reply.Text("bad gateway", 502), Reply.Problem(409, "idempotency_key_in_progress"), Reply.Json(Samples.Document()));
        using var test = new Harness(api, options => { options.MaxRetries = 1; return options; });
        await test.Client.Invoices.CreateAsync(Samples.Invoice());
        Assert.Equal(new[] { 0.5, 1.0 }, Seconds.All(test.Waits));

        var answered = new FakeApi(Reply.Problem(500, "internal_error"), Reply.Problem(409, "idempotency_key_in_progress"));
        using var other = new Harness(answered, options => { options.MaxRetries = 1; return options; });
        await Assert.ThrowsAsync<ConflictException>(() => other.Client.Invoices.CreateAsync(Samples.Invoice()));
    }

    [Fact]
    public async Task Raises_the_timeout_of_a_sync_issue_to_its_wait_plus_15_seconds()
    {
        TimeSpan? seen = null;
        var api = new FakeApi(new Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>(async (_, token) =>
        {
            // Long enough for a 0.05 s client timeout, short for the raised one.
            await Task.Delay(200, token);
            seen = TimeSpan.FromMilliseconds(200);
            return Reply.Json(Samples.Document());
        }));
        using var test = new Harness(api, options => { options.Timeout = TimeSpan.FromMilliseconds(50); return options; });
        await test.Client.Invoices.CreateAsync(Samples.Invoice(new DocumentOptions { WaitForAuthorization = true, WaitTimeoutSeconds = 1 }));
        Assert.NotNull(seen);
    }
}
