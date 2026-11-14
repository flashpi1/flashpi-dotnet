using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flashpi.Models;

namespace Flashpi.Internal;

/// <summary>Reads and writes an extensible enum as its JSON string.</summary>
/// <typeparam name="T">The enum struct.</typeparam>
internal abstract class ExtensibleEnumConverter<T> : JsonConverter<T>
    where T : struct
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => Create(reader.GetString()!),
            // A null where the contract promises a value reads as the empty value, as other
            // members the API leaves out read as their defaults.
            JsonTokenType.Null => default,
            _ => throw new JsonException($"Expected a string for {typeof(T).Name}"),
        };

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());

    // As dictionary keys too (a dictionary of counts by status).
    public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Create(reader.GetString()!);

    public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value.ToString()!);

    protected abstract T Create(string value);
}

/// <summary>The body of every list operation: a page of items and where it stands.</summary>
/// <typeparam name="T">The item.</typeparam>
internal interface IListBody<T>
{
    IReadOnlyList<T> Data { get; }

    Pagination Pagination { get; }
}

/// <summary>A request record whose optional nullable members are sent only when set.</summary>
internal interface IPresenceTracked
{
    /// <summary>Whether the member with this JSON name was set (to a value or to null).</summary>
    bool IsSet(string jsonName);
}
