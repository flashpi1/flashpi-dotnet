using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Flashpi.Models;
using Xunit;

namespace Flashpi.Tests;

[Collection("Environment")]
public sealed class ClientTests
{
    [Fact]
    public void Takes_the_key_and_tells_the_environment_from_its_prefix()
    {
        using var test = new FlashpiClient(Samples.TestKey);
        using var live = new FlashpiClient(Samples.LiveKey);
        Assert.Equal(FlashpiEnvironment.Test, test.Environment);
        Assert.Equal(FlashpiEnvironment.Live, live.Environment);
    }

    [Fact]
    public void Falls_back_to_the_environment_variable()
    {
        using var variable = new EnvironmentVariable("FLASHPI_API_KEY", Samples.LiveKey);
        using var client = new FlashpiClient();
        Assert.Equal(FlashpiEnvironment.Live, client.Environment);
    }

    [Fact]
    public void Refuses_a_missing_key()
    {
        using var variable = new EnvironmentVariable("FLASHPI_API_KEY", null);
        var error = Assert.Throws<FlashpiException>(() => new FlashpiClient());
        Assert.Contains("FLASHPI_API_KEY", error.Message);
        Assert.Throws<FlashpiException>(() => new FlashpiClient("   "));
    }

    [Theory]
    [InlineData("whsec_c2VjcmV0", "webhook signing secret")]
    [InlineData("sk_live_123", "must start with fp_test_ or fp_live_")]
    public void Refuses_a_webhook_secret_or_another_prefix(string key, string message)
    {
        var error = Assert.Throws<FlashpiException>(() => new FlashpiClient(key));
        Assert.Contains(message, error.Message);
    }

    [Theory]
    [InlineData("fp_test_abc def")]
    [InlineData("fp_test_abc\ndef")]
    [InlineData("fp_test_abc\u0000def")]
    [InlineData("fp_test_abcñdef")]
    public void Refuses_a_key_with_characters_a_key_cannot_have_without_showing_it(string key)
    {
        var error = Assert.Throws<FlashpiException>(() => new FlashpiClient(key));
        Assert.Contains("copy it again", error.Message);
        Assert.DoesNotContain("abc", error.Message);
    }

    [Fact]
    public void Refuses_such_a_key_from_the_environment_variable()
    {
        using var variable = new EnvironmentVariable("FLASHPI_API_KEY", "fp_test_abc def");
        Assert.Throws<FlashpiException>(() => new FlashpiClient());
    }

    [Fact]
    public async Task Trims_what_javascripts_trim_removes_around_a_key()
    {
        var api = new FakeApi(Reply.Json(new { status = "operational" }));
        using var test = new Harness(api, options => { options.ApiKey = $"\ufeff\u00a0 {Samples.TestKey}\r\n\u2028"; return options; });
        await test.Client.Status.GetAsync();
        Assert.Equal($"Bearer {Samples.TestKey}", api.Last.Header("Authorization"));
    }

    [Fact]
    public void Validates_its_settings()
    {
        Assert.Throws<FlashpiException>(() => new FlashpiClient(new FlashpiClientOptions { ApiKey = Samples.TestKey, Timeout = TimeSpan.Zero }));
        Assert.Throws<FlashpiException>(() => new FlashpiClient(new FlashpiClientOptions { ApiKey = Samples.TestKey, Timeout = TimeSpan.FromDays(25) }));
        Assert.Throws<FlashpiException>(() => new FlashpiClient(new FlashpiClientOptions { ApiKey = Samples.TestKey, MaxRetries = -1 }));
        Assert.Throws<FlashpiException>(() => new FlashpiClient(new FlashpiClientOptions { ApiKey = Samples.TestKey, BaseUrl = new Uri("ftp://example.com") }));
        Assert.Throws<FlashpiException>(() => new FlashpiClient(new FlashpiClientOptions { ApiKey = Samples.TestKey, BaseUrl = new Uri("https://example.com/?a=1") }));
        Assert.Throws<ArgumentNullException>(() => new FlashpiClient((FlashpiClientOptions)null!));
    }

