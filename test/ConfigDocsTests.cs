using Chronology.Tools.ConfigDocGen;
using Xunit;

namespace Chronology.Tests;

public class ConfigDocsTests
{
    [Fact]
    public void ReadmeConfigSection_MatchesSchema()
    {
        var path = RepoPath("README.md");
        Assert.True(File.Exists(path), "README.md not found. Run `dotnet run --project tools/ConfigDocGen`.");
        var readme = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.Equal(readme, ConfigDocGenerator.Splice(readme));
    }

    private static string RepoPath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Chronology.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, name);
    }
}
