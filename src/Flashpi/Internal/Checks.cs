using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Flashpi.Internal;

/// <summary>Checks of the settings and of every value that becomes a header.</summary>
internal static class Checks
{
    /// <summary>The longest timeout: 2147483647 ms, as in the Node SDK (and CancelAfter's limit).</summary>
    internal static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(int.MaxValue);

    // What JavaScript's trim() removes around a string, the Node SDK's key included: white space,
    // line breaks and the byte order mark (U+FEFF).
    private const string JsSpace = "\t\n\v\f\r \u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000\ufeff";

    // Trimmed from both ends of a header value, as the Fetch standard's Headers does.
    private static readonly char[] HttpWhitespace = [' ', '\t', '\r', '\n'];

    private static readonly char[] JsSpaces = JsSpace.ToCharArray();

    // Headers HttpClient sets from the body: another value would frame it wrongly.
    private static readonly HashSet<string> Framing = new(StringComparer.OrdinalIgnoreCase) { "Content-Length", "Transfer-Encoding" };

    internal static TimeSpan Timeout(string name, TimeSpan value)
    {
        if (value <= TimeSpan.Zero || value > MaxTimeout)
        {
            throw new FlashpiException($"{name} must be more than 0 and at most 2147483.647 seconds");
        }
        return value;
    }

    internal static int Retries(string name, int value) =>
        value >= 0 ? value : throw new FlashpiException($"{name} must be a whole number, 0 or more");

    /// <summary>
    /// A header name: an RFC 9110 token, refused with <see cref="ArgumentException"/> otherwise
    /// (names are not secret, so the message shows it).
    /// </summary>
    internal static string HeaderName(string name)
    {
        if (name is null)
        {
            throw new ArgumentException("A header name cannot be null");
        }
        if (name.Length == 0 || !IsToken(name))
        {
            throw new ArgumentException($"'{name}' is not a valid header name");
        }
        return name;
    }

    /// <summary>
    /// A header value without the spaces, tabs and line breaks around it, as the other SDKs send
    /// it. Checked before anything is sent, so a bad value is never retried as a network failure: a
    /// line break or another control character other than a tab left inside it, or a character
    /// outside ASCII (which HttpClient refuses or cannot encode), throws
    /// <see cref="ArgumentException"/>. The message never shows the value, which may be a secret.
    /// </summary>
    internal static string HeaderValue(string label, string value)
    {
        if (value is null)
        {
            throw new ArgumentException($"{label} cannot be null");
        }
        var trimmed = value.Trim(HttpWhitespace);
        foreach (var character in trimmed)
        {
            if ((character < ' ' && character != '\t') || character == '\u007f')
            {
                throw new ArgumentException($"{label} has a line break or another control character");
            }
            if (character > '~')
            {
                throw new ArgumentException($"{label} has a character outside ASCII, which HttpClient cannot send in a header");
            }
        }
        return trimmed;
    }

    /// <summary>Checked headers (<see cref="HeaderName"/>, <see cref="HeaderValue"/>), in order.
    /// Content-Length and Transfer-Encoding are refused: HttpClient sets them from the body.</summary>
    internal static List<KeyValuePair<string, string>> Headers(string label, IReadOnlyDictionary<string, string>? headers)
    {
        var checkedHeaders = new List<KeyValuePair<string, string>>();
        if (headers is null)
        {
            return checkedHeaders;
        }
        foreach (var header in headers)
        {
            var name = HeaderName(header.Key);
            if (Framing.Contains(name))
            {
                throw new ArgumentException($"'{name}' is set by the SDK from the body: leave it out of {label}");
            }
            checkedHeaders.Add(new KeyValuePair<string, string>(name, HeaderValue($"The value of header '{name}'", header.Value)));
        }
        return checkedHeaders;
    }

    /// <summary>A language to send, or null when it is empty (an empty language sends none).</summary>
    internal static string? Language(string label, Language? language)
    {
        if (language is not { } value)
        {
            return null;
        }
        var text = HeaderValue(label, value.Value);
        return text.Length == 0 ? null : text;
    }

    /// <summary>The API key: the argument or FLASHPI_API_KEY, trimmed as JavaScript's trim() does.</summary>
    internal static string ApiKey(string? apiKey)
    {
        var key = (apiKey ?? System.Environment.GetEnvironmentVariable("FLASHPI_API_KEY") ?? string.Empty).Trim(JsSpaces);
        if (key.Length == 0)
        {
            throw new FlashpiException(
                "Missing API key: pass it to new FlashpiClient(...) or set the FLASHPI_API_KEY environment variable");
        }
        if (key.StartsWith("whsec_", StringComparison.Ordinal))
        {
            throw new FlashpiException(
                "That is a webhook signing secret (whsec_...), not an API key: use it with Webhooks.Unwrap");
        }
        if (!key.StartsWith("fp_test_", StringComparison.Ordinal) && !key.StartsWith("fp_live_", StringComparison.Ordinal))
        {
            throw new FlashpiException("The API key must start with fp_test_ or fp_live_");
        }
        foreach (var character in key)
        {
            if (character is < '!' or > '~')
            {
                // Never the key itself.
                throw new FlashpiException(
                    "The API key has spaces, line breaks or other characters an API key cannot have: copy it again from the Flashpi console");
            }
        }
        return key;
    }

