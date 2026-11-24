using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Flashpi.Internal;
using Flashpi.Models;
using Xunit;

namespace Flashpi.Tests;

public sealed class ModelsTests
{
    [Fact]
    public void Reads_an_answer_with_its_api_names()
    {
        var json = """
            {"id":"d1","type":"invoice","environment":"live","status":"pending_sri","accessKey":"123",
             "sequential":1501,"createdAt":"2026-11-13T12:00:00.123Z","somethingNew":{"a":1},
             "ride":{"status":"generated","template":"modern"}}
            """;
        var document = JsonSerializer.Deserialize(json, Json.TypeInfo<Document>())!;
        Assert.Equal("d1", document.Id);
        Assert.Equal(DocumentType.Invoice, document.Type);
        Assert.Equal(FlashpiEnvironment.Live, document.Environment);
        Assert.Equal(DocumentStatus.PendingSri, document.Status);
        Assert.Equal(1501, document.Sequential);
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 12, 0, 0, 123, TimeSpan.Zero), document.CreatedAt);
        Assert.Equal(RideStatus.Generated, document.Ride!.Status);
    }

    [Fact]
    public void Reads_members_the_contract_requires_leniently_when_they_are_missing()
    {
        var document = JsonSerializer.Deserialize("{\"id\":\"d1\"}", Json.TypeInfo<Document>())!;
        Assert.Equal("d1", document.Id);
        Assert.Null(document.AccessKey);
        Assert.Equal("", document.Status.Value);
        var nulls = JsonSerializer.Deserialize("{\"id\":\"d1\",\"status\":null}", Json.TypeInfo<Document>())!;
        Assert.Equal(default, nulls.Status);
    }

    [Fact]
    public void Reads_the_warnings_of_a_certificate()
    {
        var json = """
            {"id":"c1","status":"accepted","usable":true,"ruc":"1790000000001",
             "warnings":[{"code":"ruc_mismatch","message":"Test documents only"},{"code":"brand_new","message":"New"}]}
            """;
        var certificate = JsonSerializer.Deserialize(json, Json.TypeInfo<Certificate>())!;
        Assert.Equal(CertificateStatus.Accepted, certificate.Status);
        Assert.Equal(2, certificate.Warnings.Count);
        Assert.Equal(CertificateWarningCode.RucMismatch, certificate.Warnings[0].Code);
        Assert.Equal("Test documents only", certificate.Warnings[0].Message);
        Assert.Equal("brand_new", certificate.Warnings[1].Code.Value);
        Assert.Empty(JsonSerializer.Deserialize("{\"id\":\"c1\",\"warnings\":[]}", Json.TypeInfo<Certificate>())!.Warnings);
        // An answer without them, or with null, reads as no warnings.
        Assert.Empty(JsonSerializer.Deserialize("{\"id\":\"c1\"}", Json.TypeInfo<Certificate>())!.Warnings);
        Assert.Empty(JsonSerializer.Deserialize("{\"id\":\"c1\",\"warnings\":null}", Json.TypeInfo<Certificate>())!.Warnings);
    }

    [Fact]
    public void Creates_a_certificate_without_its_warnings_as_code_written_before_them_does()
    {
        // Warnings came with the contract of 2026-11-24: setting them is not required, so this
        // still compiles, and the certificate has none.
        var certificate = new Certificate
        {
            Id = Samples.Id,
            IssuerId = Samples.OtherId,
            Environment = FlashpiEnvironment.Test,
            AllModes = false,
            Status = CertificateStatus.Accepted,
            Usable = true,
            Subject = null,
            IssuedBy = null,
            SerialNumber = null,
            ValidFrom = null,
            ValidUntil = null,
            Sha256Fingerprint = null,
            Ruc = null,
            Revocation = null,
            Rejection = null,
            CreatedAt = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero),
        };
        Assert.Empty(certificate.Warnings);
        Assert.Empty((certificate with { Warnings = null! }).Warnings);
        Assert.Equal(certificate, certificate with { });
        Assert.Contains("\"warnings\":[]", JsonSerializer.Serialize(certificate, Json.TypeInfo<Certificate>()));
        var warned = certificate with { Warnings = [new CertificateWarning { Code = CertificateWarningCode.RucMismatch, Message = "x" }] };
        Assert.Contains("\"warnings\":[{\"code\":\"ruc_mismatch\",\"message\":\"x\"}]", JsonSerializer.Serialize(warned, Json.TypeInfo<Certificate>()));
    }

    [Fact]
    public void Reads_the_failure_codes_of_the_contract_of_2026_11_24()
    {
        foreach (var (code, expected) in new[]
        {
            ("certificate_ruc_mismatch", DocumentFailureCode.CertificateRucMismatch),
            ("sri_authorization_mismatch", DocumentFailureCode.SriAuthorizationMismatch),
        })
        {
            var json = $"{{\"id\":\"d1\",\"status\":\"failed\",\"failure\":{{\"code\":\"{code}\",\"message\":\"m\"}}}}";
            var document = JsonSerializer.Deserialize(json, Json.TypeInfo<Document>())!;
            Assert.Equal(expected, document.Failure!.Code);
        }
    }

    [Fact]
    public void Keeps_enum_values_this_version_does_not_know()
    {
        var document = JsonSerializer.Deserialize("{\"id\":\"d1\",\"status\":\"archived\"}", Json.TypeInfo<Document>())!;
        Assert.Equal("archived", document.Status.Value);
        Assert.NotEqual(DocumentStatus.Authorized, document.Status);
        Assert.Equal(new DocumentStatus("archived"), document.Status);
        Assert.Equal("archived", document.Status.ToString());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{\"id\":\"d1\",\"status\":1}", Json.TypeInfo<Document>()));
    }

    [Fact]
    public void Compares_enums_by_value_and_converts_text()
    {
        DocumentStatus status = "authorized";
        Assert.True(status == DocumentStatus.Authorized);
        Assert.False(status != DocumentStatus.Authorized);
        Assert.True(status.Equals((object)DocumentStatus.Authorized));
        Assert.True(status.Equals("authorized"));
        Assert.False(status.Equals((object)"authorized"));
        Assert.Equal(DocumentStatus.Authorized.GetHashCode(), status.GetHashCode());
        Assert.Equal("", default(DocumentStatus).Value);
        Assert.Throws<ArgumentNullException>(() => new DocumentStatus(null!));
        Assert.Equal("es", Language.Spanish.Value);
        Assert.Equal(Language.Spanish, Language.FromString("es"));
        Assert.True(Language.English != Language.Spanish);
        Assert.Equal("en", Language.English.ToString());
        Assert.False(Language.English.Equals((object)"en"));
        Assert.Equal(Language.English.GetHashCode(), new Language("en").GetHashCode());
    }

    [Fact]
    public void Writes_a_request_without_its_unset_members()
    {
        var body = new CreateEstablishment { Code = "001", Address = "Quito" };
        Assert.Equal("{\"code\":\"001\",\"address\":\"Quito\"}", Encoding.UTF8.GetString(Json.Serialize(body)));
    }

    [Fact]
    public void Writes_a_last_payment_without_its_amount()
    {
        // Flashpi gives the last payment what the others leave of the total.
        var invoice = Samples.Invoice() with
        {
            Payments =
            [
                new DocumentPayment { Method = PaymentMethod.CreditCard, Amount = "1.00" },
                new DocumentPayment { Method = PaymentMethod.Cash },
            ],
        };
        Assert.Contains(
            "\"payments\":[{\"method\":\"credit_card\",\"amount\":\"1.00\"},{\"method\":\"cash\"}]",
            Encoding.UTF8.GetString(Json.Serialize(invoice)));
        var payment = new WithholdingPayment { Method = PaymentMethod.OtherFinancialSystem };
        Assert.Equal("{\"method\":\"other_financial_system\"}", Encoding.UTF8.GetString(Json.Serialize(payment)));
    }

    [Fact]
    public void Sends_null_for_an_optional_nullable_member_only_when_it_was_set()
    {
        Assert.Equal("{}", Encoding.UTF8.GetString(Json.Serialize(new UpdateIssuer())));
        Assert.Equal("{\"tradeName\":null}", Encoding.UTF8.GetString(Json.Serialize(new UpdateIssuer { TradeName = null })));
        Assert.Equal("{\"tradeName\":\"X\"}", Encoding.UTF8.GetString(Json.Serialize(new UpdateIssuer { TradeName = "X" })));
        var patch = new UpdateIssuer { TaxProfile = new IssuerTaxProfilePatch { RimpeRegime = null, KeepsAccounting = true } };
        Assert.Equal("{\"taxProfile\":{\"keepsAccounting\":true,\"rimpeRegime\":null}}", Encoding.UTF8.GetString(Json.Serialize(patch)));
        var copy = patch with { };
        Assert.Equal(patch, copy);
        Assert.NotEqual(new UpdateIssuer(), new UpdateIssuer { TradeName = null });
    }

    [Fact]
    public void Writes_text_outside_ascii_as_utf8()
    {
        var body = new CreateEstablishment { Code = "001", Address = "Av. Amazonas & Colón <1>" };
        Assert.Equal("{\"code\":\"001\",\"address\":\"Av. Amazonas & Colón <1>\"}", Encoding.UTF8.GetString(Json.Serialize(body)));
    }

    [Fact]
    public void Writes_the_query_of_a_list_with_its_api_names()
    {
        var query = Json.Query(new DocumentListParams { Limit = 5, Status = DocumentStatus.Authorized, AccessKey = "1" });
        Assert.Equal(
            new[] { new KeyValuePair<string, string>("limit", "5"), new KeyValuePair<string, string>("status", "authorized"), new KeyValuePair<string, string>("accessKey", "1") },
            query);
        Assert.Empty(Json.Query<DocumentListParams>(null));
        Assert.Empty(Json.Query(new DocumentListParams()));
    }

    [Fact]
    public void Writes_and_reads_events_of_each_type()
    {
        Event original = new CertificateEvent
        {
            Id = "e1",
            Type = EventType.CertificateRevoked,
            Environment = FlashpiEnvironment.Live,
            CreatedAt = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero),
            Data = JsonSerializer.Deserialize("{\"id\":\"c1\"}", Json.TypeInfo<Certificate>())!,
        };
        var json = JsonSerializer.Serialize(original, Json.TypeInfo<Event>());
        var read = JsonSerializer.Deserialize(json, Json.TypeInfo<Event>());
        var certificate = Assert.IsType<CertificateEvent>(read);
        Assert.Equal("c1", certificate.Data.Id);
        var webhook = JsonSerializer.Deserialize(json, Json.TypeInfo<WebhookEvent>());
        Assert.IsType<CertificateEvent>(webhook);
        Assert.Contains("\"type\":\"certificate.revoked\"", JsonSerializer.Serialize<WebhookEvent>(certificate, Json.TypeInfo<WebhookEvent>()));
        var thin = new EventThin { Id = "e2", Type = EventType.InvoiceAuthorized, Environment = FlashpiEnvironment.Test, CreatedAt = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        Assert.IsType<EventThin>(JsonSerializer.Deserialize(JsonSerializer.Serialize<WebhookEvent>(thin, Json.TypeInfo<WebhookEvent>()), Json.TypeInfo<WebhookEvent>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("[]", Json.TypeInfo<Event>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("\"x\"", Json.TypeInfo<WebhookEvent>()));
        var unknown = JsonSerializer.Deserialize("{\"id\":\"e3\",\"type\":\"x.y\",\"environment\":\"test\",\"createdAt\":\"2026-11-13T12:00:00Z\",\"data\":1}", Json.TypeInfo<Event>());
        Assert.Contains("\"data\":1", JsonSerializer.Serialize(unknown!, Json.TypeInfo<Event>()));
    }

    [Fact]
    public void Records_of_requests_and_answers_compare_by_value()
    {
        var first = Samples.Invoice();
        Assert.Equal(first.IssuerId, Samples.Invoice().IssuerId);
        Assert.Contains("IssuerId", first.ToString());
        Assert.Equal(new DocumentXml("x", DocumentXmlKind.Authorized), new DocumentXml("x", DocumentXmlKind.Authorized));
    }
}

public sealed class CertificateUploadTests
{
    private static readonly byte[] P12 = [0x30, 0x82, 0x01, 0x02];

    [Fact]
    public async System.Threading.Tasks.Task Sends_the_password_then_the_file_as_multipart_on_every_attempt()
    {
        var api = new FakeApi(Reply.NetworkError(), Reply.Json(new { id = "c1", status = "processing" }, 202));
        using var test = new Harness(api);
        var certificate = await test.Client.Issuers.Certificates.UploadAsync(Samples.Id, new CertificateUpload(P12, "clave ñ"));
        Assert.Equal("c1", certificate.Id);
        Assert.Equal(2, api.Requests.Count);
        foreach (var sent in api.Requests)
        {
            Assert.StartsWith("multipart/form-data; boundary=", sent.Header("Content-Type"));
            Assert.NotNull(sent.Header("Idempotency-Key"));
            var text = Encoding.UTF8.GetString(sent.Body!);
            var password = text.IndexOf("name=\"password\"", StringComparison.Ordinal);
            var file = text.IndexOf("name=\"file\"; filename=\"certificate.p12\"", StringComparison.Ordinal);
            Assert.True(password >= 0 && file > password, text);
            Assert.Contains("clave ñ", text);
            Assert.Contains("Content-Type: application/x-pkcs12", text);
        }
        Assert.Equal(api.Requests[0].Header("Idempotency-Key"), api.Requests[1].Header("Idempotency-Key"));
        Assert.Equal($"/v1/issuers/{Samples.Id}/certificates", api.Last.Path);
    }

    [Fact]
    public async System.Threading.Tasks.Task Reads_a_stream_once_without_disposing_it()
    {
        var api = new FakeApi(Reply.NetworkError(), Reply.Json(new { id = "o1", status = "verifying" }, 202));
        using var test = new Harness(api);
        using var stream = new System.IO.MemoryStream([0xFF, .. P12]);
        stream.ReadByte();
        var onboarding = await test.Client.IssuerOnboardings.CreateAsync(new CertificateUpload(stream, "x") { FileName = "firma.p12" });
        Assert.Equal("o1", onboarding.Id);
        Assert.True(stream.CanRead);
        Assert.Contains("filename=\"firma.p12\"", Encoding.UTF8.GetString(api.Last.Body!));
        var body = api.Last.Body!;
        Assert.Equal((byte)'\n', body[body.Length - 1]);
    }

    [Fact]
    public async System.Threading.Tasks.Task Refuses_an_empty_or_oversized_file_before_sending()
    {
        var api = new FakeApi();
        using var test = new Harness(api);
        var empty = await Assert.ThrowsAsync<FlashpiException>(() => test.Client.IssuerOnboardings.CreateAsync(new CertificateUpload([], "x")));
        Assert.Equal("The certificate file is empty", empty.Message);
        var big = await Assert.ThrowsAsync<FlashpiException>(() => test.Client.IssuerOnboardings.CreateAsync(new CertificateUpload(new byte[1_048_577], "x")));
        Assert.Contains("1048577 bytes", big.Message);
        using var stream = new System.IO.MemoryStream(new byte[2_000_000]);
        var bigStream = await Assert.ThrowsAsync<FlashpiException>(() => test.Client.IssuerOnboardings.CreateAsync(new CertificateUpload(stream, "x")));
        Assert.Contains("more than 1048576 bytes", bigStream.Message);
        Assert.Equal(1_048_577, stream.Position);
        await Assert.ThrowsAsync<ArgumentException>(() => test.Client.IssuerOnboardings.CreateAsync(new CertificateUpload(P12, "x") { FileName = "a\"b" }));
        await Assert.ThrowsAsync<FlashpiException>(() => test.Client.Issuers.Certificates.UploadAsync("", new CertificateUpload(P12, "x")));
        await Assert.ThrowsAsync<ArgumentNullException>(() => test.Client.IssuerOnboardings.CreateAsync(null!));
        Assert.Empty(api.Requests);
    }

    [Fact]
    public void Never_shows_the_file_or_the_password()
    {
        Assert.Throws<ArgumentNullException>(() => new CertificateUpload((byte[])null!, "x"));
        Assert.Throws<ArgumentNullException>(() => new CertificateUpload(P12, null!));
        var upload = new CertificateUpload(P12, "secret-password");
        Assert.Equal("CertificateUpload(FileName=certificate.p12, 4 bytes)", upload.ToString());
        using var stream = new System.IO.MemoryStream(P12);
        Assert.DoesNotContain("secret", new CertificateUpload(stream, "secret-password").ToString());
    }
}
