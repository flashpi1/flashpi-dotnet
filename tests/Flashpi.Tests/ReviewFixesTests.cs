using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Flashpi.Internal;
using Flashpi.Models;
using Xunit;

namespace Flashpi.Tests;

/// <summary>Regressions for what the adversarial review of the .NET SDK found.</summary>
public sealed class ReviewFixesTests
{
    [Fact]
    public async Task Retries_a_body_that_does_not_decompress_with_the_same_key()
    {
        var broken = new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new ThrowingContent(new InvalidDataException("bad gzip")) };
        var api = new FakeApi(broken, Reply.Json(Samples.Document(), 202));
        using var test = new Harness(api);
        await test.Client.Invoices.CreateAsync(Samples.Invoice());
        Assert.Equal(2, api.Requests.Count);
        Assert.Equal(api.Requests[0].Header("Idempotency-Key"), api.Requests[1].Header("Idempotency-Key"));

        var failing = new FakeApi(
            new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new ThrowingContent(new InvalidDataException("bad gzip")) });
        using var once = new Harness(failing, options => { options.MaxRetries = 0; return options; });
        var error = await Assert.ThrowsAsync<ApiConnectionException>(() => once.Client.Invoices.CreateAsync(Samples.Invoice(), new RequestOptions { IdempotencyKey = "k-1" }));
        Assert.Equal("k-1", error.IdempotencyKey);
        Assert.IsType<InvalidDataException>(error.InnerException);
    }

    [Fact]
    public async Task Cancels_a_call_whose_token_is_cancelled_while_its_answer_ends()
    {
        using var cancel = new CancellationTokenSource();
        var answer = new HttpResponseMessage(HttpStatusCode.OK) { Content = new CallbackContent("{\"status\":\"operational\"}", cancel.Cancel) };
        var api = new FakeApi(answer);
        using var test = new Harness(api);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test.Client.Status.GetAsync(cancellationToken: cancel.Token));
        Assert.Equal(cancel.Token, error.CancellationToken);
        Assert.Single(api.Requests);
    }

    [Fact]
    public async Task Throws_a_disposed_client_at_once_without_retrying()
    {
        var api = new FakeApi();
        var test = new Harness(api);
        test.Http.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => test.Client.Documents.GetAsync(Samples.Id));
        Assert.Empty(test.Waits);
        test.Dispose();
    }

    [Fact]
    public async Task Recovers_the_document_from_problem_details_of_another_shape()
    {
        var reuse = Reply.Json(
            $"{{\"code\":\"idempotency_key_reused\",\"status\":\"422\",\"errors\":{{}},\"documentId\":\"{Samples.OtherId}\",\"requestId\":\"r1\"}}", 422);
        var api = new FakeApi(Reply.NetworkError(), reuse, Reply.Json(Samples.Document(Samples.OtherId)));
        using var test = new Harness(api);
        var document = await test.Client.Invoices.CreateAsync(Samples.Invoice());
        Assert.Equal(Samples.OtherId, document.Id);
    }

    [Fact]
    public void Keeps_every_member_of_problem_details_of_another_shape()
    {
        var problem = ApiErrors.ParseProblem("{\"code\":\"x\",\"errors\":{},\"limit\":\"many\",\"expectedTotal\":\"1.15\"}")!;
        Assert.Equal("1.15", problem.ExpectedTotal);
        Assert.Equal("many", problem.AdditionalProperties!["limit"].GetString());
    }

    [Theory]
    [InlineData("12:30")]
    [InlineData("1.5")]
    [InlineData("tomorrow")]
    [InlineData("Fri")]
    public void Ignores_a_retry_after_that_is_not_a_date(string value) =>
        Assert.Null(RetryAfterHeader.Parse(value, new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void Reads_a_retry_after_given_as_an_iso_date()
    {
        var now = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(30), RetryAfterHeader.Parse("2026-11-13T12:00:30Z", now));
        Assert.Equal(TimeSpan.FromSeconds(30), RetryAfterHeader.Parse("2026-11-13T07:00:30-05:00", now));
        Assert.Equal(TimeSpan.FromSeconds(30), RetryAfterHeader.Parse("2026-11-13T12:00:30", now));
        Assert.Equal(TimeSpan.FromHours(12), RetryAfterHeader.Parse("2026-11-14", now));
    }

    [Fact]
    public async Task Clears_the_key_from_a_copy_of_the_request_a_handler_sent()
    {
        var api = new FakeApi(Reply.Json(Samples.Document()), Reply.Problem(404, "not_found"));
        using var copying = new CopyingHandler { InnerHandler = api };
        using var http = new HttpClient(copying);
        using var client = new FlashpiClient(new FlashpiClientOptions { ApiKey = Samples.TestKey, HttpClient = http, MaxRetries = 0 });
        var result = await client.Documents.WithResponse.GetAsync(Samples.Id);
        Assert.NotNull(result.Response.RequestMessage);
        Assert.Null(result.Response.RequestMessage!.Headers.Authorization);
        var error = await Assert.ThrowsAsync<NotFoundException>(() => client.Documents.GetAsync(Samples.Id));
        Assert.Null(error.Response.RequestMessage!.Headers.Authorization);
        Assert.Equal($"Bearer {Samples.TestKey}", api.Last.Header("Authorization"));
    }

    [Fact]
    public void Never_shows_the_webhook_secret()
    {
        var secret = new WebhookEndpointSecret { Secret = "whsec_c2VjcmV0", PreviousSecretExpiresAt = null };
        Assert.Equal("WebhookEndpointSecret { Secret = <hidden>, PreviousSecretExpiresAt =  }", secret.ToString());
        Assert.DoesNotContain("whsec_", secret.ToString());
        Assert.Equal(secret, secret with { });
    }

    [Fact]
    public void Enums_are_dictionary_keys_and_sort()
    {
        var counts = new Dictionary<DocumentStatus, int> { [DocumentStatus.Authorized] = 2, [DocumentStatus.Rejected] = 1 };
        var json = JsonSerializer.Serialize(counts, new JsonSerializerOptions());
        Assert.Equal("{\"authorized\":2,\"rejected\":1}", json);
        Assert.Equal(counts, JsonSerializer.Deserialize<Dictionary<DocumentStatus, int>>(json));
        var sorted = new[] { DocumentStatus.Rejected, DocumentStatus.Authorized, DocumentStatus.Failed }.OrderBy(status => status).ToList();
        Assert.Equal(new[] { DocumentStatus.Authorized, DocumentStatus.Failed, DocumentStatus.Rejected }, sorted);
    }

    /// <summary>Content whose stream throws when read.</summary>
    private sealed class ThrowingContent(Exception error) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.FromException(error);

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new ThrowingStream(error));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class ThrowingStream(Exception error) : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw error;

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw error;

#if NET
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw error;
#endif
    }

    /// <summary>Content that runs a callback as its last bytes are read, without looking at the token.</summary>
    private sealed class CallbackContent(string text, Action onEnd) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new CallbackStream(Encoding.UTF8.GetBytes(text), onEnd));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class CallbackStream(byte[] bytes, Action onEnd) : MemoryStream(bytes)
    {
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer, offset, count));

#if NET
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(Read(buffer.Span));
#endif

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            if (read == 0)
            {
                onEnd();
            }
            return read;
        }

#if NET
        public override int Read(Span<byte> buffer)
        {
            var read = base.Read(buffer);
            if (read == 0)
            {
                onEnd();
            }
            return read;
        }
#endif
    }

    /// <summary>A handler that sends a copy of each request, as some resilience handlers do.</summary>
    private sealed class CopyingHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var copy = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            var response = await base.SendAsync(copy, cancellationToken);
            response.RequestMessage = copy;
            return response;
        }
    }
}
