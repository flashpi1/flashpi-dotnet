using System;
using System.Collections.Generic;
using System.Net.Http;

namespace Flashpi;

/// <summary>The settings of a <see cref="FlashpiClient"/>. Every member is optional.</summary>
public sealed class FlashpiClientOptions
{
    /// <summary>
    /// The API key (<c>fp_test_...</c> or <c>fp_live_...</c>). Defaults to the
    /// <c>FLASHPI_API_KEY</c> environment variable.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>The API's address. Defaults to https://api.flashpi.com.ec.</summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>
    /// How long each attempt of a request may take, from sending it to reading the whole answer
    /// (default 60 seconds, at most 2147483.647 seconds). Issuing with
    /// <c>Options.WaitForAuthorization</c> raises it to the wait plus 15 seconds.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Retries after network errors, timeouts, 408, 429 and 5xx (default 2; 0 turns them off).</summary>
    public int? MaxRetries { get; set; }

    /// <summary>
    /// The language of the API's messages: <see cref="Flashpi.Language.English"/> (default) or
    /// <see cref="Flashpi.Language.Spanish"/>. Error codes do not change.
    /// </summary>
    public Language? Language { get; set; }

    /// <summary>
    /// Headers sent with every request (not <c>Idempotency-Key</c>: that one is per operation).
    /// </summary>
    public IReadOnlyDictionary<string, string>? DefaultHeaders { get; set; }

    /// <summary>
    /// An <see cref="System.Net.Http.HttpClient"/> to send the requests with (proxies,
    /// <c>IHttpClientFactory</c>, tests). You dispose it. Give it an infinite
    /// <see cref="System.Net.Http.HttpClient.Timeout"/> (or one longer than <see cref="Timeout"/>),
    /// since the SDK limits each attempt itself, and a handler that does not follow redirects. By
    /// default the SDK creates one that never follows redirects.
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>The settings, without the API key.</summary>
    public override string ToString() =>
        $"FlashpiClientOptions(BaseUrl={BaseUrl}, Timeout={Timeout}, MaxRetries={MaxRetries}, Language={Language})";
}
