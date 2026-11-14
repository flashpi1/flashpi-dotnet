using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Flashpi.Internal;
using Flashpi.Models;
using Xunit;

namespace Flashpi.Tests;

public sealed class EdgeTests
{
    [Fact]
    public async Task Every_list_has_a_WithResponse_page()
    {
        var api = new FakeApi();
        for (var index = 0; index < 7; index++)
        {
            api.Enqueue(Reply.Page([]));
        }
        using var test = new Harness(api);
        var client = test.Client;
        Assert.Empty((await client.Issuers.WithResponse.ListAsync()).Data.Data);
        Assert.Empty((await client.Issuers.Establishments.WithResponse.ListAsync("i")).Data.Data);
        Assert.Empty((await client.Issuers.Establishments.EmissionPoints.WithResponse.ListAsync("i", "e")).Data.Data);
        Assert.Empty((await client.Issuers.Certificates.WithResponse.ListAsync("i")).Data.Data);
        Assert.Empty((await client.WebhookEndpoints.WithResponse.ListAsync()).Data.Data);
        Assert.Empty((await client.WebhookEndpoints.Deliveries.WithResponse.ListAsync("w")).Data.Data);
        Assert.Empty((await client.Events.WithResponse.ListAsync()).Data.Data);
        Assert.Equal(
            new[] { "/v1/issuers", "/v1/issuers/i/establishments", "/v1/issuers/i/establishments/e/emission-points", "/v1/issuers/i/certificates", "/v1/webhook-endpoints", "/v1/webhook-endpoints/w/deliveries", "/v1/events" },
            api.Requests.ConvertAll(sent => sent.Path));
    }

    [Fact]
    public async Task Refuses_a_json_answer_without_a_body()
    {
        var api = new FakeApi(Reply.Empty(204), Reply.Empty(200));
        using var test = new Harness(api);
        var error = await Assert.ThrowsAsync<FlashpiException>(() => test.Client.Invoices.CreateAsync(Samples.Invoice(), new RequestOptions { IdempotencyKey = "k" }));
        Assert.Equal("The API answered 204 without a body", error.Message);
        Assert.Equal("k", error.IdempotencyKey);
        await Assert.ThrowsAsync<FlashpiException>(() => test.Client.Documents.GetAsync(Samples.Id));
    }

