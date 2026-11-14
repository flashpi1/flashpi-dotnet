using System;
using System.Collections.Generic;
using System.Net.Http;
using Flashpi.Internal;
using Flashpi.Models;

namespace Flashpi;

/// <summary>
/// Every error the SDK throws extends this class, except misused arguments
/// (<see cref="ArgumentException"/>) and cancellation through your <c>CancellationToken</c>
/// (<see cref="OperationCanceledException"/>).
/// </summary>
public class FlashpiException : Exception
{
    /// <summary>An error with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public FlashpiException(string message)
        : base(message)
    {
    }

    /// <summary>An error with a message and the error that caused it.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public FlashpiException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// The Idempotency-Key a failed POST used (yours or the one the SDK made). To try the same
    /// operation again later without issuing twice, pass it back:
    /// <c>new RequestOptions { IdempotencyKey = error.IdempotencyKey }</c>.
    /// </summary>
    public string? IdempotencyKey { get; internal set; }
}

/// <summary>
/// The API answered with an error status. Branch on <see cref="Code"/> (stable English slugs such
/// as <c>validation_failed</c>), never on the message, which follows the requested language.
/// </summary>
public class ApiException : FlashpiException
{
    internal ApiException(HttpResponseMessage response, ProblemDetails? problem, string bodyText)
        : base(ApiErrors.Message((int)response.StatusCode, problem, bodyText))
    {
        Response = response;
        Problem = problem;
        StatusCode = (int)response.StatusCode;
        Code = problem?.Code;
        Title = problem?.Title;
        Detail = problem?.Detail;
        Errors = problem?.Errors ?? [];
        RequestId = problem?.RequestId ?? HeaderValues.Get(response, "x-request-id");
    }

    /// <summary>The HTTP status of the answer.</summary>
    public int StatusCode { get; }

    /// <summary>The error code, or null when the answer had no Problem Details (for example a 502).</summary>
    public ErrorCode? Code { get; }

    /// <summary>A short summary of the problem type.</summary>
    public string? Title { get; }

    /// <summary>What went wrong in this request.</summary>
    public string? Detail { get; }

    /// <summary>The field-level problems of a request body or query (may be empty).</summary>
    public IReadOnlyList<FieldError> Errors { get; }

    /// <summary>Flashpi's id of the request (<c>x-request-id</c>): share it with support.</summary>
    public string? RequestId { get; }

    /// <summary>
    /// The whole Problem Details body (RFC 9457) with the members some codes add
    /// (<c>DocumentId</c>, <c>Limit</c>, <c>ResetsAt</c>, <c>RideStatus</c>, ...), when there was one.
    /// </summary>
    public ProblemDetails? Problem { get; }

    /// <summary>The HTTP answer, with its headers. Its body was already read.</summary>
    public HttpResponseMessage Response { get; }

    /// <summary>
    /// How long the API asked to wait before trying again (<c>Retry-After</c>), or null. Daily quota
    /// errors point at the next midnight in Ecuador, so this can be hours.
    /// </summary>
    public TimeSpan? RetryAfter => RetryAfterHeader.Parse(HeaderValues.Get(Response, "retry-after"), DateTimeOffset.UtcNow);

    /// <summary>A header of the answer in any letter case (repeated ones joined with commas), or null.</summary>
    /// <param name="name">The header's name, such as <c>Content-Language</c>.</param>
    public string? GetHeader(string name) => HeaderValues.Get(Response, name);
}

/// <summary>400: the request could not be read (malformed JSON, invalid Idempotency-Key, ...).</summary>
public sealed class BadRequestException : ApiException
{
    internal BadRequestException(HttpResponseMessage response, ProblemDetails? problem, string bodyText)
        : base(response, problem, bodyText)
    {
    }
}

/// <summary>401: the API key is missing, malformed, unknown or revoked.</summary>
public sealed class AuthenticationException : ApiException
{
    internal AuthenticationException(HttpResponseMessage response, ProblemDetails? problem, string bodyText)
        : base(response, problem, bodyText)
    {
    }
}

/// <summary>403: the key may not do this (read-only, another issuer, IP allowlist, suspended account).</summary>
public sealed class PermissionDeniedException : ApiException
{
    internal PermissionDeniedException(HttpResponseMessage response, ProblemDetails? problem, string bodyText)
        : base(response, problem, bodyText)
    {
    }
}

/// <summary>404: the resource does not exist in this mode (test or live) for this account.</summary>
public sealed class NotFoundException : ApiException
{
    internal NotFoundException(HttpResponseMessage response, ProblemDetails? problem, string bodyText)
        : base(response, problem, bodyText)
    {
    }
}

/// <summary>409: the request conflicts with the current state (sequential used, RIDE not ready, ...).</summary>
public sealed class ConflictException : ApiException
{
    internal ConflictException(HttpResponseMessage response, ProblemDetails? problem, string bodyText)
        : base(response, problem, bodyText)
    {
    }
}

/// <summary>422: the request is well formed but invalid (<c>validation_failed</c>, <c>total_mismatch</c>, ...).</summary>
public sealed class UnprocessableEntityException : ApiException
{
    internal UnprocessableEntityException(HttpResponseMessage response, ProblemDetails? problem, string bodyText)
        : base(response, problem, bodyText)
    {
    }
}

/// <summary>429: a limit was reached; see <see cref="ApiException.RetryAfter"/> and the problem's <c>Limit</c> and <c>ResetsAt</c>.</summary>
public sealed class RateLimitException : ApiException
{
    internal RateLimitException(HttpResponseMessage response, ProblemDetails? problem, string bodyText)
        : base(response, problem, bodyText)
    {
    }
}

/// <summary>5xx: Flashpi failed to answer. Requests are retried automatically before this is thrown.</summary>
public sealed class InternalServerException : ApiException
{
    internal InternalServerException(HttpResponseMessage response, ProblemDetails? problem, string bodyText)
        : base(response, problem, bodyText)
    {
    }
}

/// <summary>The request never got an answer (DNS, TLS, connection reset, ...).</summary>
public class ApiConnectionException : FlashpiException
{
    /// <summary>A request that got no answer.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The network error.</param>
    public ApiConnectionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>An attempt took longer than its timeout.</summary>
public sealed class ApiTimeoutException : ApiConnectionException
{
    /// <summary>A request that timed out.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause, if any.</param>
    public ApiTimeoutException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>Why a webhook was rejected.</summary>
public enum WebhookVerificationReason
{
    /// <summary>The <c>webhook-id</c>, <c>webhook-timestamp</c> or <c>webhook-signature</c> header is missing.</summary>
    MissingHeaders,

    /// <summary>The <c>webhook-timestamp</c> is not a Unix time in seconds.</summary>
    InvalidTimestamp,

    /// <summary>The <c>webhook-timestamp</c> is too far from now (5 minutes by default).</summary>
    TimestampOutOfRange,

    /// <summary>No signature matches the body and the secret.</summary>
    NoMatchingSignature,

    /// <summary>The secret is not a complete <c>whsec_</c> secret.</summary>
    InvalidSecret,

    /// <summary>The signed body is not a JSON object the SDK can read.</summary>
    InvalidPayload,
}

/// <summary>
/// A webhook did not come from Flashpi, was altered, or is too old: answer 400 and ignore it.
/// </summary>
public sealed class WebhookVerificationException : FlashpiException
{
    /// <summary>A rejected webhook.</summary>
    /// <param name="reason">Why it was rejected.</param>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause, if any.</param>
    public WebhookVerificationException(WebhookVerificationReason reason, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
    }

    /// <summary>Why it was rejected.</summary>
    public WebhookVerificationReason Reason { get; }
}
