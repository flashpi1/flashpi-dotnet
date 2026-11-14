using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using Flashpi.Tools.OpenApi;

namespace Flashpi.Tools.Package;

/// <summary>
/// Checks what NuGet would get: the files of the .nupkg in a directory, file by file, its
/// dependencies by target, the strong-name signature of its assemblies, and the symbols package.
/// <c>package check dist [version]</c>.
/// </summary>
internal static class PackageCommand
{
    private static readonly string[] Files =
    [
        "Flashpi.nuspec",
        "README.md",
        "icon.png",
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

    /// <summary>The public key token of Flashpi.snk, which every build of Flashpi.dll carries.</summary>
    private static readonly byte[] PublicKeyToken = [0xef, 0x9a, 0xa8, 0x17, 0x23, 0x2f, 0x07, 0x1d];

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
        problems.AddRange(CheckSignatures(nupkg));
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

    /// <summary>Every assembly is fully signed (not delay- or public-signed) with Flashpi.snk.</summary>
    private static List<string> CheckSignatures(string path)
    {
        var problems = new List<string>();
        using var archive = ZipFile.OpenRead(path);
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".dll", StringComparison.Ordinal)))
        {
            using var buffer = new MemoryStream();
            using (var stream = entry.Open())
            {
                stream.CopyTo(buffer);
            }
            buffer.Position = 0;
            using var reader = new PEReader(buffer);
            var signed = reader.PEHeaders.CorHeader is { } header && header.Flags.HasFlag(CorFlags.StrongNameSigned);
            var metadata = reader.GetMetadataReader();
            var name = new AssemblyName();
            name.SetPublicKey(metadata.GetBlobBytes(metadata.GetAssemblyDefinition().PublicKey));
            var token = name.GetPublicKeyToken() ?? [];
            if (!signed || !token.SequenceEqual(PublicKeyToken))
            {
                problems.Add($"{entry.FullName} is not strong-named with Flashpi.snk (public key token {Convert.ToHexStringLower(token)})");
            }
        }
        return problems;
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
        if (metadata.Element(ns + "license")?.Value != "MIT"
            || metadata.Element(ns + "readme")?.Value != "README.md"
            || metadata.Element(ns + "icon")?.Value != "icon.png")
        {
            yield return "the license must be MIT, the readme README.md and the icon icon.png";
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
