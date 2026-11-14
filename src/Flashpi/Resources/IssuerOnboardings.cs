using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Flashpi.Internal;
using Flashpi.Models;

namespace Flashpi.Resources;

/// <summary>
/// Creates an issuer from its .p12: upload the certificate, wait while the onboarding is
/// <c>verifying</c> until it is <c>ready</c> (Flashpi reads the holder, RUC and SRI data) or
/// <c>rejected</c>, review the prefill, then complete it. Needs an account-scoped API key with
/// read_write access.
/// </summary>
public sealed class IssuerOnboardings
{
    internal IssuerOnboardings(Engine engine) => WithResponse = new IssuerOnboardingsWithResponse(engine);

    /// <summary>The same methods, returning an <see cref="ApiResponse{T}"/> with the HTTP answer and the request id.</summary>
    public IssuerOnboardingsWithResponse WithResponse { get; }

    /// <summary>Starts an onboarding with a .p12 and its password. It starts <c>verifying</c>.</summary>
    /// <param name="upload">The .p12 and its password.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IssuerOnboarding> CreateAsync(CertificateUpload upload, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.CreateAsync(upload, options, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Retrieves an onboarding, to follow it while it is <c>verifying</c>.</summary>
    /// <param name="onboardingId">The onboarding's id.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<IssuerOnboarding> GetAsync(string onboardingId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.GetAsync(onboardingId, options, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>
    /// Completes a <c>ready</c> onboarding. For a new RUC, <paramref name="body"/> is the issuer to
    /// create (as in <c>Issuers.CreateAsync</c>): <c>LegalName</c>, <c>HeadOfficeAddress</c> and
    /// <c>TaxProfile</c> are required; build it from the onboarding's <c>Prefill</c>, which the API
    /// does not apply by itself. When the onboarding's <c>ExistingIssuer</c> is set, the body can be
    /// left out (<c>{}</c> is sent): the certificate is added to that issuer for both modes (a
    /// renewal). Send <c>Ruc</c> when <c>RucNeedsConfirmation</c> is true.
    /// </summary>
    /// <param name="onboardingId">The onboarding's id.</param>
    /// <param name="body">The issuer to create, or null for a renewal.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<CompletedIssuerOnboarding> CompleteAsync(
        string onboardingId, CompleteIssuerOnboarding? body = null, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.CompleteAsync(onboardingId, body, options, cancellationToken).ConfigureAwait(false)).Data;
}

/// <summary>The methods of <see cref="IssuerOnboardings"/>, returning the HTTP answer too.</summary>
public sealed class IssuerOnboardingsWithResponse
{
    private const string Item = "/v1/issuer-onboardings/{}";

    private readonly Engine _engine;

    internal IssuerOnboardingsWithResponse(Engine engine) => _engine = engine;

    /// <inheritdoc cref="IssuerOnboardings.CreateAsync"/>
    public async Task<ApiResponse<IssuerOnboarding>> CreateAsync(
        CertificateUpload upload, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        var form = await CertificateForm.ReadAsync(upload, cancellationToken).ConfigureAwait(false);
        var spec = new RequestSpec { Method = HttpMethod.Post, Path = "/v1/issuer-onboardings", Multipart = form, Options = options };
        return await _engine.JsonAsync<IssuerOnboarding>(spec, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="IssuerOnboardings.GetAsync"/>
    public async Task<ApiResponse<IssuerOnboarding>> GetAsync(string onboardingId, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        var spec = new RequestSpec { Method = HttpMethod.Get, Path = Paths.Of(Item, onboardingId), Options = options };
        return await _engine.JsonAsync<IssuerOnboarding>(spec, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="IssuerOnboardings.CompleteAsync"/>
    public async Task<ApiResponse<CompletedIssuerOnboarding>> CompleteAsync(
        string onboardingId, CompleteIssuerOnboarding? body = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        var spec = new RequestSpec
        {
            Method = HttpMethod.Post,
            Path = Paths.Of(Item + "/complete", onboardingId),
            Body = Json.Serialize(body ?? new CompleteIssuerOnboarding()),
            Options = options,
        };
        return await _engine.JsonAsync<CompletedIssuerOnboarding>(spec, cancellationToken).ConfigureAwait(false);
    }
}
