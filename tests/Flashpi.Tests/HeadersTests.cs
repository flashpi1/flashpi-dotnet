using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Flashpi.Tests;

public sealed class HeadersTests
{
    [Fact]
    public async Task Trims_the_spaces_around_the_values_of_every_option()
    {
        var api = new FakeApi(Reply.Json(Samples.Document(), 202));
        using var test = new Harness(api);
        await test.Client.Invoices.CreateAsync(Samples.Invoice(), new RequestOptions
        {
            IdempotencyKey = "  key-1\t",
            CorrelationId = "\r\ncorr ",
            Language = new Language(" es "),
            ExtraHeaders = new Dictionary<string, string> { ["X-Extra"] = " value " },
        });
        var sent = api.Last;
        Assert.Equal("key-1", sent.Header("Idempotency-Key"));
        Assert.Equal("corr", sent.Header("X-Correlation-Id"));
        Assert.Equal("es", sent.Header("Accept-Language"));
        Assert.Equal("value", sent.Header("X-Extra"));
    }

    [Fact]
    public async Task Keeps_tabs_and_spaces_inside_a_value()
    {
        var api = new FakeApi(Reply.Json(Samples.Document()));
        using var test = new Harness(api);
        await test.Client.Documents.GetAsync(Samples.Id, new RequestOptions { ExtraHeaders = new Dictionary<string, string> { ["X-A"] = "a\tb c" } });
        Assert.Equal("a\tb c", api.Last.Header("X-A"));
    }

    [Fact]
    public async Task Takes_the_last_spelling_of_a_repeated_idempotency_key_header_and_puts_it_on_errors()
    {
        var api = new FakeApi(Reply.Problem(400, "invalid_idempotency_key"));
        using var test = new Harness(api);
        var error = await Assert.ThrowsAsync<BadRequestException>(() => test.Client.Invoices.CreateAsync(Samples.Invoice(), new RequestOptions
        {
            ExtraHeaders = new Dictionary<string, string> { ["idempotency-key"] = "first", ["Idempotency-Key"] = " second " },
        }));
        Assert.Equal("second", api.Last.Header("Idempotency-Key"));
        Assert.Equal("second", error.IdempotencyKey);
    }

    [Fact]
    public async Task Prefers_the_option_over_the_header()
    {
        var api = new FakeApi(Reply.Json(Samples.Document(), 202));
        using var test = new Harness(api);
        await test.Client.Invoices.CreateAsync(Samples.Invoice(), new RequestOptions
        {
            IdempotencyKey = "option",
            ExtraHeaders = new Dictionary<string, string> { ["Idempotency-Key"] = "header" },
        });
        Assert.Equal("option", api.Last.Header("Idempotency-Key"));
    }

    public static TheoryData<RequestOptions> BadOptions => new()
    {
        new RequestOptions { IdempotencyKey = "a\nb" },
        new RequestOptions { IdempotencyKey = "a\u0000b" },
        new RequestOptions { CorrelationId = "a\rb" },
        new RequestOptions { CorrelationId = "a\u007fb" },
        new RequestOptions { Language = new Language("e\ns") },
        new RequestOptions { ExtraHeaders = new Dictionary<string, string> { ["X-A"] = "secret\u0001value" } },
        new RequestOptions { ExtraHeaders = new Dictionary<string, string> { ["X-A"] = "señal" } },
        new RequestOptions { ExtraHeaders = new Dictionary<string, string> { ["Bad Name"] = "x" } },
        new RequestOptions { ExtraHeaders = new Dictionary<string, string> { ["X-É"] = "x" } },
        new RequestOptions { ExtraHeaders = new Dictionary<string, string> { [""] = "x" } },
        new RequestOptions { ExtraHeaders = new Dictionary<string, string> { ["Content-Length"] = "1" } },
        new RequestOptions { ExtraHeaders = new Dictionary<string, string> { ["transfer-encoding"] = "chunked" } },
        new RequestOptions { ExtraHeaders = new Dictionary<string, string> { ["X-A"] = null! } },
    };

