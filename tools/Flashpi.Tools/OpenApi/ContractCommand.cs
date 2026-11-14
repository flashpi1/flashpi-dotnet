using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Flashpi.Tools.OpenApi;

/// <summary>
/// Keeps the SDK's copy of the Flashpi API contract and the models generated from it in sync.
/// </summary>
/// <remarks>
/// <para><c>openapi sync</c> downloads the public OpenAPI document, then generates;
/// <c>openapi generate</c> regenerates <c>src/Flashpi/Generated/Models.g.cs</c>;
/// <c>openapi check</c> fails when that file is out of date (CI).</para>
/// <para>The source is the public document the docs publish (https://flashpi.com.ec/docs/openapi.json),
/// because the API repository is private. Override it with FLASHPI_OPENAPI_URL, or point
/// FLASHPI_OPENAPI_FILE at a local file (for example a flashpi-api checkout).</para>
/// </remarks>
internal static class ContractCommand
{
    public const string ContractPath = "openapi/openapi.json";
    public const string ModelsPath = "src/Flashpi/Generated/Models.g.cs";
    private const string DefaultUrl = "https://flashpi.com.ec/docs/openapi.json";
    private const int MaxRedirects = 5;

    public static async Task<int> SyncAsync(string root)
    {
        var (source, contract) = await DownloadAsync().ConfigureAwait(false);
        // Fail early on a broken document.
        if (JsonNode.Parse(contract) is not JsonObject parsed || parsed["openapi"] is null || parsed["paths"] is null)
        {
            throw new GenerationException("The downloaded file is not an OpenAPI document");
        }
        Repository.WriteText(Path.Combine(root, ContractPath), contract.EndsWith('\n') ? contract : contract + "\n");
        Console.WriteLine($"Copied {source} to {ContractPath}");
        return Generate(root, check: false);
    }

    public static int Generate(string root, bool check)
    {
        var contract = File.ReadAllText(Path.Combine(root, ContractPath));
        var models = new Generator(contract).Generate();
        var path = Path.Combine(root, ModelsPath);
        if (check)
        {
            var current = File.Exists(path) ? File.ReadAllText(path) : "";
            if (current != models)
            {
                Console.Error.WriteLine(
                    $"{ModelsPath} is out of date. Run: dotnet run --project tools/Flashpi.Tools -- openapi generate");
                return 1;
            }
            Console.WriteLine($"{ModelsPath} matches {ContractPath}");
            return 0;
        }
        Repository.WriteText(path, models);
        Console.WriteLine($"Wrote {ModelsPath}");
        return 0;
    }

    /// <summary>Where the contract came from, and its text. Only HTTPS URLs are fetched, also
    /// through redirects.</summary>
    private static async Task<(string Source, string Contract)> DownloadAsync()
    {
        var file = Environment.GetEnvironmentVariable("FLASHPI_OPENAPI_FILE");
        if (!string.IsNullOrEmpty(file))
        {
            return (file, await File.ReadAllTextAsync(file).ConfigureAwait(false));
        }
        var url = Environment.GetEnvironmentVariable("FLASHPI_OPENAPI_URL");
        var current = new Uri(string.IsNullOrEmpty(url) ? DefaultUrl : url);
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        for (var redirects = 0; ; redirects++)
        {
            if (current.Scheme != Uri.UriSchemeHttps)
            {
                throw new GenerationException($"Refusing to download the contract from a non-HTTPS URL: {current}");
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await http.SendAsync(request).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (status is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                if (redirects >= MaxRedirects)
                {
                    throw new GenerationException($"Too many redirects from {url ?? DefaultUrl}");
                }
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new GenerationException($"{current} answered {status}");
            }
            return (url ?? DefaultUrl, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        }
    }
}
