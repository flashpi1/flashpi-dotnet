using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Flashpi.Internal;
using Flashpi.Models;

namespace Flashpi.Resources;

/// <summary>
/// The issuers (RUCs) of the account, with their establishments, emission points and .p12
/// certificates. Needs an account-scoped API key with read_write access, also to read.
/// </summary>
public sealed class Issuers
{
    private const string Collection = "/v1/issuers";
    private const string Item = "/v1/issuers/{}";

    private readonly Engine _engine;

    internal Issuers(Engine engine)
    {
        _engine = engine;
        WithResponse = new IssuersWithResponse(engine);
        Establishments = new Establishments(engine);
        Certificates = new Certificates(engine);
    }

    /// <summary>The same methods, returning an <see cref="ApiResponse{T}"/> with the HTTP answer and the request id.</summary>
    public IssuersWithResponse WithResponse { get; }

    /// <summary>The establishments (the 3-digit series codes such as <c>001</c>) of an issuer.</summary>
    public Establishments Establishments { get; }

    /// <summary>The .p12 certificates (firmas electrónicas) of an issuer.</summary>
    public Certificates Certificates { get; }

    /// <summary>Creates an issuer. To start from its .p12 instead, use <c>IssuerOnboardings</c>.</summary>
    /// <param name="body">The issuer.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Issuer> CreateAsync(CreateIssuer body, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.CreateAsync(body, options, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Lists the issuers of the account, newest first.</summary>
    /// <param name="parameters">The page size.</param>
    /// <param name="options">Options of every page's request.</param>
    /// <param name="cancellationToken">Cancels every page's request.</param>
    public Paginator<Issuer> ListAsync(IssuerListParams? parameters = null, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _engine.List<IssuerList, Issuer>(ListSpec(parameters, options), cancellationToken);

    /// <summary>Retrieves an issuer.</summary>
    /// <param name="issuerId">The issuer's id.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Issuer> GetAsync(string issuerId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.GetAsync(issuerId, options, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Updates an issuer: only the members you set change.</summary>
    /// <param name="issuerId">The issuer's id.</param>
    /// <param name="body">The members to change.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Issuer> UpdateAsync(string issuerId, UpdateIssuer body, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.UpdateAsync(issuerId, body, options, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Deletes an issuer that has not issued documents.</summary>
    /// <param name="issuerId">The issuer's id.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(string issuerId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        WithResponse.DeleteAsync(issuerId, options, cancellationToken);

    internal static RequestSpec ListSpec(IssuerListParams? parameters, RequestOptions? options) =>
        new() { Method = HttpMethod.Get, Path = Collection, Query = Json.Query(parameters), Options = options };

    internal static RequestSpec CreateSpec(CreateIssuer body, RequestOptions? options) =>
        new() { Method = HttpMethod.Post, Path = Collection, Body = Json.Serialize(Checked.Body(body)), Options = options };

    internal static RequestSpec GetSpec(string issuerId, RequestOptions? options) =>
        new() { Method = HttpMethod.Get, Path = Paths.Of(Item, issuerId), Options = options };

    internal static RequestSpec UpdateSpec(string issuerId, UpdateIssuer body, RequestOptions? options) =>
        new() { Method = RequestSpec.Patch, Path = Paths.Of(Item, issuerId), Body = Json.Serialize(Checked.Body(body)), Options = options };

    internal static RequestSpec DeleteSpec(string issuerId, RequestOptions? options) =>
        new() { Method = HttpMethod.Delete, Path = Paths.Of(Item, issuerId), Options = options };
}

/// <summary>The methods of <see cref="Issuers"/>, returning the HTTP answer too.</summary>
public sealed class IssuersWithResponse
{
    private readonly Engine _engine;

    internal IssuersWithResponse(Engine engine) => _engine = engine;

