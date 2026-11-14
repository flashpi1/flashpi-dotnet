# Flashpi for .NET

The official .NET SDK for [Flashpi](https://flashpi.com.ec), the electronic invoicing API for
Ecuador's SRI, for .NET and C#.

> **Not published on NuGet yet.** The first version (0.1.0) is being prepared. Meanwhile, call the
> API over plain HTTP: see the [documentation](https://flashpi.com.ec/docs/en/).

[Español](https://github.com/flashpi1/flashpi-dotnet/blob/main/README.md)

- The 45 operations of the API: the six document types, documents (XML, RIDE PDF, email),
  issuers with establishments, emission points and .p12 certificates, webhooks and events.
- Async methods (`Task` with a `CancellationToken`) and lists you walk with `await foreach`.
- Models (`record`s) generated from the API's OpenAPI document, with typed properties: your
  editor knows every field and the compiler asks for the required ones.
- Safe retries: every issuing request carries an `Idempotency-Key`, so a retry never issues a
  document twice.
- Typed errors (RFC 9457) with their `Code`, field errors and `RequestId`.
- Webhook signature verification (Standard Webhooks) for ASP.NET Core or any framework.
- .NET 10 with no dependencies; .NET 8 and 9 and .NET Framework 4.6.2 or later with
  System.Text.Json.

## Install

```bash
dotnet add package Flashpi
```

In .NET Framework projects and `netstandard2.0` libraries, use C# 11 or later
(`<LangVersion>latest</LangVersion>` in the project): the models have `required` and `init`
properties, which Visual Basic cannot set.

## Getting started

Create an API key in the [console](https://flashpi.com.ec/app/) and keep it in the
`FLASHPI_API_KEY` environment variable. An `fp_test_` key issues in the SRI's test environment;
an `fp_live_` key issues real documents in production.

```csharp
using Flashpi;
using Flashpi.Models;

// Reads FLASHPI_API_KEY; you can also pass it: new FlashpiClient("fp_test_...")
using var flashpi = new FlashpiClient();

var invoice = await flashpi.Invoices.CreateAsync(new CreateInvoice
{
    IssuerId = "<the demo issuer id or yours>",
    Establishment = "001",
    EmissionPoint = "001",
    Customer = new DocumentParty
    {
        IdType = IdType.Ruc,
        Id = "1760013210001",
        Name = "PRUEBAS SERVICIO DE RENTAS INTERNAS",
        Address = "Av. Amazonas y Villalengua, Quito",
    },
    Items =
    [
        new DocumentItem
        {
            Code = "P-001",
            Description = "Test service",
            Quantity = "1",
            UnitPrice = "1.00",
            Taxes = [new DocumentTax { Type = TaxType.Vat, Rate = "15" }],
        },
    ],
    Payments = [new DocumentPayment { Method = PaymentMethod.Cash, Amount = "1.15" }],
    Options = new DocumentOptions { WaitForAuthorization = true },
});

Console.WriteLine($"{invoice.Status} {invoice.AccessKey}");
```

The models are in `Flashpi.Models`: their properties are the API's in PascalCase
(`invoice.AccessKey` is `accessKey`). Amounts and quantities are **strings** (`"1.15"`), never
numbers, so no decimals are lost. Fixed values such as `IdType.Ruc` or
`DocumentStatus.Authorized` also hold values this version of the SDK does not know yet: compare
them with `==` or read their `Value`.

With the **demo issuer** you can issue in test mode without uploading a certificate: its id is on
the console's Overview (up to 50 documents a day). More in
[Sandbox and demo issuer](https://flashpi.com.ec/docs/en/sandbox/).

A client keeps a pool of connections: create one and reuse it (in ASP.NET Core, register it as a
singleton: `builder.Services.AddSingleton(new FlashpiClient())`). Release it with `Dispose()` or
`using`.

## Issuing documents

| Document            | Method                                          |
| ------------------- | ----------------------------------------------- |
| Invoice             | `flashpi.Invoices.CreateAsync(body)`            |
| Credit note         | `flashpi.CreditNotes.CreateAsync(body)`         |
| Debit note          | `flashpi.DebitNotes.CreateAsync(body)`          |
| Purchase settlement | `flashpi.PurchaseSettlements.CreateAsync(body)` |
| Waybill             | `flashpi.Waybills.CreateAsync(body)`            |
| Withholding         | `flashpi.Withholdings.CreateAsync(body)`        |

All of them return the document. By default the answer comes right away with the status
`processing`, and the SRI's outcome arrives by webhook. With
`Options = new DocumentOptions { WaitForAuthorization = true }` the API waits for the SRI (15 s by
default, up to 30 s with `WaitTimeoutSeconds`) and returns `authorized`, `rejected` or `failed`;
if the SRI takes longer, it returns `processing` or `pending_sri` and the document carries on.

## Documents

```csharp
var document = await flashpi.Documents.GetAsync(invoice.Id);

var xml = await flashpi.Documents.GetXmlAsync(invoice.Id); // xml.Kind: Authorized once the SRI authorized it
var pdf = await flashpi.Documents.GetPdfAsync(invoice.Id); // the RIDE, as bytes
await File.WriteAllBytesAsync($"{invoice.AccessKey}.pdf", pdf);

await flashpi.Documents.SendEmailAsync(invoice.Id); // emails the RIDE and XML to the buyer again
```

## Lists and pagination

`await foreach` walks every page, fetching each one as you go. `await` gives only the first:

```csharp
await foreach (var document in flashpi.Documents.ListAsync(new DocumentListParams
{
    Status = DocumentStatus.Authorized,
    IssueDateFrom = "2026-11-01",
}))
{
    Console.WriteLine(document.AccessKey);
}

var page = await flashpi.Documents.ListAsync(new DocumentListParams { Limit = 50 });
Console.WriteLine(page.Data.Count); // the documents of this page
if (page.HasNextPage)
{
    var next = await page.GetNextPageAsync();
}
```

## Errors

Every API error is a subclass of `ApiException` by its HTTP status. Branch on `Code`, which is
stable and in English; `Message` follows the requested language.

```csharp
try
{
    await flashpi.Invoices.CreateAsync(body);
}
catch (UnprocessableEntityException error)
{
    Console.WriteLine(error.Code); // validation_failed, total_mismatch, ...
    foreach (var field in error.Errors)
    {
        Console.WriteLine($"{field.Field}: {field.Message}");
    }
    throw;
}
catch (ApiException error)
{
    Console.WriteLine($"{error.StatusCode} {error.Code} {error.RequestId}"); // share RequestId with support
    throw;
}
```

| Class                          | Status | Examples of `Code`                                           |
| ------------------------------ | ------ | ------------------------------------------------------------ |
| `BadRequestException`          | 400    | `malformed_json`, `invalid_idempotency_key`                  |
| `AuthenticationException`      | 401    | `authentication_required`, `invalid_api_key`                 |
| `PermissionDeniedException`    | 403    | `key_read_only`, `key_scope_mismatch`, `ip_not_allowed`      |
| `NotFoundException`            | 404    | `not_found`                                                  |
| `ConflictException`            | 409    | `sequential_already_used`, `ride_not_available`              |
| `UnprocessableEntityException` | 422    | `validation_failed`, `total_mismatch`                        |
| `RateLimitException`           | 429    | `demo_issuer_quota_exceeded`, `account_email_limit_reached`  |
| `InternalServerException`      | 5xx    | `internal_error`                                             |
| `ApiConnectionException`       | —      | no answer (network); `ApiTimeoutException` when it timed out |

All of them extend `FlashpiException`. Some errors carry extra data in `error.Problem`, such as
`DocumentId`, `Limit`, `ResetsAt` or `RideStatus`. For messages in Spanish:
`Language = Language.Spanish` in `FlashpiClientOptions`.

## Retries and idempotency

The SDK retries up to 2 times, with a growing wait, network errors, timeouts, 408, 429 (when it
asks to wait 60 s or less) and 5xx answers. Every POST carries an `Idempotency-Key` repeated on
its retries, so the API never issues twice:

- If a retry finds that the first attempt did create the document, the SDK returns that
  document.
- If the first attempt is still running on the API (for example, the connection dropped while it
  waited for the SRI), the SDK waits for it with the same key, up to the call's timeout, without
  spending retries.

To use your own key (for example your order id), pass it to the call:

```csharp
await flashpi.Invoices.CreateAsync(body, new RequestOptions { IdempotencyKey = $"order-{order.Id}" });
```

If an issue fails anyway, the error (a `FlashpiException`) carries the key it used in
`error.IdempotencyKey`. Retrying with that key never issues a second document: within 24 hours
the API returns the first answer; later (or if the API lost the first attempt), the call fails with
a 422 `idempotency_key_reused` whose `error.Problem.DocumentId` is the document that already
exists (read it with `flashpi.Documents.GetAsync`).

```csharp
try
{
    await flashpi.Invoices.CreateAsync(body);
}
catch (FlashpiException error) when (error.IdempotencyKey is not null)
{
    SaveForRetry(body, error.IdempotencyKey); // your retry queue
    throw;
}
```

Cancelling a call through its `CancellationToken` throws `OperationCanceledException`, which is
not a `FlashpiException` and carries no key: if you cancel issues, pass your own
`IdempotencyKey`.

Daily limits (such as the demo issuer's quota) are not retried: they are thrown as
`RateLimitException` with `RetryAfter`.

## Webhooks

Verify every webhook with the body **exactly as received** (not parsed) and the endpoint's
`whsec_` secret. `Webhooks.Unwrap` needs no API key and does no I/O, and returns the event as its
type: `WebhookEndpointVerificationEvent` (the endpoint's activation), `DocumentEvent`,
`CertificateEvent`, `AccountEvent`, `EventThin` (endpoints in `thin` mode) or `UnknownEvent` (a
type this version of the SDK does not know yet).

With ASP.NET Core (minimal API):

```csharp
using Flashpi;
using Flashpi.Models;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapPost("/webhooks/flashpi", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    var payload = await reader.ReadToEndAsync();
    WebhookEvent webhook;
    try
    {
        webhook = Webhooks.Unwrap(payload, request.Headers, Environment.GetEnvironmentVariable("FLASHPI_WEBHOOK_SECRET")!);
    }
    catch (WebhookVerificationException)
    {
        return Results.BadRequest();
    }

    switch (webhook)
    {
        case WebhookEndpointVerificationEvent verification:
            // The endpoint's activation: answer the challenge.
            return Results.Json(new { challenge = verification.Data.Challenge });
        case DocumentEvent authorized when authorized.Type == EventType.InvoiceAuthorized:
            Console.WriteLine($"Authorized {authorized.Data.AccessKey}"); // Data is the document
            break;
        case DocumentEvent rejected when rejected.Type == EventType.InvoiceRejected:
            Console.WriteLine($"Rejected {rejected.Data.SriMessages.Count}");
            break;
    }
    return Results.NoContent();
});

app.Run();
```

With an ASP.NET Core MVC controller:

```csharp
using Flashpi;
using Flashpi.Models;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("webhooks/flashpi")]
public sealed class FlashpiWebhookController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive()
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync();
        try
        {
            var webhook = Webhooks.Unwrap(payload, Request.Headers, Environment.GetEnvironmentVariable("FLASHPI_WEBHOOK_SECRET")!);
            if (webhook is WebhookEndpointVerificationEvent verification)
            {
                return Ok(new { challenge = verification.Data.Challenge });
            }
            HandleEvent(webhook); // your code, like the branches of the example above
            return NoContent();
        }
        catch (WebhookVerificationException)
        {
            return BadRequest();
        }
    }

    private static void HandleEvent(WebhookEvent webhook) => Console.WriteLine(webhook.Type);
}
```

`Webhooks.Unwrap` also takes `HttpHeaders`, a `Dictionary<string, string>` and ASP.NET's
`NameValueCollection` on .NET Framework, and the body as text or as a `byte[]`. Keep the event's
`Id` and drop repeats: an event can arrive more than once. During a secret rotation, Flashpi signs
with the new secret and the previous one, so either works. An `EventThin` has no `Data`: fetch it
with `flashpi.Events.GetAsync(webhook.Id)`.

## Issuers and certificates

These operations need an account-wide key with read and write access.

An issuer starts from its electronic signature (.p12): Flashpi verifies it, reads the RUC and asks
the SRI, and returns a `Prefill` with what it found so you can build the issuer.

```csharp
var onboarding = await flashpi.IssuerOnboardings.CreateAsync(
    new CertificateUpload(File.ReadAllBytes("signature.p12"), Environment.GetEnvironmentVariable("P12_PASSWORD")!));
while (onboarding.Status == IssuerOnboardingStatus.Verifying)
{
    await Task.Delay(TimeSpan.FromSeconds(2));
    onboarding = await flashpi.IssuerOnboardings.GetAsync(onboarding.Id);
}
if (onboarding.Status != IssuerOnboardingStatus.Ready)
{
    throw new InvalidOperationException(onboarding.Rejection?.Message ?? onboarding.Status.Value);
}

CompletedIssuerOnboarding completed;
if (onboarding.ExistingIssuer is not null)
{
    // You already have that RUC: the certificate is added to that issuer (a renewal).
    completed = await flashpi.IssuerOnboardings.CompleteAsync(onboarding.Id);
}
else
{
    // A new RUC: you confirm the issuer's data, starting from the prefill.
    var prefill = onboarding.Prefill;
    var regime = prefill?.TaxProfile.RimpeRegime?.Value;
    completed = await flashpi.IssuerOnboardings.CompleteAsync(onboarding.Id, new CompleteIssuerOnboarding
    {
        LegalName = prefill?.LegalName ?? "MY COMPANY S.A.",
        HeadOfficeAddress = prefill?.HeadOfficeAddress ?? "Av. Amazonas N24-100, Quito",
        TaxProfile = new IssuerTaxProfileInput
        {
            KeepsAccounting = prefill?.TaxProfile.KeepsAccounting ?? false,
            RimpeRegime = regime is "entrepreneur" or "popular_business" ? new RimpeRegime(regime) : default(RimpeRegime?),
            // For a special taxpayer or a withholding agent, add their resolutions.
        },
        // When onboarding.RucNeedsConfirmation is true, send Ruc too.
    });
}

await foreach (var establishment in flashpi.Issuers.Establishments.ListAsync(completed.Issuer.Id))
{
    Console.WriteLine(establishment.Code);
}
```

The .p12 can be bytes or a `Stream` (`File.OpenRead("signature.p12")`), up to 1 MiB; the SDK
reads it once and does not close it.

To **renew an issuer's signature** for test and live mode at once, repeat the same flow with the
new .p12: Flashpi recognizes the RUC (`ExistingIssuer`) and `CompleteAsync()` without data adds
the certificate. `flashpi.Issuers.Certificates.UploadAsync(issuerId, upload)`, instead, uploads a
certificate for the mode of the key you use only (test or live).

Updates (`UpdateAsync`) change only the properties you set; setting `null` on one that allows it
(such as `TradeName`) clears it.

## Options

```csharp
using Flashpi;

using var flashpi = new FlashpiClient(new FlashpiClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("FLASHPI_API_KEY"), // the default anyway
    Language = Language.Spanish, // language of the messages: English (default) or Spanish
    Timeout = TimeSpan.FromSeconds(60), // per attempt (with WaitForAuthorization, at least the wait + 15 s)
    MaxRetries = 2, // 0 turns retries off
});
```

Every method also takes, before the `CancellationToken`, options of that call: `IdempotencyKey`,
`Timeout`, `MaxRetries`, `Language`, `CorrelationId` (your own id, which Flashpi logs) and
`ExtraHeaders`.

```csharp
var document = await flashpi.Documents.GetAsync(documentId, new RequestOptions
{
    Timeout = TimeSpan.FromSeconds(10),
    Language = Language.Spanish,
    CorrelationId = "order-42",
});
```

To see the HTTP answer and the request id, call the method through `WithResponse`:

```csharp
var result = await flashpi.Documents.WithResponse.GetAsync(documentId);
Console.WriteLine($"{result.Data.Status} {result.StatusCode} {result.RequestId}");
```

To send the requests with your own `HttpClient`, for example through a proxy, pass it as
`HttpClient`. You dispose it; give it an infinite `Timeout` (the SDK limits each attempt) and a
handler that does not follow redirects:

```csharp
using System.Net;
using Flashpi;

var http = new HttpClient(new SocketsHttpHandler
{
    Proxy = new WebProxy("http://proxy.internal:3128"),
    AllowAutoRedirect = false,
})
{
    Timeout = Timeout.InfiniteTimeSpan,
};
using var flashpi = new FlashpiClient(new FlashpiClientOptions { HttpClient = http });
```

In tests, an `HttpMessageHandler` of yours answers instead of the API.

## Server side only

Your API key is a secret: use the SDK on your servers, never in code that runs on your users'
devices. `flashpi.ToString()` shows the environment and the URL, never the key.

## License

[MIT](https://github.com/flashpi1/flashpi-dotnet/blob/main/LICENSE) © 2026 SIGNIADIGITAL S.A.
