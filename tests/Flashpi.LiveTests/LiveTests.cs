using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Flashpi.Models;
using Xunit;

namespace Flashpi.LiveTests;

/// <summary>
/// The SDK against the real Flashpi API in test mode (SRI pruebas), port of the Node SDK's
/// test/live/live.test.ts. It issues ONE real test invoice on the shared demo issuer, which counts
/// toward its daily quota of 50, and never accepts an fp_live_ key. FLASHPI_BASE_URL may point to
/// another API.
/// </summary>
public sealed class LiveTests(LiveInvoice live) : IClassFixture<LiveInvoice>
{
    [Fact]
    public async Task Reads_the_api_status()
    {
        live.SkipWithoutSettings();
        var status = await live.Client.Status.GetAsync();
        Assert.Equal(ApiStatusKind.Operational, status.Status);
    }

    [Fact]
    public async Task Rejects_an_invalid_invoice_with_field_errors_in_spanish()
    {
        live.SkipWithoutSettings();
        var body = LiveInvoice.SampleInvoice() with { Items = [] };
        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            live.Client.Invoices.CreateAsync(body, new RequestOptions { Language = Language.Spanish, MaxRetries = 0 }));
        Assert.Equal(ErrorCode.ValidationFailed, error.Code);
        Assert.NotEmpty(error.Errors);
        Assert.Matches("^[0-9a-f-]{36}$", error.RequestId);
        Assert.Equal("es", error.GetHeader("content-language"));
    }

    [Fact]
    public async Task Issues_a_test_invoice_and_replays_its_idempotency_key()
    {
        var invoice = await live.InvoiceAsync();
        Assert.Equal(DocumentStatus.Authorized, invoice.Status);
        Assert.Matches("^\\d{49}$", invoice.AccessKey);
        var replay = await live.Client.Invoices.WithResponse.CreateAsync(LiveInvoice.SampleInvoice(), new RequestOptions { IdempotencyKey = live.IdempotencyKey });
        Assert.Equal(invoice.Id, replay.Data.Id);
        Assert.Equal("true", replay.GetHeader("idempotent-replayed"));
    }

    [Fact]
    public async Task Finds_the_document_by_its_access_key()
    {
        var invoice = await live.InvoiceAsync();
        var page = await live.Client.Documents.ListAsync(new DocumentListParams { AccessKey = invoice.AccessKey, Limit = 5 });
        Assert.Equal(invoice.Id, Assert.Single(page.Data).Id);
    }

    [Fact]
    public async Task Downloads_the_authorized_xml()
    {
        var invoice = await live.InvoiceAsync();
        var xml = await live.Client.Documents.GetXmlAsync(invoice.Id);
        Assert.Equal(DocumentXmlKind.Authorized, xml.Kind);
        Assert.Contains(invoice.AccessKey, xml.Xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Downloads_the_ride_once_it_is_generated()
    {
        var invoice = await live.InvoiceAsync();
        byte[]? pdf = null;
        for (var poll = 0; poll < LiveInvoice.Polls && pdf is null; poll++)
        {
            try
            {
                pdf = await live.Client.Documents.GetPdfAsync(invoice.Id);
            }
            catch (ConflictException error) when (error.Code?.Value == "ride_not_available")
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
        Assert.NotNull(pdf);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdf!, 0, 5));
    }

    [Fact]
    public async Task Lists_the_events_of_the_document()
    {
        var invoice = await live.InvoiceAsync();
        var types = new List<EventType>();
        for (var poll = 0; poll < 12 && !types.Contains(EventType.InvoiceAuthorized); poll++)
        {
            if (poll > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
            types.Clear();
            await foreach (var item in live.Client.Events.ListAsync(new EventListParams { DocumentId = invoice.Id }))
            {
                types.Add(item.Type);
                Assert.Equal(invoice.Id, Assert.IsType<DocumentEvent>(item).Data.Id);
            }
        }
        Assert.Contains(EventType.InvoiceAuthorized, types);
    }

    [Fact]
    public async Task Answers_404_for_a_document_that_does_not_exist()
    {
        live.SkipWithoutSettings();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            live.Client.Documents.GetAsync("01a00000-0000-7000-8000-000000000000", new RequestOptions { MaxRetries = 0 }));
    }
}

