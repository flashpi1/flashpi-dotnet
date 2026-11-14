namespace Flashpi.Examples;

/// <summary>The names the README blocks take from an earlier block, or from the reader's code.</summary>
#pragma warning disable IDE1006, SA1306, CA1051 // The README's own lowercase names.
internal abstract class ReadmeContext
{
    protected readonly FlashpiClient flashpi = null!;
    protected readonly Document invoice = null!;
    protected readonly CreateInvoice body = null!;
    protected readonly string documentId = "";
    protected readonly Order order = new("42");
    protected readonly string[] args = [];

    /// <summary>The reader's own retry queue.</summary>
    protected static void SaveForRetry(CreateInvoice body, string idempotencyKey)
    {
    }

    protected sealed record Order(string Id);
}
