using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Flashpi.Models;

/// <summary>
/// What a webhook endpoint receives: an <see cref="Event"/> (<see cref="DocumentEvent"/>,
/// <see cref="CertificateEvent"/>, <see cref="AccountEvent"/>, or <see cref="UnknownEvent"/> for a
/// type this version of the SDK does not know yet), an <see cref="EventThin"/> on endpoints in thin
/// mode, or the activation handshake, <see cref="WebhookEndpointVerificationEvent"/>, which must be
/// answered with <c>{"challenge": data.challenge}</c>. Match it on its type.
/// </summary>
[JsonConverter(typeof(WebhookEventJsonConverter))]
public abstract partial record WebhookEvent
{
    private protected WebhookEvent()
    {
    }

    /// <summary>The event's id, also its <c>webhook-id</c> header: deduplicate on it.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The type of the event, such as <c>invoice.authorized</c>.</summary>
    [JsonPropertyName("type")]
    public required EventType Type { get; init; }

    /// <summary>The mode of the resource: <c>test</c> or <c>live</c>.</summary>
    [JsonPropertyName("environment")]
    public required FlashpiEnvironment Environment { get; init; }

    /// <summary>When it happened.</summary>
    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Reads a webhook body as the record its shape and type name.</summary>
internal sealed class WebhookEventJsonConverter : JsonConverter<WebhookEvent>
{
    private const string VerificationType = "webhook_endpoint.verification";

    public override WebhookEvent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var element = document.RootElement;
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A webhook event must be a JSON object");
        }
        var type = element.TryGetProperty("type", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
        if (type == VerificationType)
        {
            return Read<WebhookEndpointVerificationEvent>(element, options);
        }
        // Endpoints in thin mode receive events without their data.
        return element.TryGetProperty("data", out _)
            ? EventJsonConverter.ReadElement(element, options)
            : Read<EventThin>(element, options);
    }

    public override void Write(Utf8JsonWriter writer, WebhookEvent value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case Event item:
                JsonSerializer.Serialize(writer, item, (JsonTypeInfo<Event>)options.GetTypeInfo(typeof(Event)));
                break;
            case EventThin item:
                JsonSerializer.Serialize(writer, item, (JsonTypeInfo<EventThin>)options.GetTypeInfo(typeof(EventThin)));
                break;
            case WebhookEndpointVerificationEvent item:
                JsonSerializer.Serialize(
                    writer, item, (JsonTypeInfo<WebhookEndpointVerificationEvent>)options.GetTypeInfo(typeof(WebhookEndpointVerificationEvent)));
                break;
            default:
                throw new JsonException($"Cannot write the webhook event {value.GetType()}");
        }
    }

    private static T Read<T>(JsonElement element, JsonSerializerOptions options)
        where T : class =>
        element.Deserialize((JsonTypeInfo<T>)options.GetTypeInfo(typeof(T))) ?? throw new JsonException("A webhook event cannot be null");
}
