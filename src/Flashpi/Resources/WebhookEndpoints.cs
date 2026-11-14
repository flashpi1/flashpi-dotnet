using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Flashpi.Internal;
using Flashpi.Models;

namespace Flashpi.Resources;

/// <summary>
/// The URLs Flashpi sends events to, and the deliveries of those events. Needs an account-scoped
/// API key with read_write access. To check the signature of a webhook you receive, use
/// <see cref="Webhooks"/> instead.
/// </summary>
public sealed class WebhookEndpoints
{
    private const string Collection = "/v1/webhook-endpoints";
    private const string Item = "/v1/webhook-endpoints/{}";

    private readonly Engine _engine;

    internal WebhookEndpoints(Engine engine)
    {
        _engine = engine;
        WithResponse = new WebhookEndpointsWithResponse(engine);
        Deliveries = new WebhookDeliveries(engine);
    }

    /// <summary>The same methods, returning an <see cref="ApiResponse{T}"/> with the HTTP answer and the request id.</summary>
    public WebhookEndpointsWithResponse WithResponse { get; }

    /// <summary>The deliveries of events to one endpoint.</summary>
    public WebhookDeliveries Deliveries { get; }

    /// <summary>
    /// Creates an endpoint. It starts <c>pending_verification</c>: Flashpi sends it a
    /// <c>webhook_endpoint.verification</c> event, which it must answer with its challenge.
    /// </summary>
    /// <param name="body">The endpoint.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<WebhookEndpoint> CreateAsync(CreateWebhookEndpoint body, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.CreateAsync(body, options, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Lists the endpoints of the key's mode, newest first.</summary>
    /// <param name="parameters">The page size.</param>
    /// <param name="options">Options of every page's request.</param>
    /// <param name="cancellationToken">Cancels every page's request.</param>
    public Paginator<WebhookEndpoint> ListAsync(
        WebhookEndpointListParams? parameters = null, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _engine.List<WebhookEndpointList, WebhookEndpoint>(ListSpec(parameters, options), cancellationToken);

    /// <summary>Retrieves an endpoint.</summary>
    /// <param name="endpointId">The endpoint's id.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<WebhookEndpoint> GetAsync(string endpointId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.GetAsync(endpointId, options, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Updates an endpoint: only the members you set change. A new URL is verified again.</summary>
    /// <param name="endpointId">The endpoint's id.</param>
    /// <param name="body">The members to change.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<WebhookEndpoint> UpdateAsync(
        string endpointId, UpdateWebhookEndpoint body, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.UpdateAsync(endpointId, body, options, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Deletes an endpoint.</summary>
    /// <param name="endpointId">The endpoint's id.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task DeleteAsync(string endpointId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        WithResponse.DeleteAsync(endpointId, options, cancellationToken);

    /// <summary>The endpoint's signing secret (<c>whsec_...</c>), to verify the webhooks it receives.</summary>
    /// <param name="endpointId">The endpoint's id.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<WebhookEndpointSecret> GetSecretAsync(string endpointId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.GetSecretAsync(endpointId, options, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>
    /// Rotates the signing secret. The previous one keeps signing too until
    /// <c>PreviousSecretExpiresInHours</c> pass (24 by default), so you can deploy the new one.
    /// </summary>
    /// <param name="endpointId">The endpoint's id.</param>
    /// <param name="body">How long the previous secret keeps signing, or null for the default (no body is sent).</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<WebhookEndpoint> RotateSecretAsync(
        string endpointId, RotateWebhookEndpointSecret? body = null, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.RotateSecretAsync(endpointId, body, options, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Sends the endpoint a new <c>webhook_endpoint.verification</c> event.</summary>
    /// <param name="endpointId">The endpoint's id.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<WebhookEndpoint> VerifyAsync(string endpointId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.VerifyAsync(endpointId, options, cancellationToken).ConfigureAwait(false)).Data;

    internal static RequestSpec ListSpec(WebhookEndpointListParams? parameters, RequestOptions? options) =>
        new() { Method = HttpMethod.Get, Path = Collection, Query = Json.Query(parameters), Options = options };

    internal static RequestSpec CreateSpec(CreateWebhookEndpoint body, RequestOptions? options) =>
        new() { Method = HttpMethod.Post, Path = Collection, Body = Json.Serialize(Checked.Body(body)), Options = options };

    internal static RequestSpec GetSpec(string endpointId, RequestOptions? options) =>
        new() { Method = HttpMethod.Get, Path = Paths.Of(Item, endpointId), Options = options };

    internal static RequestSpec UpdateSpec(string endpointId, UpdateWebhookEndpoint body, RequestOptions? options) =>
        new() { Method = RequestSpec.Patch, Path = Paths.Of(Item, endpointId), Body = Json.Serialize(Checked.Body(body)), Options = options };

    internal static RequestSpec DeleteSpec(string endpointId, RequestOptions? options) =>
        new() { Method = HttpMethod.Delete, Path = Paths.Of(Item, endpointId), Options = options };

    internal static RequestSpec SecretSpec(string endpointId, RequestOptions? options) =>
        new() { Method = HttpMethod.Get, Path = Paths.Of(Item + "/secret", endpointId), Options = options };

    internal static RequestSpec RotateSpec(string endpointId, RotateWebhookEndpointSecret? body, RequestOptions? options) =>
        new()
        {
            Method = HttpMethod.Post,
            Path = Paths.Of(Item + "/rotate-secret", endpointId),
            Body = body is null ? null : Json.Serialize(body),
            Options = options,
        };

    internal static RequestSpec VerifySpec(string endpointId, RequestOptions? options) =>
        new() { Method = HttpMethod.Post, Path = Paths.Of(Item + "/verify", endpointId), Options = options };
}

/// <summary>The methods of <see cref="WebhookEndpoints"/>, returning the HTTP answer too.</summary>
public sealed class WebhookEndpointsWithResponse
{
    private readonly Engine _engine;

    internal WebhookEndpointsWithResponse(Engine engine) => _engine = engine;

    /// <inheritdoc cref="WebhookEndpoints.CreateAsync"/>
    public async Task<ApiResponse<WebhookEndpoint>> CreateAsync(
        CreateWebhookEndpoint body, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.JsonAsync<WebhookEndpoint>(WebhookEndpoints.CreateSpec(body, options), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc cref="WebhookEndpoints.ListAsync"/>
    public async Task<ApiResponse<Page<WebhookEndpoint>>> ListAsync(
        WebhookEndpointListParams? parameters = null, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.PageAsync<WebhookEndpointList, WebhookEndpoint>(WebhookEndpoints.ListSpec(parameters, options), cancellationToken, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc cref="WebhookEndpoints.GetAsync"/>
    public async Task<ApiResponse<WebhookEndpoint>> GetAsync(string endpointId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.JsonAsync<WebhookEndpoint>(WebhookEndpoints.GetSpec(endpointId, options), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc cref="WebhookEndpoints.UpdateAsync"/>
    public async Task<ApiResponse<WebhookEndpoint>> UpdateAsync(
        string endpointId, UpdateWebhookEndpoint body, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.JsonAsync<WebhookEndpoint>(WebhookEndpoints.UpdateSpec(endpointId, body, options), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc cref="WebhookEndpoints.DeleteAsync"/>
    public async Task<ApiResponse> DeleteAsync(string endpointId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.NoContentAsync(WebhookEndpoints.DeleteSpec(endpointId, options), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc cref="WebhookEndpoints.GetSecretAsync"/>
    public async Task<ApiResponse<WebhookEndpointSecret>> GetSecretAsync(
        string endpointId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.JsonAsync<WebhookEndpointSecret>(WebhookEndpoints.SecretSpec(endpointId, options), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc cref="WebhookEndpoints.RotateSecretAsync"/>
    public async Task<ApiResponse<WebhookEndpoint>> RotateSecretAsync(
        string endpointId, RotateWebhookEndpointSecret? body = null, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.JsonAsync<WebhookEndpoint>(WebhookEndpoints.RotateSpec(endpointId, body, options), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc cref="WebhookEndpoints.VerifyAsync"/>
    public async Task<ApiResponse<WebhookEndpoint>> VerifyAsync(string endpointId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.JsonAsync<WebhookEndpoint>(WebhookEndpoints.VerifySpec(endpointId, options), cancellationToken).ConfigureAwait(false);
}

/// <summary>The deliveries of events to one endpoint.</summary>
public sealed class WebhookDeliveries
{
    private const string Collection = "/v1/webhook-endpoints/{}/deliveries";
    private const string Item = "/v1/webhook-endpoints/{}/deliveries/{}";

    private readonly Engine _engine;

    internal WebhookDeliveries(Engine engine)
    {
        _engine = engine;
        WithResponse = new WebhookDeliveriesWithResponse(engine);
    }

    /// <summary>The same methods, returning an <see cref="ApiResponse{T}"/> with the HTTP answer and the request id.</summary>
    public WebhookDeliveriesWithResponse WithResponse { get; }

    /// <summary>Lists deliveries, newest first, optionally by status, event type or event.</summary>
    /// <param name="endpointId">The endpoint's id.</param>
    /// <param name="parameters">Filters and page size.</param>
    /// <param name="options">Options of every page's request.</param>
    /// <param name="cancellationToken">Cancels every page's request.</param>
    public Paginator<WebhookDelivery> ListAsync(
        string endpointId, WebhookDeliveryListParams? parameters = null, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _engine.List<WebhookDeliveryList, WebhookDelivery>(ListSpec(endpointId, parameters, options), cancellationToken);

    /// <summary>Retrieves a delivery with its attempts.</summary>
    /// <param name="endpointId">The endpoint's id.</param>
    /// <param name="deliveryId">The delivery's id.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<WebhookDeliveryDetail> GetAsync(string endpointId, string deliveryId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.GetAsync(endpointId, deliveryId, options, cancellationToken).ConfigureAwait(false)).Data;

    /// <summary>Sends the delivery's event again, with the same <c>webhook-id</c>.</summary>
    /// <param name="endpointId">The endpoint's id.</param>
    /// <param name="deliveryId">The delivery's id.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<WebhookDeliveryDetail> ReplayAsync(string endpointId, string deliveryId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.ReplayAsync(endpointId, deliveryId, options, cancellationToken).ConfigureAwait(false)).Data;

    internal static RequestSpec ListSpec(string endpointId, WebhookDeliveryListParams? parameters, RequestOptions? options) =>
        new() { Method = HttpMethod.Get, Path = Paths.Of(Collection, endpointId), Query = Json.Query(parameters), Options = options };

    internal static RequestSpec GetSpec(string endpointId, string deliveryId, RequestOptions? options) =>
        new() { Method = HttpMethod.Get, Path = Paths.Of(Item, endpointId, deliveryId), Options = options };

    internal static RequestSpec ReplaySpec(string endpointId, string deliveryId, RequestOptions? options) =>
        new() { Method = HttpMethod.Post, Path = Paths.Of(Item + "/replay", endpointId, deliveryId), Options = options };
}

/// <summary>The methods of <see cref="WebhookDeliveries"/>, returning the HTTP answer too.</summary>
public sealed class WebhookDeliveriesWithResponse
{
    private readonly Engine _engine;

    internal WebhookDeliveriesWithResponse(Engine engine) => _engine = engine;

    /// <inheritdoc cref="WebhookDeliveries.ListAsync"/>
    public async Task<ApiResponse<Page<WebhookDelivery>>> ListAsync(
        string endpointId, WebhookDeliveryListParams? parameters = null, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.PageAsync<WebhookDeliveryList, WebhookDelivery>(
            WebhookDeliveries.ListSpec(endpointId, parameters, options), cancellationToken, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc cref="WebhookDeliveries.GetAsync"/>
    public async Task<ApiResponse<WebhookDeliveryDetail>> GetAsync(
        string endpointId, string deliveryId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.JsonAsync<WebhookDeliveryDetail>(WebhookDeliveries.GetSpec(endpointId, deliveryId, options), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc cref="WebhookDeliveries.ReplayAsync"/>
    public async Task<ApiResponse<WebhookDeliveryDetail>> ReplayAsync(
        string endpointId, string deliveryId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.JsonAsync<WebhookDeliveryDetail>(WebhookDeliveries.ReplaySpec(endpointId, deliveryId, options), cancellationToken).ConfigureAwait(false);
}