    [Theory]
    [MemberData(nameof(BadOptions))]
    public async Task Refuses_a_bad_value_or_name_before_sending_and_never_shows_the_value(RequestOptions options)
    {
        var api = new FakeApi();
        using var test = new Harness(api);
        var error = await Assert.ThrowsAsync<ArgumentException>(() => test.Client.Invoices.CreateAsync(Samples.Invoice(), options));
        Assert.DoesNotContain("secret", error.Message);
        Assert.DoesNotContain("señal", error.Message);
        Assert.Empty(api.Requests);
    }

    public static TheoryData<Dictionary<string, string>> BadDefaultHeaders => new()
    {
        new Dictionary<string, string> { ["X-A"] = "line\nbreak" },
        new Dictionary<string, string> { ["X-A"] = "ü" },
        new Dictionary<string, string> { ["Bad:Name"] = "x" },
        new Dictionary<string, string> { ["Content-Length"] = "0" },
    };

    [Theory]
    [MemberData(nameof(BadDefaultHeaders))]
    public void Refuses_bad_default_headers_when_created(Dictionary<string, string> headers) =>
        Assert.Throws<ArgumentException>(() => new FlashpiClient(new FlashpiClientOptions { ApiKey = Samples.TestKey, DefaultHeaders = headers }));

    [Fact]
    public void Refuses_a_language_with_a_control_character_when_created() =>
        Assert.Throws<ArgumentException>(() => new FlashpiClient(new FlashpiClientOptions { ApiKey = Samples.TestKey, Language = new Language("e\ns") }));

    [Fact]
    public async Task Sends_no_language_for_an_empty_one()
    {
        var api = new FakeApi(Reply.Json(Samples.Document()), Reply.Json(Samples.Document()));
        using var test = new Harness(api, options => { options.Language = new Language("  "); return options; });
        await test.Client.Documents.GetAsync(Samples.Id);
        Assert.Null(api.Last.Header("Accept-Language"));
        using var spanish = new Harness(api, options => { options.Language = Language.Spanish; return options; });
        await spanish.Client.Documents.GetAsync(Samples.Id, new RequestOptions { Language = new Language("") });
        Assert.Equal("es", api.Last.Header("Accept-Language"));
    }

    [Fact]
    public async Task Lets_the_call_language_win_over_a_client_accept_language_header_and_call_headers_win_over_it()
    {
        var api = new FakeApi(Reply.Json(Samples.Document()), Reply.Json(Samples.Document()));
        using var test = new Harness(api, options =>
        {
            options.DefaultHeaders = new Dictionary<string, string> { ["accept-language"] = "fr" };
            return options;
        });
        await test.Client.Documents.GetAsync(Samples.Id, new RequestOptions { Language = Language.Spanish });
        Assert.Equal("es", api.Last.Header("Accept-Language"));
        await test.Client.Documents.GetAsync(Samples.Id, new RequestOptions
        {
            Language = Language.Spanish,
            ExtraHeaders = new Dictionary<string, string> { ["Accept-Language"] = "en" },
        });
        Assert.Equal("en", api.Last.Header("Accept-Language"));
    }

    [Fact]
    public async Task Sends_content_headers_with_a_body_and_keeps_the_json_type()
    {
        var api = new FakeApi(Reply.Json(Samples.Document(), 202), Reply.Json(Samples.Document()));
        using var test = new Harness(api);
        var headers = new Dictionary<string, string> { ["Content-Type"] = "text/plain", ["Content-Language"] = "es" };
        await test.Client.Invoices.CreateAsync(Samples.Invoice(), new RequestOptions { ExtraHeaders = headers });
        Assert.Equal("application/json", api.Last.Header("Content-Type"));
        Assert.Equal("es", api.Last.Header("Content-Language"));
        await test.Client.Documents.GetAsync(Samples.Id, new RequestOptions { ExtraHeaders = headers });
        Assert.Null(api.Last.Header("Content-Type"));
    }

    [Fact]
    public async Task Rejects_options_before_sending()
    {
        var api = new FakeApi();
        using var test = new Harness(api);
        await Assert.ThrowsAsync<FlashpiException>(() => test.Client.Documents.GetAsync(Samples.Id, new RequestOptions { Timeout = TimeSpan.FromSeconds(-1) }));
        await Assert.ThrowsAsync<FlashpiException>(() => test.Client.Documents.GetAsync(Samples.Id, new RequestOptions { MaxRetries = -1 }));
        Assert.Empty(api.Requests);
    }
}