/// <summary>The client, and the invoice the suite issues once and shares.</summary>
public sealed class LiveInvoice : IDisposable
{
    /// <summary>Checks of a slow SRI step, 5 seconds apart: up to 2 minutes.</summary>
    internal const int Polls = 24;

    private static readonly string ApiKey = Environment.GetEnvironmentVariable("FLASHPI_TEST_API_KEY") ?? "";
    private static readonly string IssuerId = Environment.GetEnvironmentVariable("FLASHPI_DEMO_ISSUER_ID") ?? "";
    private readonly FlashpiClient? _client;
    private Task<Document>? _invoice;

    public LiveInvoice()
    {
        if (ApiKey.Length == 0 || IssuerId.Length == 0)
        {
            return;
        }
        if (!ApiKey.StartsWith("fp_test_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("FLASHPI_TEST_API_KEY must be an fp_test_ key: this suite never runs live");
        }
        var baseUrl = Environment.GetEnvironmentVariable("FLASHPI_BASE_URL");
        _client = new FlashpiClient(new FlashpiClientOptions
        {
            ApiKey = ApiKey,
            BaseUrl = string.IsNullOrEmpty(baseUrl) ? null : new Uri(baseUrl),
        });
    }

    public FlashpiClient Client => _client!;

    public string IdempotencyKey { get; } = $"sdk-live-dotnet-{DateTimeOffset.UtcNow.ToUnixTimeSeconds():x}-{Guid.NewGuid()}";

    public void SkipWithoutSettings() =>
        Assert.SkipWhen(_client is null, "Needs FLASHPI_TEST_API_KEY and FLASHPI_DEMO_ISSUER_ID");

    /// <summary>The same test invoice as the docs' quickstart: USD 1.00 + 15 % VAT to the SRI's test RUC.</summary>
    public static CreateInvoice SampleInvoice() => new()
    {
        IssuerId = IssuerId,
        Establishment = "001",
        EmissionPoint = "001",
        Customer = new DocumentParty
        {
            IdType = IdType.Ruc,
            Id = "1760013210001",
            Name = "PRUEBAS SERVICIO DE RENTAS INTERNAS",
            Address = "AMAZONAS AVENUE AND VILLALENGUA, QUITO",
        },
        Items =
        [
            new DocumentItem
            {
                Code = "SDK-LIVE-001",
                Description = "Flashpi (.NET) live test",
                Quantity = "1",
                UnitPrice = "1.00",
                Taxes = [new DocumentTax { Type = TaxType.Vat, Rate = "15" }],
            },
        ],
        Payments = [new DocumentPayment { Method = PaymentMethod.Cash, Amount = "1.15" }],
        Options = new DocumentOptions { WaitForAuthorization = true, WaitTimeoutSeconds = 30, SendEmail = false },
    };

    /// <summary>The invoice, issued once for the whole suite and followed until the SRI decides.</summary>
    public Task<Document> InvoiceAsync()
    {
        SkipWithoutSettings();
        lock (this)
        {
            return _invoice ??= IssueAsync();
        }
    }

    private async Task<Document> IssueAsync()
    {
        var result = await Client.Invoices.WithResponse.CreateAsync(
            SampleInvoice(), new RequestOptions { IdempotencyKey = IdempotencyKey, CorrelationId = "flashpi-dotnet-live" });
        Assert.True(result.StatusCode is 200 or 202, $"status {result.StatusCode}");
        Assert.NotNull(result.RequestId);
        Assert.Equal(DocumentType.Invoice, result.Data.Type);
        Assert.Equal(FlashpiEnvironment.Test, result.Data.Environment);
        var document = result.Data;
        // The SRI's test environment can be slow: follow the document for up to 2 minutes.
        for (var poll = 0; poll < Polls && (document.Status == DocumentStatus.Processing || document.Status == DocumentStatus.PendingSri); poll++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            document = await Client.Documents.GetAsync(document.Id);
        }
        Assert.Matches(new Regex("^\\d{49}$"), document.AccessKey);
        return document;
    }

    public void Dispose() => _client?.Dispose();
}
