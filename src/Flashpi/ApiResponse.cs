using System.Net.Http;
using Flashpi.Internal;

namespace Flashpi;

/// <summary>
/// The HTTP answer of a call and Flashpi's id of the request, from a method of a resource's
/// <c>WithResponse</c> view: <c>await flashpi.Documents.WithResponse.GetAsync(id)</c>.
/// </summary>
public class ApiResponse
{
    internal ApiResponse(HttpResponseMessage response, string? requestId)
    {
        Response = response;
        RequestId = requestId;
    }

    /// <summary>
    /// The HTTP answer (status, headers). Its body was already read; its request no longer carries
    /// the API key.
    /// </summary>
    public HttpResponseMessage Response { get; }

    /// <summary>The HTTP status.</summary>
    public int StatusCode => (int)Response.StatusCode;

    /// <summary>Flashpi's id of the request (<c>x-request-id</c>): share it with support.</summary>
    public string? RequestId { get; }

    /// <summary>A header of the answer in any letter case (repeated ones joined with commas), or null.</summary>
    /// <param name="name">The header's name, such as <c>Idempotent-Replayed</c>.</param>
    public string? GetHeader(string name) => HeaderValues.Get(Response, name);
}

/// <summary>The data of a call together with the HTTP answer it came from.</summary>
/// <typeparam name="T">The data.</typeparam>
public sealed class ApiResponse<T> : ApiResponse
{
    internal ApiResponse(T data, HttpResponseMessage response, string? requestId)
        : base(response, requestId)
    {
        Data = data;
    }

    /// <summary>What the method without <c>WithResponse</c> returns.</summary>
    public T Data { get; }
}
