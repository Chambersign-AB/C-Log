using CLog.Configuration;

namespace CLog.Tests;

public class ContentPathsTests
{
    [Fact]
    public void A_relative_path_resolves_against_the_repository_root()
    {
        // The content root when running from source is src/CLog, but knowledge/ lives at
        // the top of the repository.
        var resolved = ContentPaths.Resolve(AppContext.BaseDirectory, "knowledge/rules.json");

        Assert.True(File.Exists(resolved), $"expected the shipped rules file at {resolved}");
    }

    [Fact]
    public void An_absolute_path_is_left_alone()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "clog.db");

        Assert.Equal(absolute, ContentPaths.Resolve(AppContext.BaseDirectory, absolute));
    }

    [Fact]
    public void Outside_a_repository_the_content_root_is_the_base()
    {
        var contentRoot = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

        Assert.Equal(contentRoot, ContentPaths.FindBase(contentRoot));
    }

    [Fact]
    public void A_path_that_does_not_exist_yet_still_resolves_under_the_repository_root()
    {
        // data/ is created on first run, so resolution cannot depend on the file being there.
        var resolved = ContentPaths.Resolve(AppContext.BaseDirectory, "data/triage.jsonl");

        Assert.Equal(
            Path.Combine(ContentPaths.FindBase(AppContext.BaseDirectory), "data", "triage.jsonl"),
            resolved.Replace('/', Path.DirectorySeparatorChar));
    }
}
