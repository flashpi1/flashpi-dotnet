using System;
using System.IO;
using System.Threading.Tasks;
using Flashpi.Tools.OpenApi;

namespace Flashpi.Tools;

/// <summary>The repository's tooling: keeps the generated models and the README examples in step.</summary>
internal static class Program
{
    private const string Usage = """
        Usage:
          dotnet run --project tools/Flashpi.Tools -- openapi sync|generate|check
          dotnet run --project tools/Flashpi.Tools -- readme generate|check
          dotnet run --project tools/Flashpi.Tools -- package check <directory> [version]
          dotnet run --project tools/Flashpi.Tools -- coverage check <directory> [percent]
        """;

    public static async Task<int> Main(string[] args)
    {
        if (args.Length is 3 or 4 && args[0] is "package" or "coverage" && args[1] == "check")
        {
            try
            {
                return args[0] == "package"
                    ? Package.PackageCommand.Check(args[2], args.Length == 4 ? args[3] : null)
                    : Coverage.CoverageCommand.Check(args[2], args.Length == 4 ? args[3] : "95");
            }
            catch (GenerationException error)
            {
                await Console.Error.WriteLineAsync($"error: {error.Message}").ConfigureAwait(false);
                return 1;
            }
        }
        if (args.Length != 2)
        {
            await Console.Error.WriteLineAsync(Usage).ConfigureAwait(false);
            return 2;
        }
        try
        {
            var root = Repository.FindRoot(Directory.GetCurrentDirectory());
            return (args[0], args[1]) switch
            {
                ("openapi", "sync") => await ContractCommand.SyncAsync(root).ConfigureAwait(false),
                ("openapi", "generate") => ContractCommand.Generate(root, check: false),
                ("openapi", "check") => ContractCommand.Generate(root, check: true),
                ("readme", "generate") => Readme.ReadmeCommand.Run(root, check: false),
                ("readme", "check") => Readme.ReadmeCommand.Run(root, check: true),
                _ => await PrintUsageAsync().ConfigureAwait(false),
            };
        }
        catch (GenerationException error)
        {
            await Console.Error.WriteLineAsync($"error: {error.Message}").ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> PrintUsageAsync()
    {
        await Console.Error.WriteLineAsync(Usage).ConfigureAwait(false);
        return 2;
    }
}

/// <summary>Paths of the repository.</summary>
internal static class Repository
{
    /// <summary>The directory that holds Flashpi.slnx, from <paramref name="start"/> upwards.</summary>
    public static string FindRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Flashpi.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new GenerationException($"No Flashpi.slnx in {start} or above it: run the tool inside the repository");
    }

    /// <summary>Writes text with LF line endings and no byte order mark.</summary>
    public static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ReplaceLineEndings("\n"), new System.Text.UTF8Encoding(false));
    }
}
