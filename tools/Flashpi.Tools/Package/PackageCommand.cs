using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Flashpi.Tools.OpenApi;

namespace Flashpi.Tools.Package;

/// <summary>
/// Checks what NuGet would get: the files of the .nupkg in a directory, file by file, its
/// dependencies by target, and the symbols package. <c>package check dist [version]</c>.
/// </summary>
internal static class PackageCommand
{
    private static readonly string[] Files =
    [
        "Flashpi.nuspec",
        "README.md",
        "lib/net10.0/Flashpi.dll",
        "lib/net10.0/Flashpi.xml",
        "lib/netstandard2.0/Flashpi.dll",
        "lib/netstandard2.0/Flashpi.xml",
    ];

    private static readonly string[] SymbolFiles =
    [
        "Flashpi.nuspec",
        "lib/net10.0/Flashpi.pdb",
        "lib/netstandard2.0/Flashpi.pdb",
    ];

    /// <summary>The dependencies of each target: net10.0 has none.</summary>
    private static readonly Dictionary<string, string[]> Dependencies = new(StringComparer.Ordinal)
    {
        ["net10.0"] = [],
        [".NETStandard2.0"] = ["Microsoft.Bcl.AsyncInterfaces", "System.Text.Json"],
    };

    public static int Check(string directory, string? version)
    {
        var problems = new List<string>();
        var packages = Directory.GetFiles(directory, "Flashpi.*.nupkg");
        var symbols = Directory.GetFiles(directory, "Flashpi.*.snupkg");
        if (packages.Length != 1 || symbols.Length != 1)
        {
            throw new GenerationException($"{directory} must hold one Flashpi .nupkg and one .snupkg");
        }
        var nupkg = packages[0];
        if (version is not null && Path.GetFileName(nupkg) != $"Flashpi.{version}.nupkg")
        {
            problems.Add($"{Path.GetFileName(nupkg)} is not version {version}");
        }
        problems.AddRange(CheckEntries(nupkg, Files));
        problems.AddRange(CheckEntries(symbols[0], SymbolFiles));
        problems.AddRange(CheckNuspec(nupkg, version));
        foreach (var problem in problems)
        {
            Console.Error.WriteLine($"error: {problem}");
        }
        if (problems.Count == 0)
        {
            Console.WriteLine($"{Path.GetFileName(nupkg)} and its symbols hold what NuGet should get");
        }
        return problems.Count == 0 ? 0 : 1;
    }

    private static IEnumerable<string> CheckEntries(string path, string[] expected)
    {
        using var archive = ZipFile.OpenRead(path);
        var entries = archive.Entries
            .Select(entry => entry.FullName)
            .Where(name => !name.StartsWith("_rels/", StringComparison.Ordinal)
                && !name.StartsWith("package/", StringComparison.Ordinal)
                && name != "[Content_Types].xml")
            .ToHashSet(StringComparer.Ordinal);
        foreach (var missing in expected.Where(name => !entries.Contains(name)))
        {
            yield return $"{Path.GetFileName(path)} lacks {missing}";
        }
        foreach (var extra in entries.Where(name => !expected.Contains(name)).Order(StringComparer.Ordinal))
        {
            yield return $"{Path.GetFileName(path)} holds an unexpected {extra}";
        }
    }

    private static IEnumerable<string> CheckNuspec(string path, string? version)
    {
        using var archive = ZipFile.OpenRead(path);
        using var stream = archive.GetEntry("Flashpi.nuspec")!.Open();
        var nuspec = XDocument.Load(stream);
        XNamespace ns = nuspec.Root!.Name.Namespace;
        var metadata = nuspec.Root.Element(ns + "metadata")!;
        if (metadata.Element(ns + "id")?.Value != "Flashpi")
        {
            yield return "the package id is not Flashpi";
        }
        if (version is not null && metadata.Element(ns + "version")?.Value != version)
        {
            yield return $"the nuspec is not version {version}";
        }
        if (metadata.Element(ns + "license")?.Value != "MIT" || metadata.Element(ns + "readme")?.Value != "README.md")
        {
            yield return "the license must be MIT and the readme README.md";
        }
        var groups = metadata.Element(ns + "dependencies")?.Elements(ns + "group").ToList() ?? [];
        var found = groups.ToDictionary(
            group => group.Attribute("targetFramework")?.Value ?? "",
            group => group.Elements(ns + "dependency").Select(dependency => dependency.Attribute("id")!.Value).Order(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
        if (!found.Keys.Order(StringComparer.Ordinal).SequenceEqual(Dependencies.Keys.Order(StringComparer.Ordinal)))
        {
            yield return $"the dependency groups are {string.Join(", ", found.Keys)}";
            yield break;
        }
        foreach (var (target, expected) in Dependencies)
        {
            if (!found[target].SequenceEqual(expected))
            {
                yield return $"{target} depends on {string.Join(", ", found[target])}, not {string.Join(", ", expected)}";
            }
        }
    }
}
