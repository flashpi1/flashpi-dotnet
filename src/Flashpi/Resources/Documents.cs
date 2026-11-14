using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Flashpi.Internal;
using Flashpi.Models;

namespace Flashpi.Resources;

/// <summary>
/// Issues one document type. The answer is the document: <c>processing</c> right away (202), or,
/// with <c>Options.WaitForAuthorization</c>, its outcome once the SRI decides (200), unless the wait
/// ends first (202 with <c>processing</c> or <c>pending_sri</c>; follow it with webhooks or
/// <c>Documents.GetAsync</c>).
/// </summary>
/// <typeparam name="TBody">The body of the document type (<see cref="CreateInvoice"/>, ...).</typeparam>
public sealed class DocumentIssuer<TBody>
    where TBody : class
{
    internal DocumentIssuer(Engine engine, string path, Func<TBody, DocumentOptions?> deliveryOptions) =>
        WithResponse = new DocumentIssuerWithResponse<TBody>(engine, path, deliveryOptions);

    /// <summary>The same methods, returning an <see cref="ApiResponse{T}"/> with the HTTP answer and the request id.</summary>
    public DocumentIssuerWithResponse<TBody> WithResponse { get; }

    /// <summary>
    /// Issues the document. Retries reuse the same Idempotency-Key, and when a retry finds the
    /// document already created, its current state is returned instead of an error.
    /// </summary>
    /// <param name="body">The document.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Document> CreateAsync(TBody body, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.CreateAsync(body, options, cancellationToken).ConfigureAwait(false)).Data;
}

