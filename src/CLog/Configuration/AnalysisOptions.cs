using System.Text.RegularExpressions;

namespace CLog.Configuration;

/// <summary>
/// Step two: an error judged ANALYZE is read against the code it came from and filed as a
/// GitHub issue. Off until it is configured, so step one runs on its own as before.
/// </summary>
public sealed partial class AnalysisOptions
{
    public bool Enabled { get; set; }

    /// <summary>A local clone of the repository the errors come from. Read through git, never modified.</summary>
    public string RepoPath { get; set; } = "";

    /// <summary>Only stack frames in this namespace are looked up; the rest is framework and library code.</summary>
    public string NamespacePrefix { get; set; } = "CSign.";

    /// <summary>The event property naming the commit the failing build was made from. HEAD is read when it is absent.</summary>
    public string CommitProperty { get; set; } = "CommitHash";

    public int TopFrames { get; set; } = 3;

    /// <summary>Lines read on each side of the line a frame points at.</summary>
    public int ContextLines { get; set; } = 40;

    public int MaxFiles { get; set; } = 4;
    public int MaxLines { get; set; } = 400;

    public string Model { get; set; } = "mistral";

    /// <summary>The instruction. The error, the code and the knowledge base follow it.</summary>
    public string Prompt { get; set; } =
        "Här är ett fel och koden det uppstod i. Ange trolig orsak (1–3 meningar), var i koden (fil:rad), "
        + "och ett lösningsförslag. Svara i markdown.";

    /// <summary>
    /// The least time between two "seen again" comments on one issue. An error that keeps
    /// happening is fetched every cycle; without this its issue would get a comment every cycle.
    /// </summary>
    public int RecurrenceCommentMinutes { get; set; } = 1440;

    public GitHubOptions GitHub { get; set; } = new();

    /// <summary>What is missing for step two to run, or null when it is off or fully configured.</summary>
    public string? Problem()
    {
        if (!Enabled)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(RepoPath))
        {
            return "CLog:Analysis:RepoPath is not set";
        }

        if (!RepositoryName().IsMatch(GitHub.Repository))
        {
            return "CLog:Analysis:GitHub:Repository must be \"owner/name\"";
        }

        return string.IsNullOrWhiteSpace(GitHub.Token)
            ? "CLog:Analysis:GitHub:Token is not set; supply it through user-secrets or the environment"
            : null;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
    private static partial Regex RepositoryName();
}

public sealed class GitHubOptions
{
    public string ApiUrl { get; set; } = "https://api.github.com";

    /// <summary>The repository issues are filed in, as "owner/name".</summary>
    public string Repository { get; set; } = "";

    /// <summary>A token allowed to create issues. Supply via environment variable or user-secrets, never in a committed file.</summary>
    public string Token { get; set; } = "";

    public string Label { get; set; } = "ai-triage";
}
