using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Flashpi.Models;
using Xunit;

namespace Flashpi.Tests;

public sealed class WebhooksTests
{
    private sealed record Vector(string Secret, string Id, string Timestamp, string Body, string Signature)
    {
        public DateTimeOffset At(int offset = 0) =>
            DateTimeOffset.FromUnixTimeSeconds(long.Parse(Timestamp, CultureInfo.InvariantCulture) + offset);

        public Dictionary<string, string> Headers(string? signature = null) => new()
        {
            ["webhook-id"] = Id,
            ["webhook-timestamp"] = Timestamp,
            ["webhook-signature"] = signature ?? Signature,
        };
    }

    // The example of the Standard Webhooks specification (24-byte key).
    private static readonly Vector Standard = new(
        "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw",
        "msg_p5jXN8AQM9LWM0D4loKWxJek",
        "1614265330",
        "{\"test\": 2432232314}",
        "v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=");

    // The example of Flashpi's webhook docs (32-byte key), the same the API's tests sign.
    private static readonly Vector Flashpi = new(
        "whsec_Zmxhc2hwaS1kb2NzLWV4YW1wbGUtc2VjcmV0LTMyLWI=",
        "019a1b2c-3d4e-7f60-8a1b-2c3d4e5f6a7b",
        "1792152000",
        "{\"id\":\"019a1b2c-3d4e-7f60-8a1b-2c3d4e5f6a7b\",\"type\":\"invoice.authorized\",\"environment\":\"test\",\"createdAt\":\"2026-10-16T17:20:00.000Z\"}",
        "v1,HbWOpDPWCo3wtq41t9GVHUpg69M1zgvveoDJmSWkA8U=");

    private const string Decoy = "v1,Ceo5qEr07ixe2NLpvHk3FH9bwy/WavXrAFQ/9tdO6mc=";

    private static string Reason(Action check)
    {
        try
        {
            check();
        }
        catch (WebhookVerificationException error)
        {
            return error.Reason.ToString();
        }
        return "Verified";
    }

    private static string Sign(byte[] key, Vector vector, byte[] body)
    {
        using var hmac = new HMACSHA256(key);
        var signed = new List<byte>(Encoding.UTF8.GetBytes($"{vector.Id}.{vector.Timestamp}."));
        signed.AddRange(body);
        return "v1," + Convert.ToBase64String(hmac.ComputeHash(signed.ToArray()));
    }

    private static byte[] KeyOf(Vector vector) => Convert.FromBase64String(vector.Secret.Substring("whsec_".Length));

    [Fact]
    public void Accepts_the_standard_webhooks_example()
    {
        Assert.Equal("Verified", Reason(() => Webhooks.VerifySignature(Standard.Body, Standard.Headers(), Standard.Secret, now: Standard.At())));
        Assert.Equal("Verified", Reason(() => Webhooks.VerifySignature(Flashpi.Body, Flashpi.Headers(), Flashpi.Secret, now: Flashpi.At())));
    }

    [Fact]
    public void Signs_the_raw_bytes_of_a_body_given_as_text_or_bytes()
    {
        Webhooks.VerifySignature(Encoding.UTF8.GetBytes(Standard.Body), Standard.Headers(), Standard.Secret, now: Standard.At());
        var body = Encoding.UTF8.GetBytes("{\"nombre\":\"Año\"}");
        var signature = Sign(KeyOf(Flashpi), Flashpi, body);
        Webhooks.VerifySignature("{\"nombre\":\"Año\"}", Flashpi.Headers(signature), Flashpi.Secret, now: Flashpi.At());
        var anyBytes = new byte[] { 0xff, 0xfe, 0x00 };
        Webhooks.VerifySignature(anyBytes, Flashpi.Headers(Sign(KeyOf(Flashpi), Flashpi, anyBytes)), Flashpi.Secret, now: Flashpi.At());
    }

    [Fact]
    public void Accepts_either_secret_during_a_rotation_and_skips_decoys_and_other_versions()
    {
        var headers = Standard.Headers($"{Decoy} v2,abc v1 v1,!!! {Standard.Signature}");
        Webhooks.VerifySignature(Standard.Body, headers, Standard.Secret, now: Standard.At());
    }

