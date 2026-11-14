using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Flashpi.Models;

namespace Flashpi.Internal;

/// <summary>The SDK's System.Text.Json settings, on the metadata generated at build time.</summary>
internal static class Json
{
    /// <summary>
    /// Compact JSON with text outside ASCII written as UTF-8, as the other SDKs send it. Members
    /// that are null are left out, except the optional nullable members of a request that were set
    /// (<see cref="IPresenceTracked"/>). Members the contract requires are read leniently, as the
    /// other SDKs read them: an answer without one reads as its default instead of failing.
    /// </summary>
    internal static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = FlashpiJsonContext.Default.WithAddedModifier(Modify),
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.MakeReadOnly();
        return options;
    }

    private static void Modify(JsonTypeInfo info)
    {
        foreach (var property in info.Properties)
        {
            property.IsRequired = false;
        }
        if (!PresenceTracking.Members.TryGetValue(info.Type, out var tracked))
        {
            return;
        }
        foreach (var property in info.Properties)
        {
            if (Array.IndexOf(tracked, property.Name) >= 0)
            {
                var name = property.Name;
                property.ShouldSerialize = (owner, _) => ((IPresenceTracked)owner).IsSet(name);
            }
        }
    }

    /// <summary>The metadata of a model.</summary>
    internal static JsonTypeInfo<T> TypeInfo<T>() => TypeInfoCache<T>.Value;

    /// <summary>A request body as UTF-8 JSON, serialized once for every attempt.</summary>
    internal static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, TypeInfo<T>());

    /// <summary>
    /// The query of a list operation from its parameters record: the members that are set, by
    /// their API names, as text (booleans as <c>true</c> and <c>false</c>).
    /// </summary>
    internal static List<KeyValuePair<string, string>> Query<T>(T? parameters)
        where T : class
    {
        var pairs = new List<KeyValuePair<string, string>>();
        if (parameters is null)
        {
            return pairs;
        }
        var element = JsonSerializer.SerializeToElement(parameters, TypeInfo<T>());
        foreach (var member in element.EnumerateObject())
        {
            var value = member.Value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => member.Value.GetString(),
                JsonValueKind.Number => member.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture, "The query member {0} is not a text, a number or a boolean", member.Name),
                    nameof(parameters)),
            };
            if (value is not null)
            {
                pairs.Add(new KeyValuePair<string, string>(member.Name, value));
            }
        }
        return pairs;
    }

    private static class TypeInfoCache<T>
    {
        internal static readonly JsonTypeInfo<T> Value = (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));
    }
}