    [Fact]
    public async Task Reads_an_answer_without_content()
    {
        var api = new FakeApi(new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
        using var test = new Harness(api);
        var result = await test.Client.WebhookEndpoints.WithResponse.DeleteAsync(Samples.Id);
        Assert.Equal(204, result.StatusCode);
    }

    [Fact]
    public void Refuses_a_template_that_does_not_match_its_ids_and_ids_that_are_not_text()
    {
        Assert.Throws<ArgumentException>(() => Paths.Of("/v1/{}/{}", "a"));
        Assert.Throws<ArgumentException>(() => Paths.Of("/v1/{}", "a\ud800"));
        Assert.Equal("/v1/a%20b", Paths.Of("/v1/{}", "a b"));
        Assert.Equal("https://x/v1?a=b+c&d=%C3%A9", Paths.Url("https://x", "/v1", [new("a", "b c"), new("d", "é")]));
    }

    [Fact]
    public void Links_two_cancellable_tokens()
    {
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();
        using (Tokens.Link(first.Token, second.Token, out var token))
        {
            second.Cancel();
            Assert.True(token.IsCancellationRequested);
        }
        Assert.Null(Tokens.Link(first.Token, CancellationToken.None, out var only));
        Assert.Equal(first.Token, only);
        Assert.Null(Tokens.Link(CancellationToken.None, second.Token, out only));
        Assert.Equal(second.Token, only);
    }

    [Fact]
    public void Never_shows_the_key_of_a_configuration()
    {
        var config = new ClientConfig(Samples.TestKey, "https://api.flashpi.com.ec", TimeSpan.FromSeconds(1), 0, null, []);
        Assert.DoesNotContain("fp_test", config.ToString());
    }

    [Fact]
    public void Refuses_a_stream_it_cannot_read()
    {
        using var stream = new UnreadableStream();
        Assert.Throws<ArgumentException>(() => new CertificateUpload(stream, "x"));
        Assert.Throws<ArgumentNullException>(() => new CertificateUpload((Stream)null!, "x"));
    }

    [Fact]
    public void Takes_every_kind_of_body_and_headers_for_webhooks()
    {
        const string secret = "whsec_Zmxhc2hwaS1kb2NzLWV4YW1wbGUtc2VjcmV0LTMyLWI=";
        const string body = "{\"id\":\"019a1b2c-3d4e-7f60-8a1b-2c3d4e5f6a7b\",\"type\":\"invoice.authorized\",\"environment\":\"test\",\"createdAt\":\"2026-10-16T17:20:00.000Z\"}";
        var now = DateTimeOffset.FromUnixTimeSeconds(1792152000);
        var headers = new Dictionary<string, string>
        {
            ["webhook-id"] = "019a1b2c-3d4e-7f60-8a1b-2c3d4e5f6a7b",
            ["webhook-timestamp"] = "1792152000",
            ["webhook-signature"] = "v1,HbWOpDPWCo3wtq41t9GVHUpg69M1zgvveoDJmSWkA8U=",
        };
        var multi = new Dictionary<string, IEnumerable<string>>();
        var collection = new System.Collections.Specialized.NameValueCollection();
        foreach (var header in headers)
        {
            multi[header.Key] = [header.Value];
            collection.Add(header.Key, header.Value);
        }
        var bytes = Encoding.UTF8.GetBytes(body);
        Assert.IsType<EventThin>(Webhooks.Unwrap(body, headers, secret, now: now));
        Assert.IsType<EventThin>(Webhooks.Unwrap(bytes, headers, secret, now: now));
        Assert.IsType<EventThin>(Webhooks.Unwrap(body, multi, secret, now: now));
        Assert.IsType<EventThin>(Webhooks.Unwrap(bytes, multi, secret, now: now));
        Assert.IsType<EventThin>(Webhooks.Unwrap(body, collection, secret, now: now));
        Assert.IsType<EventThin>(Webhooks.Unwrap(bytes, collection, secret, now: now));
        Webhooks.VerifySignature(bytes, multi, secret, now: now);
        Webhooks.VerifySignature(body, multi, secret, now: now);
        Webhooks.VerifySignature(bytes, headers, secret, now: now);
        Webhooks.VerifySignature(body, collection, secret, now: now);
        Webhooks.VerifySignature(bytes, collection, secret, now: now);
        Assert.Throws<ArgumentNullException>(() => Webhooks.VerifySignature(bytes, (System.Collections.Specialized.NameValueCollection)null!, secret));
        Assert.Throws<ArgumentNullException>(() => Webhooks.VerifySignature((byte[])null!, multi, secret));
        Assert.Throws<ArgumentNullException>(() => Webhooks.VerifySignature(body, (Dictionary<string, string[]>)null!, secret));
    }

    [Fact]
    public void Writes_and_reads_a_verification_event_as_a_webhook_event()
    {
        var verification = new WebhookEndpointVerificationEvent
        {
            Id = "v",
            Type = EventType.WebhookEndpointVerification,
            Environment = FlashpiEnvironment.Test,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(0),
            Data = new WebhookEndpointVerificationEventData { WebhookEndpointId = "w", Challenge = "c" },
        };
        var json = System.Text.Json.JsonSerializer.Serialize<WebhookEvent>(verification, Json.TypeInfo<WebhookEvent>());
        Assert.Equal(verification, System.Text.Json.JsonSerializer.Deserialize(json, Json.TypeInfo<WebhookEvent>()));
    }

    private sealed class UnreadableStream : MemoryStream
    {
        public override bool CanRead => false;
    }
}
