using System;

namespace Flashpi;

/// <summary>
/// The language of the API's messages: <see cref="English"/> (the default) or
/// <see cref="Spanish"/>. Error codes are the same in both. Any other text is sent as it is, as the
/// <c>Accept-Language</c> header.
/// </summary>
public readonly struct Language : IEquatable<Language>
{
    private readonly string? _value;

    /// <summary>A language as the <c>Accept-Language</c> header writes it.</summary>
    /// <param name="value">For example <c>es</c>.</param>
    public Language(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary><c>en</c>: English.</summary>
    public static Language English { get; } = new("en");

    /// <summary><c>es</c>: Spanish.</summary>
    public static Language Spanish { get; } = new("es");

    /// <summary>The value of the header (empty for the default value, which sends none).</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Whether both values are the same.</summary>
    public static bool operator ==(Language left, Language right) => left.Equals(right);

    /// <summary>Whether the values differ.</summary>
    public static bool operator !=(Language left, Language right) => !left.Equals(right);

    /// <summary>The language of a text such as <c>es</c>; null is the default value, which sends none.</summary>
    public static implicit operator Language(string? value) => FromString(value);

    /// <summary>The language of a text such as <c>es</c>; null is the default value, which sends none.</summary>
    public static Language FromString(string? value) => value is null ? default : new(value);

    /// <summary>Whether both values are the same (ordinal comparison).</summary>
    public bool Equals(Language other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <summary>Whether the object is the same value.</summary>
    public override bool Equals(object? obj) => obj is Language other && Equals(other);

    /// <summary>A hash code of the value.</summary>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <summary>The value of the header.</summary>
    public override string ToString() => Value;
}
