# Flashpi para .NET

SDK oficial de [Flashpi](https://flashpi.com.ec), la API de facturación electrónica del SRI de
Ecuador, para .NET y C#.

[English](https://github.com/flashpi1/flashpi-dotnet/blob/main/README.en.md)

- Las 45 operaciones de la API: los seis comprobantes, documentos (XML, RIDE en PDF, correo),
  emisores con establecimientos, puntos de emisión y certificados .p12, webhooks y eventos.
- Métodos asíncronos (`Task` con `CancellationToken`) y listas que se recorren con
  `await foreach`.
- Modelos (`record`) generados del documento OpenAPI de la API, con propiedades tipadas: tu editor
  conoce cada campo y el compilador te pide los obligatorios.
- Reintentos seguros: cada emisión lleva un `Idempotency-Key`, así un reintento nunca emite dos
  veces.
- Errores tipados (RFC 9457) con su `Code`, los errores por campo y el `RequestId`.
- Verificación de firmas de webhooks (Standard Webhooks) para ASP.NET Core o cualquier framework.
- .NET 10 sin dependencias; .NET 8 y 9 y .NET Framework 4.6.2 o superior con System.Text.Json.

## Instalación

```bash
dotnet add package Flashpi
```

En proyectos de .NET Framework y en bibliotecas `netstandard2.0` usa C# 11 o superior
(`<LangVersion>latest</LangVersion>` en el proyecto): los modelos tienen propiedades `required` e
`init`, que Visual Basic no puede asignar.

## Primeros pasos

Crea una clave de API en la [consola](https://flashpi.com.ec/app/) y guárdala en la variable de
entorno `FLASHPI_API_KEY`. Una clave `fp_test_` emite en el ambiente de pruebas del SRI; una
`fp_live_` emite comprobantes reales en producción.

```csharp
using Flashpi;
using Flashpi.Models;

// Lee FLASHPI_API_KEY; también puedes pasarla: new FlashpiClient("fp_test_...")
using var flashpi = new FlashpiClient();

var invoice = await flashpi.Invoices.CreateAsync(new CreateInvoice
{
    IssuerId = "<id del emisor demo o del tuyo>",
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
            Description = "Servicio de prueba",
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

Los modelos están en `Flashpi.Models`: sus propiedades son las de la API en PascalCase
(`invoice.AccessKey` es `accessKey`). Los montos y cantidades son **textos** (`"1.15"`), nunca
números, para no perder decimales. Los valores fijos, como `IdType.Ruc` o
`DocumentStatus.Authorized`, aceptan también los que esta versión del SDK aún no conoce:
compáralos con `==` o lee su `Value`.

Con el **emisor demo** puedes emitir en pruebas sin subir un certificado: su id está en el
Resumen de la consola (hasta 50 comprobantes al día). Más en
[Sandbox y emisor demo](https://flashpi.com.ec/docs/sandbox/).

Un cliente mantiene un grupo de conexiones: crea uno y reutilízalo (en ASP.NET Core, regístralo
como singleton: `builder.Services.AddSingleton(new FlashpiClient())`). Libéralo con `Dispose()` o
con `using`.

## Emitir comprobantes

| Comprobante           | Método                                          |
| --------------------- | ----------------------------------------------- |
| Factura               | `flashpi.Invoices.CreateAsync(body)`            |
| Nota de crédito       | `flashpi.CreditNotes.CreateAsync(body)`         |
| Nota de débito        | `flashpi.DebitNotes.CreateAsync(body)`          |
| Liquidación de compra | `flashpi.PurchaseSettlements.CreateAsync(body)` |
| Guía de remisión      | `flashpi.Waybills.CreateAsync(body)`            |
| Retención             | `flashpi.Withholdings.CreateAsync(body)`        |

Todos devuelven el documento. Por defecto la respuesta llega enseguida con el estado
`processing` y el resultado del SRI llega por webhook. Con
`Options = new DocumentOptions { WaitForAuthorization = true }` la API espera la respuesta del
SRI (15 s por defecto, hasta 30 s con `WaitTimeoutSeconds`) y devuelve `authorized`, `rejected` o
`failed`; si el SRI tarda más, devuelve `processing` o `pending_sri` y el documento sigue su
curso.

En facturas, notas de débito, liquidaciones de compra y los documentos de sustento de una
retención, el último pago puede ir sin `Amount`: Flashpi le pone lo que los demás pagos dejan del
total (todo, si es el único). Con varios pagos, ninguno puede ser 0 (`zero_payment`).

## Documentos

```csharp
var document = await flashpi.Documents.GetAsync(invoice.Id);

var xml = await flashpi.Documents.GetXmlAsync(invoice.Id); // xml.Kind: Authorized cuando ya lo autorizó el SRI
var pdf = await flashpi.Documents.GetPdfAsync(invoice.Id); // el RIDE, en bytes
await File.WriteAllBytesAsync($"{invoice.AccessKey}.pdf", pdf);

await flashpi.Documents.SendEmailAsync(invoice.Id); // reenvía RIDE y XML al correo del comprador
```

## Listas y paginación

Con `await foreach` recorres todas las páginas, que se piden a medida que avanzas. Con `await`
obtienes solo la primera:

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
Console.WriteLine(page.Data.Count); // los documentos de esta página
if (page.HasNextPage)
{
    var next = await page.GetNextPageAsync();
}
```

## Errores

Cada error de la API es una subclase de `ApiException` según su estado HTTP. Decide según `Code`,
que es estable y en inglés; `Message` sigue el idioma pedido.

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
    Console.WriteLine($"{error.StatusCode} {error.Code} {error.RequestId}"); // comparte RequestId con soporte
    throw;
}
```

| Clase                          | Estado | Ejemplos de `Code`                                                                |
| ------------------------------ | ------ | --------------------------------------------------------------------------------- |
| `BadRequestException`          | 400    | `malformed_json`, `invalid_idempotency_key`                                       |
| `AuthenticationException`      | 401    | `authentication_required`, `invalid_api_key`                                      |
| `PermissionDeniedException`    | 403    | `key_read_only`, `key_scope_mismatch`, `ip_not_allowed`, `live_key_required`      |
| `NotFoundException`            | 404    | `not_found`                                                                       |
| `ConflictException`            | 409    | `sequential_already_used`, `ride_not_available`, `webhook_endpoint_limit_reached` |
| `UnprocessableEntityException` | 422    | `validation_failed`, `total_mismatch`                                             |
| `RateLimitException`           | 429    | `demo_issuer_quota_exceeded`, `account_email_limit_reached`                       |
| `InternalServerException`      | 5xx    | `internal_error`                                                                  |
| `ApiConnectionException`       | —      | sin respuesta (red); `ApiTimeoutException` si se agotó el plazo                   |

Todas derivan de `FlashpiException`. Algunos errores traen datos extra en `error.Problem`, por
ejemplo `DocumentId`, `Limit`, `ResetsAt` o `RideStatus`. Para recibir los mensajes en español:
`Language = Language.Spanish` en `FlashpiClientOptions`.

## Reintentos e idempotencia

El SDK reintenta hasta 2 veces, con espera creciente, los errores de red, los tiempos agotados,
las respuestas 408, 429 (si piden esperar 60 s o menos) y 5xx. Cada POST lleva un
`Idempotency-Key` que se repite en sus reintentos, así que la API nunca emite dos veces:

- Si un reintento descubre que el primer intento sí creó el documento, el SDK devuelve ese
  documento.
- Si el primer intento sigue en curso en la API (por ejemplo, se cortó la conexión mientras
  esperaba al SRI), el SDK espera a que termine con la misma clave, hasta el tiempo límite de la
  llamada, sin gastar reintentos.

Para usar tu propia clave (por ejemplo el id de tu pedido), pásala a la llamada:

```csharp
await flashpi.Invoices.CreateAsync(body, new RequestOptions { IdempotencyKey = $"pedido-{order.Id}" });
```

Si una emisión falla igual, el error (un `FlashpiException`) trae la clave que usó en
`error.IdempotencyKey`. Reintentar con esa clave nunca emite un segundo documento: dentro de 24
horas la API devuelve la primera respuesta; después (o si la API perdió el primer intento), la
llamada falla con un 422 `idempotency_key_reused` cuyo `error.Problem.DocumentId` es el documento
que ya existe (léelo con `flashpi.Documents.GetAsync`).

```csharp
try
{
    await flashpi.Invoices.CreateAsync(body);
}
catch (FlashpiException error) when (error.IdempotencyKey is not null)
{
    SaveForRetry(body, error.IdempotencyKey); // tu cola de reintentos
    throw;
}
```

Cancelar una llamada con su `CancellationToken` lanza `OperationCanceledException`, que no es un
`FlashpiException` ni trae clave: si cancelas emisiones, pasa tu propio `IdempotencyKey`.

Los límites diarios (como la cuota del emisor demo) no se reintentan: se lanzan como
`RateLimitException` con `RetryAfter`.

## Webhooks

Verifica cada webhook con el cuerpo **tal cual llegó** (sin parsear) y el secreto `whsec_` del
endpoint. `Webhooks.Unwrap` no necesita clave de API ni hace E/S, y devuelve el evento con su
tipo: `WebhookEndpointVerificationEvent` (la activación del endpoint), `DocumentEvent`,
`CertificateEvent`, `AccountEvent`, `EventThin` (endpoints en modo `thin`) o `UnknownEvent` (un
tipo que esta versión del SDK aún no conoce).

Con ASP.NET Core (minimal API):

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
            // La activación del endpoint: responde el desafío.
            return Results.Json(new { challenge = verification.Data.Challenge });
        case DocumentEvent authorized when authorized.Type == EventType.InvoiceAuthorized:
            Console.WriteLine($"Autorizada {authorized.Data.AccessKey}"); // Data es el documento
            break;
        case DocumentEvent rejected when rejected.Type == EventType.InvoiceRejected:
            Console.WriteLine($"Rechazada {rejected.Data.SriMessages.Count}");
            break;
    }
    return Results.NoContent();
});

app.Run();
```

Con un controlador de ASP.NET Core MVC:

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
            HandleEvent(webhook); // tu código, como las ramas del ejemplo anterior
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

`Webhooks.Unwrap` acepta también `HttpHeaders`, un `Dictionary<string, string>` y el
`NameValueCollection` de ASP.NET en .NET Framework, y el cuerpo como texto o como `byte[]`.
Guarda el `Id` del evento y descarta los repetidos: un evento puede llegar más de una vez. Durante
una rotación del secreto, Flashpi firma con el nuevo y el anterior, así que cualquiera de los dos
sirve. Un evento `EventThin` no trae `Data`: pídelo con `flashpi.Events.GetAsync(webhook.Id)`.

## Emisores y certificados

Estas operaciones necesitan una clave de toda la cuenta con acceso de lectura y escritura.

Un emisor nace de su firma electrónica (.p12): Flashpi la verifica, lee el RUC y consulta al SRI,
y te devuelve un `Prefill` con lo que encontró para que armes el emisor.

```csharp
var onboarding = await flashpi.IssuerOnboardings.CreateAsync(
    new CertificateUpload(File.ReadAllBytes("firma.p12"), Environment.GetEnvironmentVariable("P12_PASSWORD")!));
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
    // Ya tienes ese RUC: el certificado se agrega a ese emisor (una renovación).
    completed = await flashpi.IssuerOnboardings.CompleteAsync(onboarding.Id);
}
else
{
    // Un RUC nuevo: tú confirmas los datos del emisor, partiendo del prefill.
    var prefill = onboarding.Prefill;
    var regime = prefill?.TaxProfile.RimpeRegime?.Value;
    completed = await flashpi.IssuerOnboardings.CompleteAsync(onboarding.Id, new CompleteIssuerOnboarding
    {
        LegalName = prefill?.LegalName ?? "MI EMPRESA S.A.",
        HeadOfficeAddress = prefill?.HeadOfficeAddress ?? "Av. Amazonas N24-100, Quito",
        TaxProfile = new IssuerTaxProfileInput
        {
            KeepsAccounting = prefill?.TaxProfile.KeepsAccounting ?? false,
            RimpeRegime = regime is "entrepreneur" or "popular_business" ? new RimpeRegime(regime) : default(RimpeRegime?),
            // Si es contribuyente especial o agente de retención, agrega sus resoluciones.
        },
        // Si onboarding.RucNeedsConfirmation es true, envía también Ruc.
    });
}

await foreach (var establishment in flashpi.Issuers.Establishments.ListAsync(completed.Issuer.Id))
{
    Console.WriteLine(establishment.Code);
}
```

El .p12 puede ir en bytes o como un `Stream` (`File.OpenRead("firma.p12")`), de hasta 1 MiB; el
SDK lo lee una vez y no lo cierra.

Para **renovar la firma** de un emisor, para pruebas y producción a la vez, repite el mismo
flujo con el .p12 nuevo: Flashpi reconoce el RUC (`ExistingIssuer`) y `CompleteAsync()` sin datos
agrega el certificado. En cambio, `flashpi.Issuers.Certificates.UploadAsync(issuerId, upload)`
sube un certificado solo para el modo de la clave que usas (pruebas o producción).

Un certificado aceptado puede traer avisos en `Warnings` (vacía si no hay ninguno):
`ruc_mismatch` es un certificado de pruebas que no es del RUC del emisor, que firma solo
documentos de pruebas y no sirve para las claves de producción.

En las actualizaciones (`UpdateAsync`) solo cambian las propiedades que asignas; asignar `null` a
una que lo admite (como `TradeName`) la borra.

## Opciones

```csharp
using Flashpi;

using var flashpi = new FlashpiClient(new FlashpiClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("FLASHPI_API_KEY"), // por defecto, esa misma variable
    Language = Language.Spanish, // idioma de los mensajes: English (por defecto) o Spanish
    Timeout = TimeSpan.FromSeconds(60), // por intento (con WaitForAuthorization, al menos la espera + 15 s)
    MaxRetries = 2, // 0 desactiva los reintentos
});
```

Cada método acepta además, antes del `CancellationToken`, opciones de esa llamada:
`IdempotencyKey`, `Timeout`, `MaxRetries`, `Language`, `CorrelationId` (tu propio id, que Flashpi
registra) y `ExtraHeaders`.

```csharp
var document = await flashpi.Documents.GetAsync(documentId, new RequestOptions
{
    Timeout = TimeSpan.FromSeconds(10),
    Language = Language.Spanish,
    CorrelationId = "pedido-42",
});
```

Para ver la respuesta HTTP y el id de la petición, llama al método a través de `WithResponse`:

```csharp
var result = await flashpi.Documents.WithResponse.GetAsync(documentId);
Console.WriteLine($"{result.Data.Status} {result.StatusCode} {result.RequestId}");
```

Para enviar las peticiones con tu propio `HttpClient`, por ejemplo con un proxy, pásalo en
`HttpClient`. Tú lo liberas; dale un `Timeout` infinito (el SDK limita cada intento) y un handler
que no siga redirecciones:

```csharp
using System.Net;
using Flashpi;

var http = new HttpClient(new SocketsHttpHandler
{
    Proxy = new WebProxy("http://proxy.interno:3128"),
    AllowAutoRedirect = false,
})
{
    Timeout = Timeout.InfiniteTimeSpan,
};
using var flashpi = new FlashpiClient(new FlashpiClientOptions { HttpClient = http });
```

En las pruebas, un `HttpMessageHandler` tuyo responde en lugar de la API.

## Solo en el servidor

Tu clave de API es secreta: usa el SDK en tus servidores, nunca en código que corre en los
dispositivos de tus usuarios. `flashpi.ToString()` muestra el ambiente y la URL, nunca la clave.

## Licencia

[MIT](https://github.com/flashpi1/flashpi-dotnet/blob/main/LICENSE) © 2026 SIGNIADIGITAL S.A.
