using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Flashpi.Internal;

namespace Flashpi.Tests;

/// <summary>A request the fake API received, with its body read.</summary>
internal sealed record Sent(string Method, Uri Url, Dictionary<string, string> Headers, byte[]? Body)
{
    public string Path => Url.AbsolutePath;

    public string Query => Url.Query;

    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;

    public string BodyText => Body is null ? "" : Encoding.UTF8.GetString(Body);

    public JsonElement Json => JsonDocument.Parse(BodyText).RootElement.Clone();
}

/// <summary>
/// An HttpMessageHandler that answers from a queue and records what it received. An exception in
/// the queue is thrown like a network failure; a function answers dynamically.
/// </summary>
internal sealed class FakeApi : HttpMessageHandler
{
    private readonly Queue<object> _replies;

    public FakeApi(params object[] replies) => _replies = new Queue<object>(replies);

    public List<Sent> Requests { get; } = [];

    public Sent Last => Requests[Requests.Count - 1];

    public void Enqueue(params object[] replies)
    {
        foreach (var reply in replies)
        {
            _replies.Enqueue(reply);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }
        byte[]? body = null;
        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }
            body = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }
        Requests.Add(new Sent(request.Method.Method, request.RequestUri!, headers, body));
        if (_replies.Count == 0)
        {
            throw new InvalidOperationException($"Unexpected request {request.Method} {request.RequestUri}");
        }
        var reply = _replies.Dequeue();
        return reply switch
        {
            HttpResponseMessage response => response,
            Exception error => throw error,
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer => await answer(request, cancellationToken).ConfigureAwait(false),
            Func<HttpRequestMessage, HttpResponseMessage> answer => answer(request),
            _ => throw new InvalidOperationException($"Unknown reply {reply}"),
        };
    }
}

/// <summary>Answers like the API's.</summary>
internal static class Reply
{
    public const string RequestId = "req_123";

    public static HttpResponseMessage Json(string json, int status = 200, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        response.Headers.TryAddWithoutValidation("x-request-id", RequestId);
        foreach (var (name, value) in headers)
        {
            if (!response.Headers.TryAddWithoutValidation(name, value))
            {
                response.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }
        return response;
    }

    public static HttpResponseMessage Json(object body, int status = 200, params (string Name, string Value)[] headers) =>
        Json(JsonSerializer.Serialize(body), status, headers);

    public static HttpResponseMessage Problem(int status, string code, object? extra = null, params (string Name, string Value)[] headers)
    {
        var body = new Dictionary<string, object?>
        {
            ["type"] = $"https://flashpi.com.ec/docs/en/errors/{code}",
            ["title"] = "Problem",
            ["status"] = status,
            ["detail"] = $"Detail of {code}",
            ["instance"] = "/v1/test",
            ["code"] = code,
            ["errors"] = Array.Empty<object>(),
            ["requestId"] = "req_problem",
        };
        if (extra is not null)
        {
            foreach (var member in JsonSerializer.SerializeToElement(extra).EnumerateObject())
            {
                body[member.Name] = member.Value;
            }
        }
        var response = Json(JsonSerializer.Serialize(body), status, headers);
        response.Content!.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/problem+json");
        return response;
    }

    public static HttpResponseMessage Text(string text, int status, string contentType = "text/html")
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(text, Encoding.UTF8, contentType) };
        return response;
    }

    public static HttpResponseMessage Empty(int status) => new((HttpStatusCode)status) { Content = new ByteArrayContent([]) };

    public static HttpResponseMessage Page(object[] data, string? nextCursor = null) =>
        Json(new { data, pagination = new { nextCursor, hasMore = nextCursor is not null } });

    public static HttpRequestException NetworkError(string message = "Connection refused") => new(message);
}

/// <summary>Sample bodies of the API.</summary>
internal static class Samples
{
    public const string TestKey = "fp_test_aaaaaaaaaaaaaaaabbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    public const string LiveKey = "fp_live_aaaaaaaaaaaaaaaabbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    public const string Id = "019a1b2c-3d4e-7f60-8a1b-2c3d4e5f6a7b";
    public const string OtherId = "019a1b2c-3d4e-7f60-8a1b-2c3d4e5f6a7c";

    public static object Document(string id = Id, string status = "authorized") => new
    {
        id,
        type = "invoice",
        environment = "test",
        status,
        accessKey = new string('1', 49),
        sequential = 1501,
        createdAt = "2026-11-13T12:00:00.000Z",
    };

    public static Models.CreateInvoice Invoice(Models.DocumentOptions? options = null) => new()
    {
        IssuerId = Id,
        Establishment = "001",
        EmissionPoint = "001",
        Customer = new Models.DocumentParty { IdType = Models.IdType.Ruc, Id = "1760013210001", Name = "PRUEBAS SERVICIO DE RENTAS INTERNAS" },
        Items = [new Models.DocumentItem { Code = "P-001", Description = "Servicio", Quantity = "1", UnitPrice = "1.00", Taxes = [new Models.DocumentTax { Type = Models.TaxType.Vat, Rate = "15" }] }],
        Payments = [new Models.DocumentPayment { Method = Models.PaymentMethod.Cash, Amount = "1.15" }],
        Options = options,
    };
}

/// <summary>A client on a fake API, with a fake clock whose waits return at once and are recorded.</summary>
internal sealed class Harness : IDisposable
{
    private TimeSpan _now = TimeSpan.FromHours(1);

    public Harness(FakeApi api, Func<FlashpiClientOptions, FlashpiClientOptions>? configure = null, double random = 0)
    {
        Api = api;
        Http = new HttpClient(api) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        var options = new FlashpiClientOptions { ApiKey = Samples.TestKey, HttpClient = Http };
        options = configure?.Invoke(options) ?? options;
        Hooks = new ClientHooks
        {
            Delay = (wait, token) =>
            {
                token.ThrowIfCancellationRequested();
                Waits.Add(wait);
                _now += wait;
                return Task.CompletedTask;
            },
            Random = () => random,
            Elapsed = () => _now,
            UtcNow = () => new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero),
        };
        Client = new FlashpiClient(options, Hooks);
    }

    public FakeApi Api { get; }

    public HttpClient Http { get; }

    public ClientHooks Hooks { get; }

    public FlashpiClient Client { get; }

    public List<TimeSpan> Waits { get; } = [];

    /// <summary>Moves the fake clock, as time spent in an attempt.</summary>
    public void Advance(TimeSpan time) => _now += time;

    public void Dispose()
    {
        Client.Dispose();
        Http.Dispose();
    }
}

internal static class Seconds
{
    public static TimeSpan Of(double seconds) => TimeSpan.FromMilliseconds(Math.Round(seconds * 1000));

    public static double[] All(IEnumerable<TimeSpan> waits) => [.. waits.Select(wait => Math.Round(wait.TotalSeconds, 3))];
}
