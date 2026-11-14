using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Flashpi.Internal;
using Flashpi.Models;

namespace Flashpi.Resources;

/// <summary>Events: what happened to documents, certificates and email sending, newest first.</summary>
public sealed class Events
{
    private readonly Engine _engine;

    internal Events(Engine engine)
    {
        _engine = engine;
        WithResponse = new EventsWithResponse(engine);
    }

    /// <summary>The same methods, returning an <see cref="ApiResponse{T}"/> with the HTTP answer and the request id.</summary>
    public EventsWithResponse WithResponse { get; }

    /// <summary>Lists events, optionally by type, issuer, document or certificate.</summary>
    /// <param name="parameters">Filters and page size.</param>
    /// <param name="options">Options of every page's request.</param>
    /// <param name="cancellationToken">Cancels every page's request.</param>
    public Paginator<Event> ListAsync(EventListParams? parameters = null, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _engine.List<EventList, Event>(ListSpec(parameters, options), cancellationToken);

    /// <summary>Retrieves an event: the same object its webhook delivered.</summary>
    /// <param name="eventId">The event's id.</param>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<Event> GetAsync(string eventId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.GetAsync(eventId, options, cancellationToken).ConfigureAwait(false)).Data;

    internal static RequestSpec ListSpec(EventListParams? parameters, RequestOptions? options) =>
        new() { Method = HttpMethod.Get, Path = "/v1/events", Query = Json.Query(parameters), Options = options };

    internal static RequestSpec GetSpec(string eventId, RequestOptions? options) =>
        new() { Method = HttpMethod.Get, Path = Paths.Of("/v1/events/{}", eventId), Options = options };
}

/// <summary>The methods of <see cref="Events"/>, returning the HTTP answer too.</summary>
public sealed class EventsWithResponse
{
    private readonly Engine _engine;

    internal EventsWithResponse(Engine engine) => _engine = engine;

    /// <inheritdoc cref="Events.ListAsync"/>
    public async Task<ApiResponse<Page<Event>>> ListAsync(EventListParams? parameters = null, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.PageAsync<EventList, Event>(Events.ListSpec(parameters, options), cancellationToken, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc cref="Events.GetAsync"/>
    public async Task<ApiResponse<Event>> GetAsync(string eventId, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.JsonAsync<Event>(Events.GetSpec(eventId, options), cancellationToken).ConfigureAwait(false);
}

/// <summary>The API's own status. It needs no API key.</summary>
public sealed class StatusResource
{
    internal StatusResource(Engine engine) => WithResponse = new StatusResourceWithResponse(engine);

    /// <summary>The same methods, returning an <see cref="ApiResponse{T}"/> with the HTTP answer and the request id.</summary>
    public StatusResourceWithResponse WithResponse { get; }

    /// <summary>The API's status.</summary>
    /// <param name="options">Options of this call.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<ApiStatus> GetAsync(RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        (await WithResponse.GetAsync(options, cancellationToken).ConfigureAwait(false)).Data;
}

/// <summary>The methods of <see cref="StatusResource"/>, returning the HTTP answer too.</summary>
public sealed class StatusResourceWithResponse
{
    private readonly Engine _engine;

    internal StatusResourceWithResponse(Engine engine) => _engine = engine;

    /// <inheritdoc cref="StatusResource.GetAsync"/>
    public async Task<ApiResponse<ApiStatus>> GetAsync(RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        await _engine.JsonAsync<ApiStatus>(new RequestSpec { Method = HttpMethod.Get, Path = "/v1/status", Options = options }, cancellationToken)
            .ConfigureAwait(false);
}
