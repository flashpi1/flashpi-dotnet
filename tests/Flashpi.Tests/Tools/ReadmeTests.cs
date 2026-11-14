using System.IO;
using System.Linq;
using Flashpi.Tools.Readme;
using Xunit;

namespace Flashpi.Tests.Tools;

public sealed class ReadmeTests
{
    [Fact]
    public void The_committed_examples_match_the_readmes()
    {
        var root = VersionTests.RepositoryRoot();
        var expected = ReadmeCommand.Generate(root);
        var directory = Path.Combine(root, ReadmeCommand.ExamplesPath);
        var current = Directory.GetFiles(directory, "*.cs").ToDictionary(file => Path.GetFileName(file), File.ReadAllText);
        Assert.Equal(expected.Keys.Order(), current.Keys.Order());
        foreach (var (name, text) in expected)
        {
            Assert.Equal(text, current[name]);
        }
    }

    [Fact]
    public void Both_readmes_have_the_same_examples()
    {
        var root = VersionTests.RepositoryRoot();
        var spanish = ReadmeCommand.Blocks(File.ReadAllText(Path.Combine(root, "README.md")));
        var english = ReadmeCommand.Blocks(File.ReadAllText(Path.Combine(root, "README.en.md")));
        Assert.Equal(spanish.Count, english.Count);
    }

    [Fact]
    public void Reads_only_csharp_blocks()
    {
        var blocks = ReadmeCommand.Blocks("text\n```bash\nls\n```\n\n```csharp\nvar a = 1;\n```\n");
        Assert.Equal(["var a = 1;\n"], blocks);
    }
}
