using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Flashpi.Models;

namespace Flashpi.Internal;

/// <summary>The exception of an error answer.</summary>
internal static class ApiErrors
{
    /// <summary>The exception class that matches the status of an answer.</summary>
    internal static ApiException Create(HttpResponseMessage response, byte[] body)
    {
        var text = Text.Decode(body);
        var problem = ParseProblem(text);
        return (int)response.StatusCode switch
        {
            400 => new BadRequestException(response, problem, text),
            401 => new AuthenticationException(response, problem, text),
            403 => new PermissionDeniedException(response, problem, text),
            404 => new NotFoundException(response, problem, text),
            409 => new ConflictException(response, problem, text),
            422 => new UnprocessableEntityException(response, problem, text),
            429 => new RateLimitException(response, problem, text),
            >= 500 => new InternalServerException(response, problem, text),
            _ => new ApiException(response, problem, text),
        };
    }

    internal static string Message(int status, ProblemDetails? problem, string bodyText)
    {
        if (problem is not null)
        {
            var detail = problem.Detail ?? problem.Title;
            return detail is null
                ? string.Format(CultureInfo.InvariantCulture, "{0} {1}", status, problem.Code)
                : string.Format(CultureInfo.InvariantCulture, "{0} {1}: {2}", status, problem.Code, detail);
        }
        var excerpt = string.Join(" ", bodyText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (excerpt.Length > 200)
        {
            excerpt = excerpt.Substring(0, 200);
        }
        return excerpt.Length > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0} {1}", status, excerpt)
            : string.Format(CultureInfo.InvariantCulture, "{0} (no response body)", status);
    }

    /// <summary>
    /// The Problem Details of an error body: a JSON object with a text <c>code</c>. Anything else
    /// (an answer from the edge, a CDN or a firewall) has none.
    /// </summary>
    internal static ProblemDetails? ParseProblem(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("code", out var code)
                || code.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            try
            {
                return root.Deserialize(Json.TypeInfo<ProblemDetails>());
            }
            catch (JsonException)
            {
                // A member of another type than the contract says: keep what can be read.
                return new ProblemDetails
                {
                    Type = Member(root, "type")!,
                    Title = Member(root, "title")!,
                    Status = root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Number ? status.GetDouble() : 0,
                    Detail = Member(root, "detail")!,
                    Instance = Member(root, "instance")!,
                    Code = new ErrorCode(code.GetString()!),
                    Errors = [],
                    RequestId = Member(root, "requestId")!,
                };
            }
        }
    }

    private static string? Member(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>Text of answers.</summary>
internal static class Text
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    /// <summary>UTF-8 text, with invalid bytes replaced (as the Node SDK's TextDecoder does).</summary>
    internal static string Decode(byte[] body) => Utf8.GetString(body);
}

/// <summary>Headers of answers, in any letter case.</summary>
internal static class HeaderValues
{
    internal static string? Get(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values)
            || (response.Content is not null && response.Content.Headers.TryGetValues(name, out values)))
        {
            return string.Join(", ", values);
        }
        return null;
    }
}

/// <summary>The <c>Retry-After</c> header.</summary>
internal static class RetryAfterHeader
{
    // Longer digit strings are past any useful wait.
    private const int MaxDeltaDigits = 18;

    private static readonly string[] DateFormats = ["r", "ddd, d MMM yyyy H:m:s 'GMT'", "dddd, dd-MMM-yy HH:mm:ss 'GMT'", "ddd MMM d HH:mm:ss yyyy"];

    /// <summary>The wait a <c>Retry-After</c> value asks for (delta-seconds or an HTTP date), or null.</summary>
    internal static TimeSpan? Parse(string? value, DateTimeOffset now)
    {
        if (value is null)
        {
            return null;
        }
        var trimmed = value.Trim();
        if (trimmed.Length > 0 && trimmed.All(character => character is >= '0' and <= '9'))
        {
            if (trimmed.Length > MaxDeltaDigits)
            {
                return TimeSpan.MaxValue;
            }
            var seconds = long.Parse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture);
            return seconds >= (long)TimeSpan.MaxValue.TotalSeconds ? TimeSpan.MaxValue : TimeSpan.FromSeconds(seconds);
        }
        if (!DateTimeOffset.TryParseExact(trimmed, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AllowWhiteSpaces, out var moment)
            && !DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out moment))
        {
            return null;
        }
        var wait = Math.Ceiling((moment - now).TotalSeconds);
        return TimeSpan.FromSeconds(Math.Max(0, wait));
    }
}