    [Fact]
    public void Accepts_the_longest_timeout()
    {
        using var client = new FlashpiClient(new FlashpiClientOptions { ApiKey = Samples.TestKey, Timeout = TimeSpan.FromMilliseconds(int.MaxValue), MaxRetries = 0 });
        Assert.Equal(FlashpiEnvironment.Test, client.Environment);
    }

    [Fact]
    public void Refuses_an_idempotency_key_in_the_default_headers()
    {
        var error = Assert.Throws<FlashpiException>(() => new FlashpiClient(new FlashpiClientOptions
        {
            ApiKey = Samples.TestKey,
            DefaultHeaders = new Dictionary<string, string> { ["idempotency-KEY"] = " abc " },
        }));
        Assert.Contains("Idempotency-Key", error.Message);
    }

    [Fact]
    public void Never_shows_the_key()
    {
        using var client = new FlashpiClient(Samples.TestKey);
        Assert.Equal("FlashpiClient(Environment=test, BaseUrl=https://api.flashpi.com.ec)", client.ToString());
        Assert.DoesNotContain("fp_test", new FlashpiClientOptions { ApiKey = Samples.TestKey }.ToString());
    }

    [Fact]
    public async Task Disposes_its_own_http_client_but_not_yours()
    {
        var api = new FakeApi(Reply.Json(new { status = "operational" }));
        using var http = new HttpClient(api);
        var client = new FlashpiClient(new FlashpiClientOptions { ApiKey = Samples.TestKey, HttpClient = http });
        client.Dispose();
        var status = await http.GetAsync(new Uri("https://api.flashpi.com.ec/v1/status"));
        Assert.True(status.IsSuccessStatusCode);
        new FlashpiClient(Samples.TestKey).Dispose();
    }

    [Fact]
    public async Task Sends_the_key_json_headers_and_the_sdk_version()
    {
        var api = new FakeApi(Reply.Json(Samples.Document(), 202));
        using var test = new Harness(api);
        await test.Client.Invoices.CreateAsync(Samples.Invoice());
        var sent = api.Last;
        Assert.Equal("POST", sent.Method);
        Assert.Equal("https://api.flashpi.com.ec/v1/invoices", sent.Url.ToString());
        Assert.Equal($"Bearer {Samples.TestKey}", sent.Header("Authorization"));
        Assert.Equal("application/json", sent.Header("Accept"));
        Assert.Equal("application/json", sent.Header("Content-Type"));
        Assert.Equal($"flashpi-dotnet/{Internal.SdkVersion.Value}", sent.Header("User-Agent"));
        Assert.True(Guid.TryParse(sent.Header("Idempotency-Key"), out _));
        Assert.Equal("ruc", sent.Json.GetProperty("customer").GetProperty("idType").GetString());
        Assert.False(sent.Json.TryGetProperty("issueDate", out _));
    }

    [Fact]
    public async Task Uses_a_fresh_key_per_call_and_the_callers_key_when_given()
    {
        var api = new FakeApi(Reply.Json(Samples.Document(), 202), Reply.Json(Samples.Document(), 202), Reply.Json(Samples.Document(), 202));
        using var test = new Harness(api);
        await test.Client.Invoices.CreateAsync(Samples.Invoice());
        await test.Client.Invoices.CreateAsync(Samples.Invoice());
        await test.Client.Invoices.CreateAsync(Samples.Invoice(), new RequestOptions { IdempotencyKey = "order-42" });
        Assert.NotEqual(api.Requests[0].Header("Idempotency-Key"), api.Requests[1].Header("Idempotency-Key"));
        Assert.Equal("order-42", api.Requests[2].Header("Idempotency-Key"));
    }

    [Fact]
    public async Task Never_sends_an_idempotency_key_on_patch_delete_or_get()
    {
        var api = new FakeApi(
            Reply.Json(new { id = Samples.Id }), Reply.Empty(204), Reply.Json(Samples.Document()));
        using var test = new Harness(api);
        await test.Client.Issuers.UpdateAsync(Samples.Id, new UpdateIssuer { LegalName = "X" }, new RequestOptions { IdempotencyKey = "k" });
        await test.Client.Issuers.DeleteAsync(Samples.Id);
        await test.Client.Documents.GetAsync(Samples.Id);
        Assert.All(api.Requests, sent => Assert.Null(sent.Header("Idempotency-Key")));
    }