    [Fact]
    public void Accepts_the_secret_without_its_prefix_and_with_spaces_around_it()
    {
        Webhooks.VerifySignature(Standard.Body, Standard.Headers(), "MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw", now: Standard.At());
        Webhooks.VerifySignature(Standard.Body, Standard.Headers(), $"  {Standard.Secret}\n", now: Standard.At());
    }

    [Fact]
    public void Accepts_a_signature_and_a_secret_without_padding_like_atob()
    {
        Webhooks.VerifySignature(Standard.Body, Standard.Headers(Standard.Signature.TrimEnd('=')), Standard.Secret, now: Standard.At());
        Webhooks.VerifySignature(Flashpi.Body, Flashpi.Headers(), Flashpi.Secret.TrimEnd('='), now: Flashpi.At());
    }

    [Theory]
    [InlineData("v1,AAAA")]
    [InlineData("v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=X")]
    [InlineData("v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE==")]
    [InlineData("v1,g0hM9SsE-OTPJTGt_tmIKtSyZlE3uFJELVlNIOLJ1OE=")]
    [InlineData("v1, g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=")]
    [InlineData("v2,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=")]
    public void Rejects_bad_signatures(string signature) =>
        Assert.Equal("NoMatchingSignature", Reason(() => Webhooks.VerifySignature(Standard.Body, Standard.Headers(signature), Standard.Secret, now: Standard.At())));

    [Fact]
    public void Rejects_a_body_signed_with_another_secret_or_altered()
    {
        Assert.Equal("NoMatchingSignature", Reason(() => Webhooks.VerifySignature(Standard.Body, Standard.Headers(), Flashpi.Secret, now: Standard.At())));
        Assert.Equal("NoMatchingSignature", Reason(() => Webhooks.VerifySignature(Standard.Body + " ", Standard.Headers(), Standard.Secret, now: Standard.At())));
    }

    [Fact]
    public void Accepts_timestamps_up_to_5_minutes_away_and_rejects_others()
    {
        Assert.Equal("Verified", Reason(() => Webhooks.VerifySignature(Standard.Body, Standard.Headers(), Standard.Secret, now: Standard.At(300))));
        Assert.Equal("Verified", Reason(() => Webhooks.VerifySignature(Standard.Body, Standard.Headers(), Standard.Secret, now: Standard.At(-300))));
        Assert.Equal("TimestampOutOfRange", Reason(() => Webhooks.VerifySignature(Standard.Body, Standard.Headers(), Standard.Secret, now: Standard.At(301))));
        Assert.Equal("TimestampOutOfRange", Reason(() => Webhooks.VerifySignature(Standard.Body, Standard.Headers(), Standard.Secret, now: Standard.At(-301))));
        Assert.Equal("TimestampOutOfRange", Reason(() => Webhooks.VerifySignature(Standard.Body, Standard.Headers(), Standard.Secret)));
    }

    [Fact]
    public void Takes_another_tolerance()
    {
        Assert.Equal("Verified", Reason(() => Webhooks.VerifySignature(Standard.Body, Standard.Headers(), Standard.Secret, TimeSpan.FromHours(1), Standard.At(3600))));
        Assert.Equal("TimestampOutOfRange", Reason(() => Webhooks.VerifySignature(Standard.Body, Standard.Headers(), Standard.Secret, TimeSpan.Zero, Standard.At(1))));
        Assert.Throws<ArgumentOutOfRangeException>(() => Webhooks.VerifySignature(Standard.Body, Standard.Headers(), Standard.Secret, TimeSpan.FromSeconds(-1), Standard.At()));
    }

