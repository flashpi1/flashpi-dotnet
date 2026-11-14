using System;
using System.Collections.Generic;

namespace Flashpi;

/// <summary>
/// Options of one call, the last argument before the <c>CancellationToken</c> of every method.
/// A null member keeps the client's setting.
/// </summary>
/// <example>
/// <code>
/// await flashpi.Invoices.CreateAsync(body, new RequestOptions { IdempotencyKey = $"order-{order.Id}" });
/// </code>
/// </example>
public sealed record RequestOptions
{
    /// <summary>
    /// The Idempotency-Key of a POST: 1 to 255 visible ASCII characters (no spaces), unique per
    /// operation, such as your order id or a UUID. The SDK creates one when it is missing and
    /// reuses it on its retries, so a retry never issues a document twice. Ignored by other
    /// methods. An <c>Idempotency-Key</c> in <see cref="ExtraHeaders"/> works too.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>
    /// How long each attempt may take, from sending it to reading the whole answer, overriding
    /// the client's <see cref="FlashpiClientOptions.Timeout"/>.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Retries after network errors, timeouts, 408, 429 and 5xx, overriding the client's
    /// <see cref="FlashpiClientOptions.MaxRetries"/>.
    /// </summary>
    public int? MaxRetries { get; init; }

    /// <summary>The language of the API's messages for this call, overriding the client's.</summary>
    public Language? Language { get; init; }

    /// <summary>
    /// Your own id for the request (<c>X-Correlation-Id</c>, 1 to 128 of <c>A-Za-z0-9._:/+=-</c>),
    /// echoed back and logged by Flashpi.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Extra headers for this call, sent after the client's. They never replace the API key.
    /// Content headers (such as <c>Content-Type</c>) go only with a body.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ExtraHeaders { get; init; }
}
