using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flashpi.Internal;
using Flashpi.Models;

namespace Flashpi;

/// <summary>
/// Verification of the webhooks Flashpi sends (Standard Webhooks). They need no API key.
/// </summary>
/// <remarks>
/// A webhook is signed with HMAC-SHA256 over <c>webhook-id.webhook-timestamp.body</c> using the
/// endpoint's <c>whsec_</c> secret. During a secret rotation Flashpi signs with both secrets, so
/// the <c>webhook-signature</c> header lists several signatures; any of them is enough.
/// </remarks>
public static class Webhooks
{
    private const string SecretPrefix = "whsec_";

    // Key sizes of a Standard Webhooks secret (Flashpi makes 32-byte keys).
    private const int MinSecretBytes = 24;
    private const int MaxSecretBytes = 64;
    private const int SignatureBytes = 32;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The default largest gap between a webhook's timestamp and your clock: 5 minutes.</summary>
    public static TimeSpan DefaultTolerance { get; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Checks that a webhook comes from Flashpi and is recent, and returns its body as a
    /// <see cref="WebhookEvent"/>: match it on its type (<see cref="WebhookEndpointVerificationEvent"/>,
    /// <see cref="DocumentEvent"/>, ...). Throws <see cref="WebhookVerificationException"/> otherwise:
    /// answer 400 and ignore the request.
    /// </summary>
    /// <param name="payload">The raw body, exactly as received (not parsed and serialized again).</param>
    /// <param name="headers">The request headers, such as ASP.NET Core's <c>Request.Headers</c>.</param>
    /// <param name="secret">The endpoint's signing secret (<c>whsec_...</c>).</param>
    /// <param name="tolerance">The largest gap between <c>webhook-timestamp</c> and now (default 5 minutes).</param>
    /// <param name="now">The current time, for tests (default: the clock).</param>
    public static WebhookEvent Unwrap<TValues>(
        string payload, IEnumerable<KeyValuePair<string, TValues>> headers, string secret, TimeSpan? tolerance = null, DateTimeOffset? now = null)
        where TValues : IEnumerable<string?> =>
        UnwrapCore(TextBody(payload), payload, Lookup(headers), secret, tolerance, now);

    /// <inheritdoc cref="Unwrap{TValues}(string, IEnumerable{KeyValuePair{string, TValues}}, string, TimeSpan?, DateTimeOffset?)"/>
    public static WebhookEvent Unwrap<TValues>(
        byte[] payload, IEnumerable<KeyValuePair<string, TValues>> headers, string secret, TimeSpan? tolerance = null, DateTimeOffset? now = null)
        where TValues : IEnumerable<string?> =>
        UnwrapCore(BytesBody(payload), null, Lookup(headers), secret, tolerance, now);

    /// <inheritdoc cref="Unwrap{TValues}(string, IEnumerable{KeyValuePair{string, TValues}}, string, TimeSpan?, DateTimeOffset?)"/>
    public static WebhookEvent Unwrap(
        string payload, IEnumerable<KeyValuePair<string, string>> headers, string secret, TimeSpan? tolerance = null, DateTimeOffset? now = null) =>
        UnwrapCore(TextBody(payload), payload, Lookup(headers), secret, tolerance, now);

    /// <inheritdoc cref="Unwrap{TValues}(string, IEnumerable{KeyValuePair{string, TValues}}, string, TimeSpan?, DateTimeOffset?)"/>
    public static WebhookEvent Unwrap(
        byte[] payload, IEnumerable<KeyValuePair<string, string>> headers, string secret, TimeSpan? tolerance = null, DateTimeOffset? now = null) =>
        UnwrapCore(BytesBody(payload), null, Lookup(headers), secret, tolerance, now);

    /// <inheritdoc cref="Unwrap{TValues}(string, IEnumerable{KeyValuePair{string, TValues}}, string, TimeSpan?, DateTimeOffset?)"/>
    public static WebhookEvent Unwrap(
        string payload, NameValueCollection headers, string secret, TimeSpan? tolerance = null, DateTimeOffset? now = null) =>
        UnwrapCore(TextBody(payload), payload, Lookup(headers), secret, tolerance, now);

    /// <inheritdoc cref="Unwrap{TValues}(string, IEnumerable{KeyValuePair{string, TValues}}, string, TimeSpan?, DateTimeOffset?)"/>
    public static WebhookEvent Unwrap(
        byte[] payload, NameValueCollection headers, string secret, TimeSpan? tolerance = null, DateTimeOffset? now = null) =>
        UnwrapCore(BytesBody(payload), null, Lookup(headers), secret, tolerance, now);

    /// <summary>
    /// Checks that a webhook comes from Flashpi and is recent. Throws
    /// <see cref="WebhookVerificationException"/> otherwise: answer 400 and ignore the request.
    /// </summary>
    /// <param name="payload">The raw body, exactly as received (not parsed and serialized again).</param>
    /// <param name="headers">The request headers, such as ASP.NET Core's <c>Request.Headers</c>.</param>
    /// <param name="secret">The endpoint's signing secret (<c>whsec_...</c>).</param>
    /// <param name="tolerance">The largest gap between <c>webhook-timestamp</c> and now (default 5 minutes).</param>
    /// <param name="now">The current time, for tests (default: the clock).</param>
    public static void VerifySignature<TValues>(
        string payload, IEnumerable<KeyValuePair<string, TValues>> headers, string secret, TimeSpan? tolerance = null, DateTimeOffset? now = null)
        where TValues : IEnumerable<string?> =>
        Verify(TextBody(payload), Lookup(headers), secret, tolerance, now);

    /// <inheritdoc cref="VerifySignature{TValues}(string, IEnumerable{KeyValuePair{string, TValues}}, string, TimeSpan?, DateTimeOffset?)"/>
    public static void VerifySignature<TValues>(
        byte[] payload, IEnumerable<KeyValuePair<string, TValues>> headers, string secret, TimeSpan? tolerance = null, DateTimeOffset? now = null)
        where TValues : IEnumerable<string?> =>
        Verify(BytesBody(payload), Lookup(headers), secret, tolerance, now);

    /// <inheritdoc cref="VerifySignature{TValues}(string, IEnumerable{KeyValuePair{string, TValues}}, string, TimeSpan?, DateTimeOffset?)"/>
    public static void VerifySignature(
        string payload, IEnumerable<KeyValuePair<string, string>> headers, string secret, TimeSpan? tolerance = null, DateTimeOffset? now = null) =>
        Verify(TextBody(payload), Lookup(headers), secret, tolerance, now);

    /// <inheritdoc cref="VerifySignature{TValues}(string, IEnumerable{KeyValuePair{string, TValues}}, string, TimeSpan?, DateTimeOffset?)"/>
    public static void VerifySignature(
        byte[] payload, IEnumerable<KeyValuePair<string, string>> headers, string secret, TimeSpan? tolerance = null, DateTimeOffset? now = null) =>
        Verify(BytesBody(payload), Lookup(headers), secret, tolerance, now);

    /// <inheritdoc cref="VerifySignature{TValues}(string, IEnumerable{KeyValuePair{string, TValues}}, string, TimeSpan?, DateTimeOffset?)"/>
    public static void VerifySignature(
        string payload, NameValueCollection headers, string secret, TimeSpan? tolerance = null, DateTimeOffset? now = null) =>
        Verify(TextBody(payload), Lookup(headers), secret, tolerance, now);

    /// <inheritdoc cref="VerifySignature{TValues}(string, IEnumerable{KeyValuePair{string, TValues}}, string, TimeSpan?, DateTimeOffset?)"/>
    public static void VerifySignature(
        byte[] payload, NameValueCollection headers, string secret, TimeSpan? tolerance = null, DateTimeOffset? now = null) =>
        Verify(BytesBody(payload), Lookup(headers), secret, tolerance, now);

    // The body is read lazily, so a bad secret or stale headers are reported first, as in the
    // other SDKs.
    private static Func<byte[]> TextBody(string payload)
    {
        Checked.NotNull(payload, nameof(payload));
        return () =>
        {
            try
            {
                return StrictUtf8.GetBytes(payload);
            }
            catch (EncoderFallbackException error)
            {
                throw new WebhookVerificationException(WebhookVerificationReason.InvalidPayload, "The webhook body is not valid text", error);
            }
        };
    }

    private static Func<byte[]> BytesBody(byte[] payload)
    {
        Checked.NotNull(payload, nameof(payload));
        return () => payload;
    }

    private static WebhookEvent UnwrapCore(
        Func<byte[]> body, string? text, Func<string, List<string>> headers, string secret, TimeSpan? tolerance, DateTimeOffset? now)
    {
        var bytes = Verify(body, headers, secret, tolerance, now);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text ?? Text.Decode(bytes));
        }
        catch (JsonException error)
        {
            throw new WebhookVerificationException(WebhookVerificationReason.InvalidPayload, "The webhook body is not JSON", error);
        }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new WebhookVerificationException(WebhookVerificationReason.InvalidPayload, "The webhook body is not a JSON object");
            }
            try
            {
                return document.RootElement.Deserialize(Json.TypeInfo<WebhookEvent>())
                    ?? throw new JsonException("null");
            }
            catch (JsonException error)
            {
                throw new WebhookVerificationException(
                    WebhookVerificationReason.InvalidPayload, "The webhook body is not an event this version of the SDK can read", error);
            }
        }
    }

    /// <summary>The checks; returns the body's bytes once the signature matches.</summary>
    private static byte[] Verify(Func<byte[]> body, Func<string, List<string>> headers, string secret, TimeSpan? tolerance, DateTimeOffset? now)
    {
        var allowed = tolerance ?? DefaultTolerance;
        if (allowed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), "The tolerance must be zero or more");
        }
        var current = now ?? DateTimeOffset.UtcNow;
        var key = SecretKey(secret);
        try
        {
            var webhookId = First(headers("webhook-id"));
            var timestamp = First(headers("webhook-timestamp"));
            var signatures = string.Join(" ", headers("webhook-signature"));
            if (string.IsNullOrEmpty(webhookId) || string.IsNullOrEmpty(timestamp) || signatures.Length == 0)
            {
                throw new WebhookVerificationException(
                    WebhookVerificationReason.MissingHeaders,
                    "The webhook-id, webhook-timestamp and webhook-signature headers are required");
            }
            if (!IsTimestamp(timestamp!))
            {
                throw new WebhookVerificationException(WebhookVerificationReason.InvalidTimestamp, "The webhook-timestamp is not valid");
            }
            var seconds = long.Parse(timestamp!, NumberStyles.None, CultureInfo.InvariantCulture);
            var gap = Math.Abs(current.ToUnixTimeSeconds() - seconds);
            if (gap > allowed.TotalSeconds)
            {
                throw new WebhookVerificationException(
                    WebhookVerificationReason.TimestampOutOfRange,
                    string.Format(CultureInfo.InvariantCulture, "The webhook-timestamp is more than {0} seconds away from now", allowed.TotalSeconds));
            }
            var bytes = body();
            byte[] prefix;
            try
            {
                prefix = StrictUtf8.GetBytes($"{webhookId}.{timestamp}.");
            }
            catch (EncoderFallbackException)
            {
                // A webhook-id that is not text was not signed by Flashpi.
                throw NoMatch();
            }
            if (!Signed(key, prefix, bytes, signatures))
            {
                throw NoMatch();
            }
            return bytes;
        }
        finally
        {
            Array.Clear(key, 0, key.Length);
        }
    }

    private static WebhookVerificationException NoMatch() =>
        new(WebhookVerificationReason.NoMatchingSignature, "No webhook-signature matches the body and the secret");

    private static string? First(List<string> values) => values.Count > 0 ? values[0] : null;

    private static bool IsTimestamp(string value)
    {
        if (value.Length is 0 or > 12)
        {
            return false;
        }
        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Whether a v1 signature of the header signs the message (one HMAC, however many signatures).</summary>
    private static bool Signed(byte[] key, byte[] prefix, byte[] body, string signatures)
    {
        byte[] expected;
        using (var hmac = new HMACSHA256(key))
        {
            hmac.TransformBlock(prefix, 0, prefix.Length, null, 0);
            hmac.TransformFinalBlock(body, 0, body.Length);
            expected = hmac.Hash!;
        }
        try
        {
            foreach (var entry in signatures.Split(' '))
            {
                var comma = entry.IndexOf(',');
                if (comma < 0 || entry.Substring(0, comma) != "v1")
                {
                    continue;
                }
                var signature = DecodeBase64(entry.Substring(comma + 1));
                if (signature is { Length: SignatureBytes } && FixedTimeEquals(signature, expected))
                {
                    return true;
                }
            }
            return false;
        }
        finally
        {
            Array.Clear(expected, 0, expected.Length);
        }
    }

    private static bool FixedTimeEquals(byte[] left, byte[] right)
    {
#if NET
        return CryptographicOperations.FixedTimeEquals(left, right);
#else
        if (left.Length != right.Length)
        {
            return false;
        }
        var difference = 0;
        for (var index = 0; index < left.Length; index++)
        {
            difference |= left[index] ^ right[index];
        }
        return difference == 0;
#endif
    }

    /// <summary>The HMAC key of a <c>whsec_</c> secret. A refused key is cleared before throwing.</summary>
    private static byte[] SecretKey(string secret)
    {
        if (secret is null || secret.Trim().Length == 0)
        {
            throw new WebhookVerificationException(
                WebhookVerificationReason.InvalidSecret,
                "The webhook secret is empty: copy it from the console or WebhookEndpoints.GetSecretAsync()");
        }
        var value = secret.Trim();
        if (value.StartsWith(SecretPrefix, StringComparison.Ordinal))
        {
            value = value.Substring(SecretPrefix.Length);
        }
        var key = DecodeBase64(value);
        var size = key?.Length ?? 0;
        if (key is not null && size is >= MinSecretBytes and <= MaxSecretBytes)
        {
            return key;
        }
        if (key is not null)
        {
            Array.Clear(key, 0, key.Length);
        }
        if (size == 0)
        {
            throw new WebhookVerificationException(
                WebhookVerificationReason.InvalidSecret, "The webhook secret is not a whsec_ secret (base64 after the prefix)");
        }
        throw new WebhookVerificationException(
            WebhookVerificationReason.InvalidSecret,
            "The webhook secret is not a complete whsec_ secret: copy it again from the console or WebhookEndpoints.GetSecretAsync()");
    }

    /// <summary>Standard base64 to bytes, or null when it is not base64 (padding optional, as atob).</summary>
    private static byte[]? DecodeBase64(string value)
    {
        var padding = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var alphabet = character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '+' or '/';
            if (character == '=')
            {
                padding++;
            }
            else if (!alphabet || padding > 0)
            {
                return null;
            }
        }
        // Padding only completes a length that is a multiple of 4.
        if (padding > 2 || value.Length % 4 == 1 || (padding > 0 && value.Length % 4 != 0))
        {
            return null;
        }
        var data = value.TrimEnd('=');
        if (data.Length % 4 == 1)
        {
            return null;
        }
        var padded = data + new string('=', (4 - (data.Length % 4)) % 4);
        try
        {
            return Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // Headers in any letter case. Repeated headers: every value, in order.

    private static Func<string, List<string>> Lookup<TValues>(IEnumerable<KeyValuePair<string, TValues>> headers)
        where TValues : IEnumerable<string?>
    {
        Checked.NotNull(headers, nameof(headers));
        return name =>
        {
            var values = new List<string>();
            foreach (var header in headers)
            {
                if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase) && header.Value is not null)
                {
                    foreach (var value in header.Value)
                    {
                        if (value is not null)
                        {
                            values.Add(value);
                        }
                    }
                }
            }
            return values;
        };
    }

    private static Func<string, List<string>> Lookup(IEnumerable<KeyValuePair<string, string>> headers)
    {
        Checked.NotNull(headers, nameof(headers));
        return name =>
        {
            var values = new List<string>();
            foreach (var header in headers)
            {
                if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase) && header.Value is not null)
                {
                    values.Add(header.Value);
                }
            }
            return values;
        };
    }

    private static Func<string, List<string>> Lookup(NameValueCollection headers)
    {
        Checked.NotNull(headers, nameof(headers));
        return name =>
        {
            // Get joins repeated values with commas again, which ASP.NET may have split
            // ("v1,<signature>" holds one).
            var values = new List<string>();
            foreach (var key in headers.AllKeys)
            {
                if (key is not null && string.Equals(key, name, StringComparison.OrdinalIgnoreCase) && headers.Get(key) is { } value)
                {
                    values.Add(value);
                }
            }
            return values;
        };
    }
}
