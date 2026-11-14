using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Flashpi.Models;

namespace Flashpi.Internal;

/// <summary>
/// The multipart body of a .p12 upload: a <c>password</c> field, then a <c>file</c> part. The file
/// is read once, so every attempt sends the same bytes.
/// </summary>
internal sealed class CertificateForm
{
    /// <summary>The largest .p12 the API accepts (413 <c>payload_too_large</c> above it).</summary>
    internal const int MaxBytes = 1_048_576;

    private const string DefaultFileName = "certificate.p12";
    private const string FileContentType = "application/x-pkcs12";

    private readonly byte[] _file;
    private readonly string _password;
    private readonly string _fileName;

    private CertificateForm(byte[] file, string password, string fileName)
    {
        _file = file;
        _password = password;
        _fileName = fileName;
    }

    /// <summary>
    /// Reads the upload, before anything is sent: a file that is empty or over 1 MiB throws
    /// <see cref="FlashpiException"/> (a stream is read only up to one byte past that), a file name
    /// that cannot be sent as it is throws <see cref="ArgumentException"/>.
    /// </summary>
    internal static async Task<CertificateForm> ReadAsync(CertificateUpload upload, CancellationToken cancellationToken)
    {
        Checked.NotNull(upload, nameof(upload));
        var fileName = upload.FileName ?? DefaultFileName;
        foreach (var character in fileName)
        {
            if (character is < ' ' or > '~' or '"' or '\\')
            {
                throw new ArgumentException("The certificate FileName must be printable ASCII without quotes or backslashes", nameof(upload));
            }
        }
        var file = upload.Bytes ?? await ReadStreamAsync(upload, cancellationToken).ConfigureAwait(false);
        if (file.Length == 0)
        {
            throw new FlashpiException("The certificate file is empty");
        }
        if (file.Length > MaxBytes)
        {
            throw new FlashpiException(string.Format(
                CultureInfo.InvariantCulture, "The certificate file has {0} bytes; the API accepts up to {1}", file.Length, MaxBytes));
        }
        return new CertificateForm(file, upload.Password, fileName);
    }

    private static async Task<byte[]> ReadStreamAsync(CertificateUpload upload, CancellationToken cancellationToken)
    {
        var stream = upload.Stream!;
        var buffer = new byte[MaxBytes + 1];
        var size = 0;
        while (size < buffer.Length)
        {
#if NET
            var read = await stream.ReadAsync(buffer.AsMemory(size), cancellationToken).ConfigureAwait(false);
#else
            var read = await stream.ReadAsync(buffer, size, buffer.Length - size, cancellationToken).ConfigureAwait(false);
#endif
            if (read == 0)
            {
                break;
            }
            size += read;
        }
        if (size > MaxBytes)
        {
            throw new FlashpiException(string.Format(
                CultureInfo.InvariantCulture, "The certificate file has more than {0} bytes, the most the API accepts", MaxBytes));
        }
        var file = new byte[size];
        Buffer.BlockCopy(buffer, 0, file, 0, size);
        return file;
    }

    /// <summary>A new body for one attempt.</summary>
    internal HttpContent CreateContent()
    {
        var form = new MultipartFormDataContent();
        var password = new ByteArrayContent(Encoding.UTF8.GetBytes(_password));
        password.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"password\"" };
        form.Add(password);
        var file = new ByteArrayContent(_file);
        file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "\"file\"",
            FileName = $"\"{_fileName}\"",
        };
        file.Headers.ContentType = new MediaTypeHeaderValue(FileContentType);
        form.Add(file);
        return form;
    }

    /// <summary>Only the part names: never the .p12 or its password.</summary>
    public override string ToString() => "CertificateForm(password, file)";
}
