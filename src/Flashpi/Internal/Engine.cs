using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Flashpi.Internal;

/// <summary>A successful answer, its body read.</summary>
/// <param name="Response">The HTTP answer, its body replaced by the bytes read.</param>
/// <param name="Body">The body.</param>
/// <param name="RequestId">Flashpi's id of the request.</param>
/// <param name="IdempotencyKey">The key the call used, for errors reading the body.</param>
internal sealed record RawResponse(HttpResponseMessage Response, byte[] Body, string? RequestId, string? IdempotencyKey);

/// <summary>
/// Requests to the Flashpi API: retries, timeouts, idempotency and typed errors, as the other SDKs
/// send them.
/// </summary>
internal sealed class Engine
{
    internal const string UserAgent = "flashpi-dotnet/" + SdkVersion.Value;

    /// <summary>Longest Retry-After the SDK waits for by itself; longer ones (daily quotas) are thrown.</summary>
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(8);

    // Waits between checks on an earlier attempt that the API is still running.
    private static readonly TimeSpan InProgressFirstWait = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan InProgressMaxWait = TimeSpan.FromSeconds(5);
    private const string InProgress = "idempotency_key_in_progress";

    private readonly HttpClient _http;

    internal Engine(ClientConfig config, HttpClient http, ClientHooks hooks)
    {
        Config = config;
        _http = http;
        Hooks = hooks;
    }

    internal ClientConfig Config { get; }

    internal ClientHooks Hooks { get; }

    /// <summary>A JSON answer as its model.</summary>
    internal async Task<ApiResponse<T>> JsonAsync<T>(RequestSpec spec, CancellationToken cancellationToken)
    {
        var raw = await SendAsync(spec, cancellationToken).ConfigureAwait(false);
        return new ApiResponse<T>(ReadJson<T>(raw), raw.Response, raw.RequestId);
    }

    /// <summary>An answer as text (an XML).</summary>
    internal async Task<ApiResponse<string>> TextAsync(RequestSpec spec, CancellationToken cancellationToken)
    {
        var raw = await SendAsync(spec with { Response = ResponseKind.Text }, cancellationToken).ConfigureAwait(false);
        return new ApiResponse<string>(Text.Decode(raw.Body), raw.Response, raw.RequestId);
    }

    /// <summary>An answer as bytes (a PDF).</summary>
    internal async Task<ApiResponse<byte[]>> BytesAsync(RequestSpec spec, CancellationToken cancellationToken)
    {
        var raw = await SendAsync(spec with { Response = ResponseKind.Bytes }, cancellationToken).ConfigureAwait(false);
        return new ApiResponse<byte[]>(raw.Body, raw.Response, raw.RequestId);
    }

    /// <summary>An answer without a body (a DELETE).</summary>
    internal async Task<ApiResponse> NoContentAsync(RequestSpec spec, CancellationToken cancellationToken)
    {
        var raw = await SendAsync(spec with { Response = ResponseKind.None }, cancellationToken).ConfigureAwait(false);
        return new ApiResponse(raw.Response, raw.RequestId);
    }

    /// <summary>A list: <c>await foreach</c> walks every item, <c>await</c> gives the first page.</summary>
    internal Paginator<TItem> List<TList, TItem>(RequestSpec spec, CancellationToken listToken)
        where TList : IListBody<TItem> =>
        new(async token => (await PageAsync<TList, TItem>(spec, token, listToken).ConfigureAwait(false)).Data, listToken);

    /// <summary>One page of a list, with the HTTP answer; its next page keeps the same filters and options.</summary>
    internal async Task<ApiResponse<Page<TItem>>> PageAsync<TList, TItem>(RequestSpec spec, CancellationToken token, CancellationToken listToken)
        where TList : IListBody<TItem>
    {
        var result = await JsonAsync<TList>(spec, token).ConfigureAwait(false);
        var page = new Page<TItem>(
            result.Data.Data,
            result.Data.Pagination,
            async (cursor, next) => (await PageAsync<TList, TItem>(spec.WithCursor(cursor), next, listToken).ConfigureAwait(false)).Data,
            listToken);
        return new ApiResponse<Page<TItem>>(page, result.Response, result.RequestId);
    }

    private static T ReadJson<T>(RawResponse raw)
    {
        var status = (int)raw.Response.StatusCode;
        if (status == 204 || raw.Body.Length == 0)
        {
            throw new FlashpiException(string.Format(CultureInfo.InvariantCulture, "The API answered {0} without a body", status))
            {
                IdempotencyKey = raw.IdempotencyKey,
            };
        }
        try
        {
            return JsonSerializer.Deserialize(raw.Body, Json.TypeInfo<T>())
                ?? throw new JsonException("null");
        }
        catch (JsonException error)
        {
            throw new FlashpiException(
                string.Format(CultureInfo.InvariantCulture, "The API answered {0} with a body that is not the JSON of a {1}", status, typeof(T).Name),
                error)
            {
                IdempotencyKey = raw.IdempotencyKey,
            };
        }
    }