/// <summary>The methods of <see cref="DocumentIssuer{TBody}"/>, returning the HTTP answer too.</summary>
/// <typeparam name="TBody">The body of the document type.</typeparam>
public sealed class DocumentIssuerWithResponse<TBody>
    where TBody : class
{
    /// <summary>Seconds the API waits for the SRI in sync mode when <c>WaitTimeoutSeconds</c> is not sent.</summary>
    private const int DefaultWaitSeconds = 15;

    /// <summary>Time on top of the sync wait for Flashpi to build, sign and answer.</summary>
    private const int SyncMarginSeconds = 15;

    private readonly Engine _engine;
    private readonly string _path;
    private readonly Func<TBody, DocumentOptions?> _deliveryOptions;

    internal DocumentIssuerWithResponse(Engine engine, string path, Func<TBody, DocumentOptions?> deliveryOptions)
    {
        _engine = engine;
        _path = path;
        _deliveryOptions = deliveryOptions;
    }

    /// <inheritdoc cref="DocumentIssuer{TBody}.CreateAsync"/>
    public async Task<ApiResponse<Document>> CreateAsync(TBody body, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Checked.Body(body);
        var spec = new RequestSpec
        {
            Method = HttpMethod.Post,
            Path = _path,
            Body = Json.Serialize(body),
            Options = options,
            MinTimeout = SyncTimeoutFloor(_deliveryOptions(body)),
            RecoverDocument = true,
        };
        return await _engine.JsonAsync<Document>(spec, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The shortest per-attempt timeout for issuing: in sync mode the API holds the request for up
    /// to <c>WaitTimeoutSeconds</c> (15 by default) while the SRI decides, so each attempt waits at
    /// least that plus 15 seconds.
    /// </summary>
    private static TimeSpan SyncTimeoutFloor(DocumentOptions? delivery) =>
        delivery?.WaitForAuthorization == true
            ? TimeSpan.FromSeconds((delivery.WaitTimeoutSeconds ?? DefaultWaitSeconds) + SyncMarginSeconds)
            : TimeSpan.Zero;
}

/// <summary>Every issued document, of any type: search, XML, RIDE (PDF) and email.</summary>
public sealed class Documents
{
    private readonly Engine _engine;

    internal Documents(Engine engine)
    {
        _engine = engine;
        WithResponse = new DocumentsWithResponse(engine);
    }

    /// <summary>The same methods, returning an <see cref="ApiResponse{T}"/> with the HTTP answer and the request id.</summary>
    public DocumentsWithResponse WithResponse { get; }

    /// <summary>Lists documents, newest first, with optional filters.</summary>
    /// <param name="parameters">Filters and page size.</param>
    /// <param name="options">Options of every page's request.</param>
    /// <param name="cancellationToken">Cancels every page's request.</param>
    public Paginator<Document> ListAsync(DocumentListParams? parameters = null, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _engine.List<DocumentList, Document>(DocumentSpecs.List(parameters, options), cancellationToken);

    /// <summary>Retrieves a document with its tax status, SRI messages, RIDE and email state.</summary>
    /// <param name="documentId">The document's id.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Document> GetAsync(string documentId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.GetAsync(documentId, options, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>
    /// The document's XML: the authorized one once the SRI authorizes it, otherwise the signed or
    /// unsigned one (see <see cref="DocumentXml.Kind"/>).
    /// </summary>
    /// <param name="documentId">The document's id.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<DocumentXml> GetXmlAsync(string documentId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.GetXmlAsync(documentId, options, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>
    /// The RIDE (the printable PDF) of an authorized document. Throws a
    /// <see cref="ConflictException"/> with code <c>ride_not_available</c> while it is still being generated.
    /// </summary>
    /// <param name="documentId">The document's id.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<byte[]> GetPdfAsync(string documentId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.GetPdfAsync(documentId, options, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>
    /// Emails the authorized document (RIDE and XML) again to the address of its request. The new
    /// delivery shows in <c>Email.Deliveries</c>; daily limits apply (<see cref="RateLimitException"/>).
    /// </summary>
    /// <param name="documentId">The document's id.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Document> SendEmailAsync(string documentId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.SendEmailAsync(documentId, options, cancellationToken).ConfigureAwait(false)).Data;
}

/// <summary>The methods of <see cref="Documents"/>, returning the HTTP answer too.</summary>
public sealed class DocumentsWithResponse
{
    private readonly Engine _engine;

    internal DocumentsWithResponse(Engine engine) => _engine = engine;

    /// <inheritdoc cref="Documents.ListAsync"/>
    public async Task<ApiResponse<Page<Document>>> ListAsync(
        DocumentListParams? parameters = null, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.PageAsync<DocumentList, Document>(DocumentSpecs.List(parameters, options), cancellationToken, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc cref="Documents.GetAsync"/>
    public async Task<ApiResponse<Document>> GetAsync(string documentId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.JsonAsync<Document>(DocumentSpecs.Get(documentId, options), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc cref="Documents.GetXmlAsync"/>
    public async Task<ApiResponse<DocumentXml>> GetXmlAsync(string documentId, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        var spec = new RequestSpec
        {
            Method = HttpMethod.Get,
            Path = Paths.Of("/v1/documents/{}/xml", documentId),
            Accept = "application/xml, application/problem+json",
            Options = options,
        };
        var result = await _engine.TextAsync(spec, cancellationToken).ConfigureAwait(false);
        DocumentXmlKind? kind = result.GetHeader("flashpi-xml-kind") switch
        {
            "authorized" => DocumentXmlKind.Authorized,
            "signed" => DocumentXmlKind.Signed,
            "unsigned" => DocumentXmlKind.Unsigned,
            _ => null,
        };
        return new ApiResponse<DocumentXml>(new DocumentXml(result.Data, kind), result.Response, result.RequestId);
    }

    /// <inheritdoc cref="Documents.GetPdfAsync"/>
    public async Task<ApiResponse<byte[]>> GetPdfAsync(string documentId, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        var spec = new RequestSpec
        {
            Method = HttpMethod.Get,
            Path = Paths.Of("/v1/documents/{}/pdf", documentId),
            Accept = "application/pdf, application/problem+json",
            Options = options,
        };
        return await _engine.BytesAsync(spec, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="Documents.SendEmailAsync"/>
    public async Task<ApiResponse<Document>> SendEmailAsync(string documentId, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        var spec = new RequestSpec
        {
            Method = HttpMethod.Post,
            Path = Paths.Of("/v1/documents/{}/email", documentId),
            Body = Json.Serialize(new SendDocumentEmail()),
            Options = options,
        };
        return await _engine.JsonAsync<Document>(spec, cancellationToken).ConfigureAwait(false);
    }
}

internal static class DocumentSpecs
{
    internal static RequestSpec List(DocumentListParams? parameters, RequestOptions? options) =>
        new() { Method = HttpMethod.Get, Path = "/v1/documents", Query = Json.Query(parameters), Options = options };

    internal static RequestSpec Get(string documentId, RequestOptions? options) =>
        new() { Method = HttpMethod.Get, Path = Paths.Of("/v1/documents/{}", documentId), Options = options };
}
