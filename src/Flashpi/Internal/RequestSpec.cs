using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Flashpi.Internal;

/// <summary>How to read a successful answer.</summary>
internal enum ResponseKind
{
    Json,
    Text,
    Bytes,
    None,
}

/// <summary>One API operation, as the resources declare it.</summary>
internal sealed record RequestSpec
{
    internal static readonly HttpMethod Patch = new("PATCH");

    public required HttpMethod Method { get; init; }

    /// <summary>The path, built with <see cref="Paths.Of"/> when it carries ids.</summary>
    public required string Path { get; init; }

    /// <summary>The query, in order. A list's later pages replace its <c>cursor</c>.</summary>
    public IReadOnlyList<KeyValuePair<string, string>>? Query { get; init; }

    /// <summary>A JSON body, serialized once so every attempt sends the same bytes.</summary>
    public byte[]? Body { get; init; }

    /// <summary>A multipart body (a .p12 upload), built again from the same bytes for every attempt.</summary>
    public CertificateForm? Multipart { get; init; }

    public string Accept { get; init; } = "application/json";

    public ResponseKind Response { get; init; } = ResponseKind.Json;

    public RequestOptions? Options { get; init; }

    /// <summary>
    /// A floor for the per-attempt timeout of requests the API holds open (issuing with
    /// <c>Options.WaitForAuthorization</c>).
    /// </summary>
    public TimeSpan MinTimeout { get; init; }

    /// <summary>
    /// For document POSTs: when a retry of this call is answered 422 <c>idempotency_key_reused</c>
    /// with a <c>documentId</c>, an earlier attempt created the document, so the call returns
    /// GET /v1/documents/{documentId} instead of failing.
    /// </summary>
    public bool RecoverDocument { get; init; }

    /// <summary>The same list request for the page after <paramref name="cursor"/>.</summary>
    internal RequestSpec WithCursor(string cursor)
    {
        var query = new List<KeyValuePair<string, string>>();
        foreach (var pair in Query ?? [])
        {
            if (pair.Key != "cursor")
            {
                query.Add(pair);
            }
        }
        query.Add(new KeyValuePair<string, string>("cursor", cursor));
        return this with { Query = query };
    }
}

/// <summary>The settings of a client that the requests need.</summary>
internal sealed record ClientConfig(
    string ApiKey,
    string BaseUrl,
    TimeSpan Timeout,
    int MaxRetries,
    string? Language,
    IReadOnlyList<KeyValuePair<string, string>> DefaultHeaders)
{
    // Never the API key.
    public override string ToString() => $"ClientConfig(BaseUrl={BaseUrl})";
}

/// <summary>The clock, the waits and the jitter of a client, replaced in tests.</summary>
internal sealed class ClientHooks
{
    private static readonly ThreadLocal<Random> Randoms = new(() => new Random(Guid.NewGuid().GetHashCode()));

    internal static readonly ClientHooks Default = new();

    /// <summary>Waits before a retry.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    /// <summary>A number from 0 (included) to 1, for the jitter.</summary>
    public Func<double> Random { get; init; } = () => Randoms.Value!.NextDouble();

    /// <summary>A monotonic clock.</summary>
    public Func<TimeSpan> Elapsed { get; init; } = () => TimeSpan.FromTicks((long)(Stopwatch.GetTimestamp() * TicksPerTimestamp));

    /// <summary>The wall clock, for Retry-After dates.</summary>
    public Func<DateTimeOffset> UtcNow { get; init; } = () => DateTimeOffset.UtcNow;

    private static double TicksPerTimestamp => (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;
}