    /// <summary>One call, with its retries and recoveries.</summary>
    internal async Task<RawResponse> SendAsync(RequestSpec spec, CancellationToken cancellationToken)
    {
        var call = Prepare(spec);
        var started = Hooks.Elapsed();
        int attempt = 0, retries = 0, waits = 0;
        // Whether an earlier attempt may have reached the API without its answer reaching us (a
        // network error, a timeout, or a 5xx from a proxy). Only then can a 409
        // idempotency_key_in_progress be this call's own attempt still running.
        var unanswered = false;
        while (true)
        {
            FlashpiException failure;
            ExceptionDispatchInfo? thrown = null;
            try
            {
                var outcome = await AttemptAsync(spec, call, cancellationToken).ConfigureAwait(false);
                if (outcome.Error is null)
                {
                    return outcome.Response!;
                }
                failure = outcome.Error;
            }
            catch (ApiConnectionException error)
            {
                failure = error;
                thrown = ExceptionDispatchInfo.Capture(error);
            }
            catch (FlashpiException error)
            {
                error.IdempotencyKey = call.IdempotencyKey;
                throw;
            }
            failure.IdempotencyKey = call.IdempotencyKey;

            if (attempt > 0 && failure is ApiException api)
            {
                var documentId = RecoverableDocument(spec, api);
                if (documentId is not null)
                {
                    return await RecoverDocumentAsync(call, documentId, cancellationToken).ConfigureAwait(false);
                }
                // A DELETE retried after a lost answer finds the resource already gone.
                if (spec.Method == HttpMethod.Delete && api.StatusCode == 404)
                {
                    return Deleted(api, call.IdempotencyKey);
                }
                // This call's earlier attempt is still running on the API (a sync issue whose
                // answer was lost): wait for it with the same key while the call has time left.
                // These checks do not count as retries; once the time is up, the retry rules apply.
                if (unanswered && api.StatusCode == 409 && api.Code?.Value == InProgress)
                {
                    var wait = Min(Scale(InProgressFirstWait, waits), InProgressMaxWait);
                    if (Hooks.Elapsed() + wait - started <= call.Timeout)
                    {
                        waits++;
                        attempt++;
                        await Hooks.Delay(wait, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }
            }

            if (failure is ApiConnectionException || failure is ApiException { StatusCode: >= 500, Problem: null })
            {
                unanswered = true;
            }
            var delay = RetryDelay(failure, retries);
            if (delay is null || retries >= call.MaxRetries)
            {
                thrown?.Throw();
                throw failure;
            }
            retries++;
            attempt++;
            await Hooks.Delay(delay.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The wait before retrying, or null when the failure must not be retried.</summary>
    /// <remarks>
    /// Retried: network errors and timeouts, 408, 429 and 5xx except 501 (with a Retry-After of
    /// 60 seconds or less), and 409 <c>idempotency_key_in_progress</c>. POSTs are safe to retry
    /// because they keep their Idempotency-Key.
    /// </remarks>
    internal TimeSpan? RetryDelay(FlashpiException failure, int retries)
    {
        if (failure is ApiConnectionException)
        {
            return Backoff(retries);
        }
        if (failure is not ApiException api)
        {
            return null;
        }
        if (api.StatusCode == 409)
        {
            return api.Code?.Value == InProgress ? Max(InProgressFirstWait, Backoff(retries)) : null;
        }
        var transient = api.StatusCode is 408 or 429 || (api.StatusCode >= 500 && api.StatusCode != 501);
        // The shared demo certificate is missing: no retry within seconds will fix that.
        if (!transient || api.Code?.Value == "demo_issuer_unavailable")
        {
            return null;
        }
        var retryAfter = RetryAfterHeader.Parse(HeaderValues.Get(api.Response, "retry-after"), Hooks.UtcNow());
        if (retryAfter is null)
        {
            return Backoff(retries);
        }
        return retryAfter <= MaxRetryAfter ? retryAfter : null;
    }

    /// <summary>Exponential backoff from 0.5 s up to 8 s, minus up to 25 % of jitter.</summary>
    internal TimeSpan Backoff(int retries)
    {
        var delay = Min(Scale(InitialRetryDelay, retries), MaxRetryDelay);
        var milliseconds = Math.Round(delay.TotalMilliseconds * (1 - (Hooks.Random() * 0.25)));
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static TimeSpan Scale(TimeSpan initial, int steps) =>
        TimeSpan.FromTicks(initial.Ticks * (1L << Math.Min(steps, 16)));

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;

    private static string? RecoverableDocument(RequestSpec spec, ApiException failure)
    {
        if (!spec.RecoverDocument || failure.StatusCode != 422 || failure.Code?.Value != "idempotency_key_reused")
        {
            return null;
        }
        var documentId = failure.Problem?.DocumentId;
        return string.IsNullOrEmpty(documentId) ? null : documentId;
    }

    /// <summary>
    /// Reads the document an earlier attempt of the call created. A failure keeps the call's key,
    /// so trying again later with that key returns the document instead of issuing another.
    /// </summary>
    private async Task<RawResponse> RecoverDocumentAsync(Call call, string documentId, CancellationToken cancellationToken)
    {
        try
        {
            var read = new RequestSpec
            {
                Method = HttpMethod.Get,
                Path = Paths.Of("/v1/documents/{}", documentId),
                Options = call.Options with { IdempotencyKey = null },
            };
            var recovered = await SendAsync(read, cancellationToken).ConfigureAwait(false);
            return recovered with { IdempotencyKey = call.IdempotencyKey };
        }
        catch (FlashpiException error)
        {
            error.IdempotencyKey = call.IdempotencyKey;
            throw;
        }
    }

    /// <summary>The result of a DELETE whose resource is gone: a 204 with the 404's headers.</summary>
    private static RawResponse Deleted(ApiException failure, string? idempotencyKey)
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.NoContent)
        {
            Content = new ByteArrayContent([]),
            RequestMessage = failure.Response.RequestMessage,
            Version = failure.Response.Version,
        };
        foreach (var header in failure.Response.Headers)
        {
            response.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return new RawResponse(response, [], failure.RequestId, idempotencyKey);
    }

    // Before the first attempt.

    /// <summary>What every attempt of one call shares, resolved before the first one is sent.</summary>
    private sealed record Call(
        RequestOptions Options,
        int MaxRetries,
        TimeSpan Timeout,
        string Url,
        string? IdempotencyKey,
        string? Language,
        string? CorrelationId,
        IReadOnlyList<KeyValuePair<string, string>> ExtraHeaders);

    /// <summary>Checks the options and builds what the attempts share. Throws before anything is sent.</summary>
    private Call Prepare(RequestSpec spec)
    {
        var options = spec.Options ?? new RequestOptions();
        var extraHeaders = Checks.Headers("ExtraHeaders", options.ExtraHeaders);
        var idempotencyKey = options.IdempotencyKey is null ? null : Checks.HeaderValue("IdempotencyKey", options.IdempotencyKey);
        var correlationId = options.CorrelationId is null ? null : Checks.HeaderValue("CorrelationId", options.CorrelationId);
        var language = Checks.Language("Language", options.Language);
        var maxRetries = options.MaxRetries is { } retries ? Checks.Retries("MaxRetries", retries) : Config.MaxRetries;
        var timeout = options.Timeout is { } requested ? Checks.Timeout("Timeout", requested) : Config.Timeout;
        if (spec.MinTimeout > timeout)
        {
            timeout = spec.MinTimeout > Checks.MaxTimeout ? Checks.MaxTimeout : spec.MinTimeout;
        }
        string? key = null;
        if (spec.Method == HttpMethod.Post)
        {
            // The caller's key (the option, else the last spelling of the header), or a new one.
            key = idempotencyKey;
            if (key is null)
            {
                foreach (var header in extraHeaders)
                {
                    if (string.Equals(header.Key, "Idempotency-Key", StringComparison.OrdinalIgnoreCase))
                    {
                        key = header.Value;
                    }
                }
            }
            key ??= Guid.NewGuid().ToString();
        }
        return new Call(
            options,
            maxRetries,
            timeout,
            Paths.Url(Config.BaseUrl, spec.Path, spec.Query),
            key,
            language,
            correlationId,
            extraHeaders);
    }

    // One attempt.

    private sealed record AttemptOutcome(RawResponse? Response, ApiException? Error);

    /// <summary>
    /// One attempt within its deadline, from sending it to reading the whole answer: a token that
    /// cancels at the timeout covers both, and also disposes the answer, which ends a read that the
    /// platform's stream would not cancel (.NET Framework).
    /// </summary>
    private async Task<AttemptOutcome> AttemptAsync(RequestSpec spec, Call call, CancellationToken cancellationToken)
    {
        using var request = BuildRequest(spec, call);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(call.Timeout);
        try
        {
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (IsTransferFailure(error))
            {
                throw TransferFailure(spec, call, error, deadline, cancellationToken);
            }

            byte[] body;
            try
            {
                using var abort = deadline.Token.Register(static state => ((HttpResponseMessage)state!).Dispose(), response);
                body = await ReadBodyAsync(response.Content, deadline.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (IsTransferFailure(error))
            {
                response.Dispose();
                throw TransferFailure(spec, call, error, deadline, cancellationToken);
            }

            var original = response.Content;
            response.Content = new ByteArrayContent(body);
            if (original is not null)
            {
                foreach (var header in original.Headers)
                {
                    if (!string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                    {
                        response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }
                original.Dispose();
            }

            var status = (int)response.StatusCode;
            if (status is < 200 or > 299)
            {
                return new AttemptOutcome(null, ApiErrors.Create(response, body));
            }
            var requestId = HeaderValues.Get(response, "x-request-id");
            return new AttemptOutcome(new RawResponse(response, body, requestId, call.IdempotencyKey), null);
        }
        finally
        {
            // The answer keeps its request: never with the API key in it.
            request.Headers.Authorization = null;
        }
    }

    private static async Task<byte[]> ReadBodyAsync(HttpContent? content, CancellationToken cancellationToken)
    {
        if (content is null)
        {
            return [];
        }
#if NET
        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
        var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
        using (stream)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
            return buffer.ToArray();
        }
    }

    private static bool IsTransferFailure(Exception error) =>
        error is OperationCanceledException or HttpRequestException or IOException or ObjectDisposedException;

    /// <summary>
    /// The error of an attempt that got no complete answer. Cancelling through the caller's token
    /// is the caller's <see cref="OperationCanceledException"/>, never a retried failure.
    /// </summary>
    private static Exception TransferFailure(
        RequestSpec spec, Call call, Exception error, CancellationTokenSource deadline, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new OperationCanceledException("The call was cancelled", error, cancellationToken);
        }
        var method = spec.Method.Method;
        if (deadline.IsCancellationRequested || error is OperationCanceledException)
        {
            var seconds = call.Timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
            return new ApiTimeoutException($"{method} {call.Url} timed out after {seconds} seconds", error);
        }
        var reason = error.Message.Length > 0 ? error.Message : error.GetType().Name;
        return new ApiConnectionException($"{method} {call.Url} failed: {reason}", error);
    }

    private HttpRequestMessage BuildRequest(RequestSpec spec, Call call)
    {
        var request = new HttpRequestMessage(spec.Method, call.Url);
        MediaTypeHeaderValue? multipartType = null;
        if (spec.Multipart is { } form)
        {
            request.Content = form.CreateContent();
            multipartType = request.Content.Headers.ContentType;
        }
        else if (spec.Body is { } body)
        {
            request.Content = new ByteArrayContent(body);
        }

        // From the lowest priority to the highest: the SDK's defaults, the client's language and
        // headers, the call's language and headers, then the SDK's own.
        var headers = new List<KeyValuePair<string, string>>();
        Set(headers, "Accept", spec.Accept);
        Set(headers, "User-Agent", UserAgent);
        if (Config.Language is { } clientLanguage)
        {
            Set(headers, "Accept-Language", clientLanguage);
        }
        foreach (var header in Config.DefaultHeaders)
        {
            Set(headers, header.Key, header.Value);
        }
        if (call.Language is { } language)
        {
            Set(headers, "Accept-Language", language);
        }
        foreach (var header in call.ExtraHeaders)
        {
            Set(headers, header.Key, header.Value);
        }
        if (call.CorrelationId is { } correlationId)
        {
            Set(headers, "X-Correlation-Id", correlationId);
        }
        if (call.IdempotencyKey is { } key)
        {
            Set(headers, "Idempotency-Key", key);
        }

        foreach (var header in headers)
        {
            if (string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value) && request.Content is not null)
            {
                // A content header (Content-Type, Content-Language, ...): only with a body.
                request.Content.Headers.Remove(header.Key);
                request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        if (multipartType is not null)
        {
            // The multipart type with its boundary.
            request.Content!.Headers.ContentType = multipartType;
        }
        else if (spec.Body is not null)
        {
            request.Content!.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Config.ApiKey);
        return request;
    }

    /// <summary>Sets a header, replacing an earlier one of the same name in any letter case.</summary>
    private static void Set(List<KeyValuePair<string, string>> headers, string name, string value)
    {
        headers.RemoveAll(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase));
        headers.Add(new KeyValuePair<string, string>(name, value));
    }
}
