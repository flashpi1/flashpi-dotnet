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