    [Theory]
    [InlineData("webhook-id")]
    [InlineData("webhook-timestamp")]
    [InlineData("webhook-signature")]
    public void Rejects_a_request_without_a_header(string missing)
    {
        var headers = Standard.Headers();
        headers.Remove(missing);
        Assert.Equal("MissingHeaders", Reason(() => Webhooks.VerifySignature(Standard.Body, headers, Standard.Secret, now: Standard.At())));
        headers[missing] = "";
        Assert.Equal("MissingHeaders", Reason(() => Webhooks.VerifySignature(Standard.Body, headers, Standard.Secret, now: Standard.At())));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1614265330.5")]
    [InlineData("-1614265330")]
    [InlineData("1234567890123")]
    [InlineData("1614265330\n")]
    public void Rejects_an_invalid_timestamp(string timestamp)
    {
        var headers = Standard.Headers();
        headers["webhook-timestamp"] = timestamp;
        Assert.Equal("InvalidTimestamp", Reason(() => Webhooks.VerifySignature(Standard.Body, headers, Standard.Secret, now: Standard.At())));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("whsec_")]
    [InlineData("whsec_not base64!")]
    [InlineData("whsec_c2hvcnQ=")]
    [InlineData("whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLa")]
    public void Refuses_an_invalid_secret_before_a_stale_timestamp(string secret) =>
        Assert.Equal("InvalidSecret", Reason(() => Webhooks.VerifySignature(Standard.Body, Standard.Headers(), secret)));

    [Fact]
    public void Accepts_a_64_byte_key_and_refuses_65()
    {
        var key = new byte[64];
        var signature = Sign(key, Standard, Encoding.UTF8.GetBytes(Standard.Body));
        Webhooks.VerifySignature(Standard.Body, Standard.Headers(signature), "whsec_" + Convert.ToBase64String(key), now: Standard.At());
        Assert.Equal("InvalidSecret", Reason(() => Webhooks.VerifySignature(Standard.Body, Standard.Headers(), "whsec_" + Convert.ToBase64String(new byte[65]), now: Standard.At())));
    }

    [Fact]
    public void Refuses_a_text_body_that_cannot_be_encoded()
    {
        var body = "{\"a\":\"\ud800\"}";
        Assert.Equal("InvalidPayload", Reason(() => Webhooks.VerifySignature(body, Standard.Headers(), Standard.Secret, now: Standard.At())));
    }

    [Fact]
    public void Rejects_a_webhook_id_that_is_not_text()
    {
        var headers = Standard.Headers();
        headers["webhook-id"] = "msg_\udc80";
        Assert.Equal("NoMatchingSignature", Reason(() => Webhooks.VerifySignature(Standard.Body, headers, Standard.Secret, now: Standard.At())));
    }

    [Fact]
    public void Reads_headers_in_any_letter_case_and_joins_repeated_signatures()
    {
        var multi = new Dictionary<string, string[]>
        {
            ["Webhook-Id"] = [Standard.Id],
            ["WEBHOOK-TIMESTAMP"] = [Standard.Timestamp],
            ["webhook-signature"] = [Decoy, Standard.Signature],
        };
        Webhooks.VerifySignature(Standard.Body, multi, Standard.Secret, now: Standard.At());

        Assert.Equal("NoMatchingSignature", Reason(() => Webhooks.VerifySignature(Standard.Body, new NameValueCollection { { "Webhook-Id", Standard.Id }, { "Webhook-Timestamp", Standard.Timestamp }, { "Webhook-Signature", Decoy } }, Standard.Secret, now: Standard.At())));

        using var request = new HttpRequestMessage();
        request.Headers.TryAddWithoutValidation("webhook-id", Standard.Id);
        request.Headers.TryAddWithoutValidation("webhook-timestamp", Standard.Timestamp);
        request.Headers.TryAddWithoutValidation("webhook-signature", Standard.Signature);
        Webhooks.VerifySignature(Standard.Body, request.Headers, Standard.Secret, now: Standard.At());
    }

    [Fact]
    public void Reads_a_name_value_collection_whose_signature_holds_commas()
    {
        var headers = new NameValueCollection
        {
            { "Webhook-Id", Standard.Id },
            { "Webhook-Timestamp", Standard.Timestamp },
            { "Webhook-Signature", $"{Decoy} {Standard.Signature}" },
        };
        Webhooks.VerifySignature(Standard.Body, headers, Standard.Secret, now: Standard.At());
        Webhooks.Unwrap(Encoding.UTF8.GetBytes(Standard.Body), headers, Standard.Secret, now: Standard.At());
    }

    [Fact]
    public void Returns_the_verified_event()
    {
        var webhook = Webhooks.Unwrap(Flashpi.Body, Flashpi.Headers(), Flashpi.Secret, now: Flashpi.At());
        var thin = Assert.IsType<EventThin>(webhook);
        Assert.Equal(EventType.InvoiceAuthorized, thin.Type);
        Assert.Equal(Flashpi.Id, thin.Id);
        Assert.Equal(FlashpiEnvironment.Test, thin.Environment);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 17, 20, 0, TimeSpan.Zero), thin.CreatedAt);
    }

    [Fact]
    public void Returns_each_kind_of_webhook_as_its_type()
    {
        var verification = Unwrap("{\"id\":\"v1\",\"type\":\"webhook_endpoint.verification\",\"environment\":\"live\",\"createdAt\":\"2026-11-13T12:00:00Z\",\"data\":{\"webhookEndpointId\":\"w1\",\"challenge\":\"ch_123\"}}");
        Assert.Equal("ch_123", Assert.IsType<WebhookEndpointVerificationEvent>(verification).Data.Challenge);
        Assert.Equal(EventType.WebhookEndpointVerification, verification.Type);

        var document = Unwrap("{\"id\":\"e1\",\"type\":\"invoice.rejected\",\"environment\":\"test\",\"createdAt\":\"2026-11-13T12:00:00Z\",\"data\":{\"id\":\"d1\",\"status\":\"rejected\",\"sriMessages\":[]}}");
        var rejected = Assert.IsType<DocumentEvent>(document);
        Assert.Equal(DocumentStatus.Rejected, rejected.Data.Status);

        var unknown = Unwrap("{\"id\":\"e2\",\"type\":\"refund.created\",\"environment\":\"test\",\"createdAt\":\"2026-11-13T12:00:00Z\",\"data\":{\"x\":1}}");
        Assert.IsType<UnknownEvent>(unknown);

        var message = rejected switch
        {
            DocumentEvent { Type.Value: "invoice.rejected" } item => item.Data.Id,
            _ => "other",
        };
        Assert.Equal("d1", message);
    }

    [Fact]
    public void Raises_before_parsing_when_the_signature_is_wrong_and_invalid_payload_for_a_body_that_is_not_an_object()
    {
        Assert.Equal("NoMatchingSignature", Reason(() => Webhooks.Unwrap("not json", Flashpi.Headers(), Flashpi.Secret, now: Flashpi.At())));
        foreach (var body in new[] { "not json", "[1,2]", "42", "{\"id\":\"e\",\"type\":\"invoice.authorized\",\"environment\":\"test\",\"createdAt\":\"yesterday\"}" })
        {
            var signature = Sign(KeyOf(Flashpi), Flashpi, Encoding.UTF8.GetBytes(body));
            Assert.Equal("InvalidPayload", Reason(() => Webhooks.Unwrap(body, Flashpi.Headers(signature), Flashpi.Secret, now: Flashpi.At())));
        }
    }

    [Fact]
    public void Refuses_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => Webhooks.VerifySignature((string)null!, Standard.Headers(), Standard.Secret));
        Assert.Throws<ArgumentNullException>(() => Webhooks.VerifySignature(Standard.Body, (Dictionary<string, string>)null!, Standard.Secret));
        Assert.Equal("InvalidSecret", Reason(() => Webhooks.VerifySignature(Standard.Body, Standard.Headers(), null!)));
    }

    private static WebhookEvent Unwrap(string body)
    {
        var signature = Sign(KeyOf(Flashpi), Flashpi, Encoding.UTF8.GetBytes(body));
        return Webhooks.Unwrap(body, Flashpi.Headers(signature), Flashpi.Secret, now: Flashpi.At());
    }
}