    [Fact]
    public async Task Asks_for_the_client_language_and_lets_a_call_override_it()
    {
        var api = new FakeApi(Reply.Json(Samples.Document()), Reply.Json(Samples.Document()));
        using var test = new Harness(api, options => { options.Language = Language.Spanish; return options; });
        await test.Client.Documents.GetAsync(Samples.Id);
        await test.Client.Documents.GetAsync(Samples.Id, new RequestOptions { Language = Language.English });
        Assert.Equal("es", api.Requests[0].Header("Accept-Language"));
        Assert.Equal("en", api.Requests[1].Header("Accept-Language"));
    }

    [Fact]
    public async Task Sends_the_correlation_id_and_custom_headers_but_never_replaces_the_key()
    {
        var api = new FakeApi(Reply.Json(Samples.Document()));
        using var test = new Harness(api, options =>
        {
            options.DefaultHeaders = new Dictionary<string, string> { ["X-Team"] = "billing" };
            return options;
        });
        await test.Client.Documents.GetAsync(Samples.Id, new RequestOptions
        {
            CorrelationId = "order-42",
            ExtraHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer other", ["X-Trace"] = "t1" },
        });
        var sent = api.Last;
        Assert.Equal("order-42", sent.Header("X-Correlation-Id"));
        Assert.Equal("billing", sent.Header("X-Team"));
        Assert.Equal("t1", sent.Header("X-Trace"));
        Assert.Equal($"Bearer {Samples.TestKey}", sent.Header("Authorization"));
    }

    [Fact]
    public async Task Keeps_a_base_url_path_and_drops_its_trailing_slash()
    {
        var api = new FakeApi(Reply.Json(new { status = "operational" }));
        using var test = new Harness(api, options => { options.BaseUrl = new Uri("http://localhost:8080/proxy/"); return options; });
        await test.Client.Status.GetAsync();
        Assert.Equal("http://localhost:8080/proxy/v1/status", api.Last.Url.ToString());
    }

    [Fact]
    public async Task Returns_the_response_and_request_id_with_WithResponse()
    {
        var api = new FakeApi(Reply.Json(Samples.Document(), 200, ("Idempotent-Replayed", "true")));
        using var test = new Harness(api);
        var result = await test.Client.Documents.WithResponse.GetAsync(Samples.Id);
        Assert.Equal(Samples.Id, result.Data.Id);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(Reply.RequestId, result.RequestId);
        Assert.Equal("true", result.GetHeader("idempotent-replayed"));
        Assert.Equal("application/json; charset=utf-8", result.GetHeader("content-type"));
        Assert.Null(result.Response.RequestMessage?.Headers.Authorization);
    }

