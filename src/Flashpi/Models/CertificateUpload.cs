using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Flashpi.Models;

/// <summary>
/// A .p12 certificate file (firma electrónica) and its password, for
/// <c>Issuers.Certificates.UploadAsync</c> and <c>IssuerOnboardings.CreateAsync</c>. Its
/// <see cref="ToString"/> never shows the file or the password.
/// </summary>
public sealed class CertificateUpload
{
    /// <summary>A .p12 from its bytes.</summary>
    /// <param name="file">The .p12 (PKCS #12) file, up to 1 MiB.</param>
    /// <param name="password">The certificate's password (sent to Flashpi only, never stored in plain text).</param>
    public CertificateUpload(byte[] file, string password)
    {
        Bytes = Internal.Checked.NotNull(file, nameof(file));
        Password = Internal.Checked.NotNull(password, nameof(password));
    }

    /// <summary>
    /// A .p12 from a readable stream, read from its position to its end once, before the first
    /// attempt (so every retry sends the same bytes). The SDK does not dispose it.
    /// </summary>
    /// <param name="file">The .p12 (PKCS #12) file, up to 1 MiB.</param>
    /// <param name="password">The certificate's password (sent to Flashpi only, never stored in plain text).</param>
    public CertificateUpload(Stream file, string password)
    {
        Stream = Internal.Checked.NotNull(file, nameof(file));
        Password = Internal.Checked.NotNull(password, nameof(password));
        if (!file.CanRead)
        {
            throw new ArgumentException("The certificate stream cannot be read", nameof(file));
        }
    }

    /// <summary>The file name sent with it (default <c>certificate.p12</c>): printable ASCII without quotes.</summary>
    public string? FileName { get; init; }

    internal byte[]? Bytes { get; }

    internal Stream? Stream { get; }

    internal string Password { get; }

    /// <summary>The upload, without the file or the password.</summary>
    public override string ToString() =>
        Bytes is not null
            ? $"CertificateUpload(FileName={FileName ?? "certificate.p12"}, {Bytes.Length} bytes)"
            : $"CertificateUpload(FileName={FileName ?? "certificate.p12"}, stream)";
}

/// <summary>Which XML <c>Documents.GetXmlAsync</c> returned (the <c>Flashpi-Xml-Kind</c> header).</summary>
public enum DocumentXmlKind
{
    /// <summary>The XML the SRI authorized, with its authorization.</summary>
    Authorized,

    /// <summary>The signed XML, before the SRI's authorization.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "The API's name of the stage.")]
    Signed,

    /// <summary>The XML before it was signed.</summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "The API's name of the stage.")]
    Unsigned,
}

/// <summary>The XML of a document and its stage.</summary>
/// <param name="Xml">The XML.</param>
/// <param name="Kind">Its stage, or null when the API did not say.</param>
public sealed record DocumentXml(string Xml, DocumentXmlKind? Kind);
