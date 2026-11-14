using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using Flashpi.Internal;
using Flashpi.Models;
using Flashpi.Resources;

namespace Flashpi;

/// <summary>
/// The Flashpi API client. Create one and reuse it: it keeps a pool of connections.
/// </summary>
/// <remarks>
/// An <c>fp_test_</c> key issues in the SRI test environment (pruebas); an <c>fp_live_</c> key
/// issues real documents in production. Use it on servers only: the key must stay secret.
/// <code>
/// using var flashpi = new FlashpiClient(); // reads FLASHPI_API_KEY
/// var invoice = await flashpi.Invoices.CreateAsync(new CreateInvoice { ... });
/// </code>
/// </remarks>
public sealed class FlashpiClient : IDisposable
{
    private const int DefaultMaxRetries = 2;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    private readonly HttpClient? _ownedHttpClient;
    private readonly Engine _engine;

    /// <summary>A client with the key of the <c>FLASHPI_API_KEY</c> environment variable.</summary>
    public FlashpiClient()
        : this(new FlashpiClientOptions())
    {
    }

    /// <summary>A client with an API key.</summary>
    /// <param name="apiKey">The API key (<c>fp_test_...</c> or <c>fp_live_...</c>).</param>
    public FlashpiClient(string apiKey)
        : this(new FlashpiClientOptions { ApiKey = apiKey })
    {
    }

    /// <summary>A client with settings.</summary>
    /// <param name="options">The settings: every member is optional.</param>
    public FlashpiClient(FlashpiClientOptions options)
        : this(options, ClientHooks.Default)
    {
    }

    internal FlashpiClient(FlashpiClientOptions options, ClientHooks hooks)
    {
        Checked.NotNull(options, nameof(options));
        // The key is checked first, so a missing or bad key is the error reported.
        var apiKey = Checks.ApiKey(options.ApiKey);
        var defaultHeaders = Checks.Headers("DefaultHeaders", options.DefaultHeaders);
        foreach (var header in defaultHeaders)
        {
            if (string.Equals(header.Key, "Idempotency-Key", StringComparison.OrdinalIgnoreCase))
            {
                throw new FlashpiException(
                    "DefaultHeaders cannot carry an Idempotency-Key: it must be unique per operation. Pass RequestOptions.IdempotencyKey to each call instead");
            }
        }
        var config = new ClientConfig(
            apiKey,
            Checks.BaseUrl(options.BaseUrl),
            Checks.Timeout("Timeout", options.Timeout ?? DefaultTimeout),
            Checks.Retries("MaxRetries", options.MaxRetries ?? DefaultMaxRetries),
            Checks.Language("Language", options.Language),
            defaultHeaders);
        Environment = apiKey.StartsWith("fp_test_", StringComparison.Ordinal) ? FlashpiEnvironment.Test : FlashpiEnvironment.Live;

        var httpClient = options.HttpClient;
        if (httpClient is null)
        {
            _ownedHttpClient = CreateHttpClient();
            httpClient = _ownedHttpClient;
        }
        _engine = new Engine(config, httpClient, hooks);

        Invoices = new DocumentIssuer<CreateInvoice>(_engine, "/v1/invoices", body => body.Options);
        CreditNotes = new DocumentIssuer<CreateCreditNote>(_engine, "/v1/credit-notes", body => body.Options);
        DebitNotes = new DocumentIssuer<CreateDebitNote>(_engine, "/v1/debit-notes", body => body.Options);
        PurchaseSettlements = new DocumentIssuer<CreatePurchaseSettlement>(_engine, "/v1/purchase-settlements", body => body.Options);
        Waybills = new DocumentIssuer<CreateWaybill>(_engine, "/v1/waybills", body => body.Options);
        Withholdings = new DocumentIssuer<CreateWithholding>(_engine, "/v1/withholdings", body => body.Options);
        Documents = new Documents(_engine);
        Issuers = new Issuers(_engine);
        IssuerOnboardings = new IssuerOnboardings(_engine);
        WebhookEndpoints = new WebhookEndpoints(_engine);
        Events = new Events(_engine);
        Status = new StatusResource(_engine);
    }

    /// <summary><c>Test</c> for <c>fp_test_</c> keys (SRI pruebas), <c>Live</c> for <c>fp_live_</c> keys (SRI production).</summary>
    public FlashpiEnvironment Environment { get; }

    /// <summary>Invoices (facturas).</summary>
    public DocumentIssuer<CreateInvoice> Invoices { get; }

    /// <summary>Credit notes (notas de crédito).</summary>
    public DocumentIssuer<CreateCreditNote> CreditNotes { get; }

    /// <summary>Debit notes (notas de débito).</summary>
    public DocumentIssuer<CreateDebitNote> DebitNotes { get; }

    /// <summary>Purchase settlements (liquidaciones de compra).</summary>
    public DocumentIssuer<CreatePurchaseSettlement> PurchaseSettlements { get; }

    /// <summary>Waybills (guías de remisión).</summary>
    public DocumentIssuer<CreateWaybill> Waybills { get; }

    /// <summary>Withholding receipts (comprobantes de retención).</summary>
    public DocumentIssuer<CreateWithholding> Withholdings { get; }

    /// <summary>Every issued document: search, XML, RIDE (PDF) and email.</summary>
    public Documents Documents { get; }

    /// <summary>Issuers (RUCs) with their establishments, emission points and certificates.</summary>
    public Issuers Issuers { get; }

    /// <summary>Create an issuer from its .p12 certificate.</summary>
    public IssuerOnboardings IssuerOnboardings { get; }

    /// <summary>The URLs Flashpi sends events to.</summary>
    public WebhookEndpoints WebhookEndpoints { get; }

    /// <summary>Events of documents, certificates and email sending.</summary>
    public Events Events { get; }

    /// <summary>The API's status.</summary>
    public StatusResource Status { get; }

    /// <summary>Closes the connections, unless the <see cref="HttpClient"/> came from you.</summary>
    public void Dispose() => _ownedHttpClient?.Dispose();

    /// <summary>The environment and the API's address, never the key.</summary>
    public override string ToString() => $"FlashpiClient(Environment={Environment}, BaseUrl={_engine.Config.BaseUrl})";

    /// <summary>
    /// The SDK's own HttpClient: it never follows redirects, decompresses answers, and leaves the
    /// timeouts to the SDK, which limits each attempt.
    /// </summary>
    private static HttpClient CreateHttpClient()
    {
#if NET
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            // Connections are renewed, so DNS changes are seen.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
#else
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        RemoveConnectionLimit(handler);
#endif
        return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

#if !NET
    /// <summary>
    /// .NET Framework allows 2 connections per server outside ASP.NET: calls would wait for one
    /// while their deadline runs. Lifted as .NET does; .NET Framework before 4.7.1 has no such
    /// setting, and keeps its limit.
    /// </summary>
    private static void RemoveConnectionLimit(HttpClientHandler handler)
    {
        try
        {
            SetMaxConnections(handler);
        }
        catch (Exception error) when (error is MissingMethodException or PlatformNotSupportedException or NotSupportedException)
        {
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void SetMaxConnections(HttpClientHandler handler) => handler.MaxConnectionsPerServer = int.MaxValue;
#endif
}