    [Fact]
    public async Task Reads_text_and_bytes_answers()
    {
        var xml = Reply.Text("<factura/>", 200, "application/xml");
        xml.Headers.TryAddWithoutValidation("Flashpi-Xml-Kind", "signed");
        var pdf = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent([0x25, 0x50, 0x44, 0x46]) };
        var api = new FakeApi(xml, pdf);
        using var test = new Harness(api);
        var document = await test.Client.Documents.GetXmlAsync(Samples.Id);
        Assert.Equal(new DocumentXml("<factura/>", DocumentXmlKind.Signed), document);
        Assert.Equal("application/xml, application/problem+json", api.Requests[0].Header("Accept"));
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, await test.Client.Documents.GetPdfAsync(Samples.Id));
        Assert.Equal("application/pdf, application/problem+json", api.Requests[1].Header("Accept"));
    }

    [Fact]
    public async Task Reads_an_unknown_xml_kind_as_null()
    {
        var api = new FakeApi(Reply.Text("<x/>", 200, "application/xml"));
        using var test = new Harness(api);
        Assert.Null((await test.Client.Documents.GetXmlAsync(Samples.Id)).Kind);
    }

    [Fact]
    public async Task Rejects_empty_path_ids_before_sending_anything()
    {
        var api = new FakeApi();
        using var test = new Harness(api);
        await Assert.ThrowsAsync<FlashpiException>(() => test.Client.Documents.GetAsync(""));
        await Assert.ThrowsAsync<FlashpiException>(() => test.Client.Documents.GetAsync("  "));
        await Assert.ThrowsAsync<FlashpiException>(() => test.Client.Documents.GetAsync(null!));
        await Assert.ThrowsAsync<FlashpiException>(() => test.Client.Documents.GetAsync(".."));
        await Assert.ThrowsAsync<FlashpiException>(() => test.Client.Issuers.Establishments.GetAsync(Samples.Id, "."));
        Assert.Throws<FlashpiException>(() => test.Client.WebhookEndpoints.Deliveries.ListAsync(""));
        Assert.Empty(api.Requests);
    }

    [Fact]
    public async Task Encodes_path_ids_like_encodeURIComponent()
    {
        var api = new FakeApi(Reply.Json(Samples.Document()), Reply.Json(Samples.Document()));
        using var test = new Harness(api);
        await test.Client.Documents.GetAsync("a/b c?d#é!*'()");
        Assert.Equal("/v1/documents/a%2Fb%20c%3Fd%23%C3%A9!*'()", api.Last.Url.AbsolutePath);
        await test.Client.Documents.GetAsync("...");
        Assert.Equal("/v1/documents/...", api.Last.Url.AbsolutePath);
    }

    [Fact]
    public async Task Throws_a_flashpi_exception_with_the_key_when_a_success_body_is_not_json()
    {
        var api = new FakeApi(Reply.Text("<html>", 202));
        using var test = new Harness(api);
        var error = await Assert.ThrowsAsync<FlashpiException>(() => test.Client.Invoices.CreateAsync(Samples.Invoice(), new RequestOptions { IdempotencyKey = "k-1" }));
        Assert.Contains("202", error.Message);
        Assert.Equal("k-1", error.IdempotencyKey);
    }

    [Fact]
    public async Task Refuses_a_null_body()
    {
        using var test = new Harness(new FakeApi());
        await Assert.ThrowsAsync<ArgumentNullException>(() => test.Client.Invoices.CreateAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => test.Client.Issuers.CreateAsync(null!));
    }

    [Fact]
    public async Task Sends_the_body_of_issuer_onboarding_completion_or_an_empty_object()
    {
        var api = new FakeApi(Reply.Json(new { issuer = new { id = Samples.Id } }, 201), Reply.Json(new { issuer = new { id = Samples.Id } }, 201));
        using var test = new Harness(api);
        await test.Client.IssuerOnboardings.CompleteAsync(Samples.Id);
        Assert.Equal("{}", api.Requests[0].BodyText);
        await test.Client.IssuerOnboardings.CompleteAsync(Samples.Id, new CompleteIssuerOnboarding { Ruc = "1790012345001" });
        Assert.Equal("{\"ruc\":\"1790012345001\"}", api.Requests[1].BodyText);
    }

    [Fact]
    public async Task Sends_no_body_to_rotate_a_secret_unless_given_and_an_empty_object_to_email()
    {
        var api = new FakeApi(Reply.Json(new { id = Samples.Id }), Reply.Json(new { id = Samples.Id }), Reply.Json(Samples.Document(), 202));
        using var test = new Harness(api);
        await test.Client.WebhookEndpoints.RotateSecretAsync(Samples.Id);
        Assert.Null(api.Requests[0].Body);
        await test.Client.WebhookEndpoints.RotateSecretAsync(Samples.Id, new RotateWebhookEndpointSecret { PreviousSecretExpiresInHours = 0 });
        Assert.Equal("{\"previousSecretExpiresInHours\":0}", api.Requests[1].BodyText);
        await test.Client.Documents.SendEmailAsync(Samples.Id);
        Assert.Equal("{}", api.Requests[2].BodyText);
    }
}

/// <summary>Sets an environment variable for one test, then puts it back.</summary>
internal sealed class EnvironmentVariable : IDisposable
{
    private readonly string _name;
    private readonly string? _previous;

    public EnvironmentVariable(string name, string? value)
    {
        _name = name;
        _previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
}

[CollectionDefinition("Environment", DisableParallelization = true)]
public sealed class EnvironmentCollection
{
}

public sealed class LinqUse
{
    [Fact]
    public void Samples_keys_are_well_formed() => Assert.True(Samples.TestKey.All(character => character is > ' ' and < '\u007f'));
}
