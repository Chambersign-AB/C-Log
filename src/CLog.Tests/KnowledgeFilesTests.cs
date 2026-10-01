using CLog.Triage;

namespace CLog.Tests;

/// <summary>
/// The files under knowledge/ are edited by people and read by the service every cycle, so a
/// typo in them is a production problem. These tests keep the shipped files honest.
/// </summary>
public class KnowledgeFilesTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "C-Log.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    private static string KnowledgePath(string file) => Path.Combine(RepositoryRoot(), "knowledge", file);

    [Fact]
    public void The_shipped_rules_file_parses()
    {
        var rules = RuleSet.Load(KnowledgePath("rules.json"));

        Assert.Equal(1, rules.Version);
    }

    [Fact]
    public void The_shipped_rules_file_starts_with_an_empty_ignore_list()
    {
        // Nothing is filtered until somebody decides to filter it.
        Assert.Empty(RuleSet.Load(KnowledgePath("rules.json")).Ignore);
    }

    [Fact]
    public void The_known_errors_template_exists_and_names_its_three_sections()
    {
        var text = File.ReadAllText(KnowledgePath("known-errors.md"));

        Assert.Contains("**Symptom**", text);
        Assert.Contains("**Cause**", text);
        Assert.Contains("**Solution**", text);
    }

    [Fact]
    public void The_known_errors_template_has_no_real_entries_yet()
    {
        // The worked example lives inside an HTML comment, so it does not count as an entry.
        var text = System.Text.RegularExpressions.Regex.Replace(
            File.ReadAllText(KnowledgePath("known-errors.md")),
            "<!--.*?-->",
            "",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        var headings = text.Split('\n').Count(line => line.StartsWith("## ", StringComparison.Ordinal));

        Assert.Equal(1, headings);
    }
}