    /// <inheritdoc cref="Issuers.CreateAsync"/>
    public async Task<ApiResponse<Issuer>> CreateAsync(CreateIssuer body, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.JsonAsync<Issuer>(Issuers.CreateSpec(body, options), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc cref="Issuers.ListAsync"/>
    public async Task<ApiResponse<Page<Issuer>>> ListAsync(IssuerListParams? parameters = null, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.PageAsync<IssuerList, Issuer>(Issuers.ListSpec(parameters, options), cancellationToken, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc cref="Issuers.GetAsync"/>
    public async Task<ApiResponse<Issuer>> GetAsync(string issuerId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.JsonAsync<Issuer>(Issuers.GetSpec(issuerId, options), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc cref="Issuers.UpdateAsync"/>
    public async Task<ApiResponse<Issuer>> UpdateAsync(string issuerId, UpdateIssuer body, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.JsonAsync<Issuer>(Issuers.UpdateSpec(issuerId, body, options), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc cref="Issuers.DeleteAsync"/>
    public async Task<ApiResponse> DeleteAsync(string issuerId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.NoContentAsync(Issuers.DeleteSpec(issuerId, options), cancellationToken).ConfigureAwait(false);
}

/// <summary>The establishments (the 3-digit series codes such as <c>001</c>) of an issuer.</summary>
public sealed class Establishments
{
    private const string Collection = "/v1/issuers/{}/establishments";
    private const string Item = "/v1/issuers/{}/establishments/{}";

    private readonly Engine _engine;

    internal Establishments(Engine engine)
    {
        _engine = engine;
        WithResponse = new EstablishmentsWithResponse(engine);
        EmissionPoints = new EmissionPoints(engine);
    }

    /// <summary>The same methods, returning an <see cref="ApiResponse{T}"/> with the HTTP answer and the request id.</summary>
    public EstablishmentsWithResponse WithResponse { get; }

    /// <summary>The emission points (the second 3-digit series code) of an establishment.</summary>
    public EmissionPoints EmissionPoints { get; }

    /// <summary>Creates an establishment of an issuer.</summary>
    /// <param name="issuerId">The issuer's id.</param>
    /// <param name="body">The establishment.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Establishment> CreateAsync(string issuerId, CreateEstablishment body, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.CreateAsync(issuerId, body, options, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Lists the establishments of an issuer, by code.</summary>
    /// <param name="issuerId">The issuer's id.</param>
    /// <param name="parameters">The page size.</param>
    /// <param name="options">Options of every page's request.</param>
    /// <param name="cancellationToken">Cancels every page's request.</param>
    public Paginator<Establishment> ListAsync(
        string issuerId, EstablishmentListParams? parameters = null, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _engine.List<EstablishmentList, Establishment>(ListSpec(issuerId, parameters, options), cancellationToken);

    /// <summary>Retrieves an establishment.</summary>
    /// <param name="issuerId">The issuer's id.</param>
    /// <param name="establishmentId">The establishment's id.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Establishment> GetAsync(string issuerId, string establishmentId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.GetAsync(issuerId, establishmentId, options, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Updates an establishment: only the members you set change.</summary>
    /// <param name="issuerId">The issuer's id.</param>
    /// <param name="establishmentId">The establishment's id.</param>
    /// <param name="body">The members to change.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Establishment> UpdateAsync(
        string issuerId, string establishmentId, UpdateEstablishment body, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.UpdateAsync(issuerId, establishmentId, body, options, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Deletes an establishment that has not issued documents.</summary>
    /// <param name="issuerId">The issuer's id.</param>
    /// <param name="establishmentId">The establishment's id.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(string issuerId, string establishmentId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        WithResponse.DeleteAsync(issuerId, establishmentId, options, cancellationToken);

    internal static RequestSpec ListSpec(string issuerId, EstablishmentListParams? parameters, RequestOptions? options) =>
        new() { Method = HttpMethod.Get, Path = Paths.Of(Collection, issuerId), Query = Json.Query(parameters), Options = options };

    internal static RequestSpec CreateSpec(string issuerId, CreateEstablishment body, RequestOptions? options) =>
        new() { Method = HttpMethod.Post, Path = Paths.Of(Collection, issuerId), Body = Json.Serialize(Checked.Body(body)), Options = options };

    internal static RequestSpec GetSpec(string issuerId, string establishmentId, RequestOptions? options) =>
        new() { Method = HttpMethod.Get, Path = Paths.Of(Item, issuerId, establishmentId), Options = options };

    internal static RequestSpec UpdateSpec(string issuerId, string establishmentId, UpdateEstablishment body, RequestOptions? options) =>
        new()
        {
            Method = RequestSpec.Patch,
            Path = Paths.Of(Item, issuerId, establishmentId),
            Body = Json.Serialize(Checked.Body(body)),
            Options = options,
        };

    internal static RequestSpec DeleteSpec(string issuerId, string establishmentId, RequestOptions? options) =>
        new() { Method = HttpMethod.Delete, Path = Paths.Of(Item, issuerId, establishmentId), Options = options };
}

/// <summary>The methods of <see cref="Establishments"/>, returning the HTTP answer too.</summary>
public sealed class EstablishmentsWithResponse
{
    private readonly Engine _engine;

    internal EstablishmentsWithResponse(Engine engine) => _engine = engine;

    /// <inheritdoc cref="Establishments.CreateAsync"/>
    public async Task<ApiResponse<Establishment>> CreateAsync(
        string issuerId, CreateEstablishment body, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.JsonAsync<Establishment>(Establishments.CreateSpec(issuerId, body, options), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc cref="Establishments.ListAsync"/>
    public async Task<ApiResponse<Page<Establishment>>> ListAsync(
        string issuerId, EstablishmentListParams? parameters = null, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.PageAsync<EstablishmentList, Establishment>(Establishments.ListSpec(issuerId, parameters, options), cancellationToken, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc cref="Establishments.GetAsync"/>
    public async Task<ApiResponse<Establishment>> GetAsync(
        string issuerId, string establishmentId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.JsonAsync<Establishment>(Establishments.GetSpec(issuerId, establishmentId, options), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc cref="Establishments.UpdateAsync"/>
    public async Task<ApiResponse<Establishment>> UpdateAsync(
        string issuerId, string establishmentId, UpdateEstablishment body, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.JsonAsync<Establishment>(Establishments.UpdateSpec(issuerId, establishmentId, body, options), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc cref="Establishments.DeleteAsync"/>
    public async Task<ApiResponse> DeleteAsync(string issuerId, string establishmentId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.NoContentAsync(Establishments.DeleteSpec(issuerId, establishmentId, options), cancellationToken).ConfigureAwait(false);
}

/// <summary>The emission points (the second 3-digit series code) of an establishment.</summary>
public sealed class EmissionPoints
{
    private const string Collection = "/v1/issuers/{}/establishments/{}/emission-points";
    private const string Item = "/v1/issuers/{}/establishments/{}/emission-points/{}";

    private readonly Engine _engine;

    internal EmissionPoints(Engine engine)
    {
        _engine = engine;
        WithResponse = new EmissionPointsWithResponse(engine);
    }

    /// <summary>The same methods, returning an <see cref="ApiResponse{T}"/> with the HTTP answer and the request id.</summary>
    public EmissionPointsWithResponse WithResponse { get; }

    /// <summary>Creates an emission point of an establishment.</summary>
    /// <param name="issuerId">The issuer's id.</param>
    /// <param name="establishmentId">The establishment's id.</param>
    /// <param name="body">The emission point.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<EmissionPoint> CreateAsync(
        string issuerId, string establishmentId, CreateEmissionPoint body, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.CreateAsync(issuerId, establishmentId, body, options, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Lists the emission points of an establishment, by code.</summary>
    /// <param name="issuerId">The issuer's id.</param>
    /// <param name="establishmentId">The establishment's id.</param>
    /// <param name="parameters">The page size.</param>
    /// <param name="options">Options of every page's request.</param>
    /// <param name="cancellationToken">Cancels every page's request.</param>
    public Paginator<EmissionPoint> ListAsync(
        string issuerId,
        string establishmentId,
        EmissionPointListParams? parameters = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _engine.List<EmissionPointList, EmissionPoint>(ListSpec(issuerId, establishmentId, parameters, options), cancellationToken);

    /// <summary>Retrieves an emission point.</summary>
    /// <param name="issuerId">The issuer's id.</param>
    /// <param name="establishmentId">The establishment's id.</param>
    /// <param name="emissionPointId">The emission point's id.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<EmissionPoint> GetAsync(
        string issuerId, string establishmentId, string emissionPointId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.GetAsync(issuerId, establishmentId, emissionPointId, options, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Deletes an emission point that has not issued documents.</summary>
    /// <param name="issuerId">The issuer's id.</param>
    /// <param name="establishmentId">The establishment's id.</param>
    /// <param name="emissionPointId">The emission point's id.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(
        string issuerId, string establishmentId, string emissionPointId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        WithResponse.DeleteAsync(issuerId, establishmentId, emissionPointId, options, cancellationToken);

    internal static RequestSpec ListSpec(string issuerId, string establishmentId, EmissionPointListParams? parameters, RequestOptions? options) =>
        new() { Method = HttpMethod.Get, Path = Paths.Of(Collection, issuerId, establishmentId), Query = Json.Query(parameters), Options = options };

    internal static RequestSpec CreateSpec(string issuerId, string establishmentId, CreateEmissionPoint body, RequestOptions? options) =>
        new()
        {
            Method = HttpMethod.Post,
            Path = Paths.Of(Collection, issuerId, establishmentId),
            Body = Json.Serialize(Checked.Body(body)),
            Options = options,
        };

    internal static RequestSpec GetSpec(string issuerId, string establishmentId, string emissionPointId, RequestOptions? options) =>
        new() { Method = HttpMethod.Get, Path = Paths.Of(Item, issuerId, establishmentId, emissionPointId), Options = options };

    internal static RequestSpec DeleteSpec(string issuerId, string establishmentId, string emissionPointId, RequestOptions? options) =>
        new() { Method = HttpMethod.Delete, Path = Paths.Of(Item, issuerId, establishmentId, emissionPointId), Options = options };
}

/// <summary>The methods of <see cref="EmissionPoints"/>, returning the HTTP answer too.</summary>
public sealed class EmissionPointsWithResponse
{
    private readonly Engine _engine;

    internal EmissionPointsWithResponse(Engine engine) => _engine = engine;

    /// <inheritdoc cref="EmissionPoints.CreateAsync"/>
    public async Task<ApiResponse<EmissionPoint>> CreateAsync(
        string issuerId, string establishmentId, CreateEmissionPoint body, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.JsonAsync<EmissionPoint>(EmissionPoints.CreateSpec(issuerId, establishmentId, body, options), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc cref="EmissionPoints.ListAsync"/>
    public async Task<ApiResponse<Page<EmissionPoint>>> ListAsync(
        string issuerId,
        string establishmentId,
        EmissionPointListParams? parameters = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await _engine.PageAsync<EmissionPointList, EmissionPoint>(
            EmissionPoints.ListSpec(issuerId, establishmentId, parameters, options), cancellationToken, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc cref="EmissionPoints.GetAsync"/>
    public async Task<ApiResponse<EmissionPoint>> GetAsync(
        string issuerId, string establishmentId, string emissionPointId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.JsonAsync<EmissionPoint>(EmissionPoints.GetSpec(issuerId, establishmentId, emissionPointId, options), cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc cref="EmissionPoints.DeleteAsync"/>
    public async Task<ApiResponse> DeleteAsync(
        string issuerId, string establishmentId, string emissionPointId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.NoContentAsync(EmissionPoints.DeleteSpec(issuerId, establishmentId, emissionPointId, options), cancellationToken).ConfigureAwait(false);
}

/// <summary>The .p12 certificates (firmas electrónicas) of an issuer.</summary>
public sealed class Certificates
{
    private const string Collection = "/v1/issuers/{}/certificates";
    private const string Item = "/v1/issuers/{}/certificates/{}";

    private readonly Engine _engine;

    internal Certificates(Engine engine)
    {
        _engine = engine;
        WithResponse = new CertificatesWithResponse(engine);
    }

    /// <summary>The same methods, returning an <see cref="ApiResponse{T}"/> with the HTTP answer and the request id.</summary>
    public CertificatesWithResponse WithResponse { get; }

    /// <summary>
    /// Uploads a .p12 for the issuer, for the mode of the API key only (test or live). To renew the
    /// certificate for both modes at once, use <c>IssuerOnboardings</c> with the new .p12. The
    /// certificate starts <c>processing</c> while Flashpi verifies it.
    /// </summary>
    /// <param name="issuerId">The issuer's id.</param>
    /// <param name="upload">The .p12 and its password.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Certificate> UploadAsync(string issuerId, CertificateUpload upload, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.UploadAsync(issuerId, upload, options, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Lists the certificates of an issuer, newest first.</summary>
    /// <param name="issuerId">The issuer's id.</param>
    /// <param name="parameters">The page size.</param>
    /// <param name="options">Options of every page's request.</param>
    /// <param name="cancellationToken">Cancels every page's request.</param>
    public Paginator<Certificate> ListAsync(
        string issuerId, CertificateListParams? parameters = null, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _engine.List<CertificateList, Certificate>(ListSpec(issuerId, parameters, options), cancellationToken);

    /// <summary>Retrieves a certificate.</summary>
    /// <param name="issuerId">The issuer's id.</param>
    /// <param name="certificateId">The certificate's id.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Certificate> GetAsync(string issuerId, string certificateId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.GetAsync(issuerId, certificateId, options, cancellationToken).ConfigureAwait(false)).Data;

    internal static RequestSpec ListSpec(string issuerId, CertificateListParams? parameters, RequestOptions? options) =>
        new() { Method = HttpMethod.Get, Path = Paths.Of(Collection, issuerId), Query = Json.Query(parameters), Options = options };

    internal static RequestSpec UploadSpec(string issuerId, CertificateForm form, RequestOptions? options) =>
        new() { Method = HttpMethod.Post, Path = Paths.Of(Collection, issuerId), Multipart = form, Options = options };

    internal static RequestSpec GetSpec(string issuerId, string certificateId, RequestOptions? options) =>
        new() { Method = HttpMethod.Get, Path = Paths.Of(Item, issuerId, certificateId), Options = options };
}

/// <summary>The methods of <see cref="Certificates"/>, returning the HTTP answer too.</summary>
public sealed class CertificatesWithResponse
{
    private const string Collection = "/v1/issuers/{}/certificates";

    private readonly Engine _engine;

    internal CertificatesWithResponse(Engine engine) => _engine = engine;

    /// <inheritdoc cref="Certificates.UploadAsync"/>
    public async Task<ApiResponse<Certificate>> UploadAsync(
        string issuerId, CertificateUpload upload, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        // The id is checked before the file is read.
        _ = Paths.Of(Collection, issuerId);
        var form = await CertificateForm.ReadAsync(upload, cancellationToken).ConfigureAwait(false);
        return await _engine.JsonAsync<Certificate>(Certificates.UploadSpec(issuerId, form, options), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="Certificates.ListAsync"/>
    public async Task<ApiResponse<Page<Certificate>>> ListAsync(
        string issuerId, CertificateListParams? parameters = null, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.PageAsync<CertificateList, Certificate>(Certificates.ListSpec(issuerId, parameters, options), cancellationToken, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc cref="Certificates.GetAsync"/>
    public async Task<ApiResponse<Certificate>> GetAsync(string issuerId, string certificateId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.JsonAsync<Certificate>(Certificates.GetSpec(issuerId, certificateId, options), cancellationToken).ConfigureAwait(false);
}