    /// <summary>The base URL without its trailing slashes, or a FlashpiException.</summary>
    internal static string BaseUrl(Uri? baseUrl)
    {
        var url = baseUrl ?? new Uri("https://api.flashpi.com.ec");
        if (!url.IsAbsoluteUri
            || (url.Scheme != "http" && url.Scheme != "https")
            || url.Host.Length == 0
            || url.Query.Length > 0
            || url.Fragment.Length > 0)
        {
            throw new FlashpiException($"BaseUrl must be an http:// or https:// URL without a query, got {url.OriginalString}");
        }
        return url.AbsoluteUri.TrimEnd('/');
    }

    private static bool IsToken(string name)
    {
        foreach (var character in name)
        {
            var allowed = character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'
                or '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';
            if (!allowed)
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>Checks of arguments.</summary>
internal static class Checked
{
    /// <summary>A request body, which cannot be null.</summary>
    internal static T Body<T>(T body)
        where T : class => NotNull(body, nameof(body));

    /// <summary>An argument that cannot be null (ArgumentNullException.ThrowIfNull, which netstandard2.0 lacks).</summary>
    internal static T NotNull<T>(T? value, string name)
        where T : class => value ?? throw new ArgumentNullException(name);
}

/// <summary>Paths and queries of requests.</summary>
internal static class Paths
{
    /// <summary>
    /// A path from a template with <c>{}</c> placeholders, each id percent-encoded as
    /// encodeURIComponent does. Refuses missing ids, and <c>.</c> and <c>..</c>, which URL
    /// normalization would turn into another path.
    /// </summary>
    internal static string Of(string template, params string?[] ids)
    {
        var parts = template.Split(["{}"], StringSplitOptions.None);
        if (parts.Length != ids.Length + 1)
        {
            throw new ArgumentException($"{template} has {parts.Length - 1} placeholders, not {ids.Length}");
        }
        var shown = template.Replace("{}", "{id}");
        var path = new StringBuilder(parts[0]);
        for (var index = 0; index < ids.Length; index++)
        {
            var id = ids[index];
            if (id is null || id.Trim().Length == 0)
            {
                throw new FlashpiException(id is null ? $"Missing id in {shown}: got null" : $"Missing id in {shown}: got \"{id}\"");
            }
            if (id is "." or "..")
            {
                throw new FlashpiException($"Invalid id in {shown}: \"{id}\"");
            }
            path.Append(Encode(id, plusForSpace: false)).Append(parts[index + 1]);
        }
        return path.ToString();
    }

    /// <summary>The URL of a request.</summary>
    internal static string Url(string baseUrl, string path, IReadOnlyList<KeyValuePair<string, string>>? query)
    {
        var url = new StringBuilder(baseUrl).Append(path);
        if (query is { Count: > 0 })
        {
            url.Append('?');
            for (var index = 0; index < query.Count; index++)
            {
                if (index > 0)
                {
                    url.Append('&');
                }
                url.Append(Encode(query[index].Key, plusForSpace: true)).Append('=').Append(Encode(query[index].Value, plusForSpace: true));
            }
        }
        return url.ToString();
    }

    /// <summary>
    /// Percent-encoding of UTF-8: in a path, every character but those encodeURIComponent keeps;
    /// in a query, as an HTML form (spaces as <c>+</c>, only letters, digits and <c>-_.~</c> kept).
    /// Lone surrogates, which have no UTF-8, throw <see cref="ArgumentException"/>.
    /// </summary>
    private static string Encode(string value, bool plusForSpace)
    {
        byte[] bytes;
        try
        {
            bytes = new UTF8Encoding(false, throwOnInvalidBytes: true).GetBytes(value);
        }
        catch (EncoderFallbackException)
        {
            throw new ArgumentException("A path id or a query value has a character that cannot be sent as UTF-8");
        }
        var encoded = new StringBuilder(bytes.Length);
        foreach (var current in bytes)
        {
            var character = (char)current;
            var keep = character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.' or '~'
                || (!plusForSpace && character is '!' or '*' or '\'' or '(' or ')');
            if (keep)
            {
                encoded.Append(character);
            }
            else if (plusForSpace && character == ' ')
            {
                encoded.Append('+');
            }
            else
            {
                encoded.Append('%').Append(current.ToString("X2", CultureInfo.InvariantCulture));
            }
        }
        return encoded.ToString();
    }
}
