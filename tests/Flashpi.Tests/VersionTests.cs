using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Flashpi.Tests;

public sealed class VersionTests
{
    [Fact]
    public void The_user_agent_version_matches_the_package()
    {
        var project = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Flashpi", "Flashpi.csproj"));
        var version = Regex.Match(project, "<Version>([^<]+)</Version>").Groups[1].Value;
        Assert.Equal(Internal.SdkVersion.Value, version);
    }

    [Fact]
    public void The_assembly_is_strong_named_with_the_repository_key() =>
        Assert.Equal([0xef, 0x9a, 0xa8, 0x17, 0x23, 0x2f, 0x07, 0x1d], typeof(FlashpiClient).Assembly.GetName().GetPublicKeyToken());

    internal static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Flashpi.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("Run the tests inside the repository");
    }
}
