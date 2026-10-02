using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CLog.Configuration;
using CLog.Storage;
using CLog.Triage;

namespace CLog.Analysis;

/// <summary>
/// Turns an error judged ANALYZE into an issue: shown with its code, or analysed against it
/// in Model mode, filed once, and commented on when the error comes back.
/// </summary>
public sealed class IssueReporter(
    Analyst analyst,
    IIssueTracker tracker,
    IFingerprintStore store,
    IOptions<CLogOptions> options,
    ILogger<IssueReporter> logger)
{
    private const int MaxTitleLength = 120;
    private const int MaxStackLines = 60;

    private readonly AnalysisMode _mode = options.Value.Analysis.Mode;

    private readonly TimeSpan _commentInterval =
        TimeSpan.FromMinutes(Math.Max(1, options.Value.Analysis.RecurrenceCommentMinutes));

    /// <summary>
    /// Analyses the error and files it. Returns the issue number, or null when the issue could
    /// not be filed — the caller then leaves the error unseen so it is tried again.
    /// </summary>
    public async Task<int?> FileAsync(
        ErrorGroup group,
        string errorReport,
        string knownErrors,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var analysis = await analyst.AnalyzeAsync(group.Sample, errorReport, knownErrors, cancellationToken);

        var body = _mode == AnalysisMode.Context
            ? ContextBody(group, errorReport, analysis.Code)
            : Body(errorReport, analysis);

        var number = await tracker.CreateIssueAsync(Title(group), body, cancellationToken);
        if (number is null)
        {
            logger.LogWarning(
                "[{Fingerprint}] could not be filed as an issue; it stays unseen and is tried again next cycle",
                group.Fingerprint.Hash);
            return null;
        }

        // Saved at once and not cancellable: an issue that exists but is not remembered would
        // be filed a second time.
        await store.SaveIssueAsync(group.Fingerprint.Hash, number.Value, now, CancellationToken.None);
        logger.LogInformation("[{Fingerprint}] filed as issue #{Issue}", group.Fingerprint.Hash, number);
        return number;
    }

    /// <summary>Tells the error's issue that it has happened again, at most once per comment interval.</summary>
    public async Task CommentIfDueAsync(ErrorGroup group, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var issue = await store.GetIssueAsync(group.Fingerprint.Hash, cancellationToken);
        if (issue is null || now - issue.LastReportedAt < _commentInterval)
        {
            return;
        }

        var comment = $"Seen again: {group.Count} occurrence(s) in the fetch at {now:u}.";
        if (!await tracker.AddCommentAsync(issue.Number, comment, cancellationToken))
        {
            // Left as it was, so the next cycle that sees the error tries again.
            return;
        }

        await store.SaveIssueAsync(group.Fingerprint.Hash, issue.Number, now, CancellationToken.None);
        logger.LogInformation("[{Fingerprint}] recurred; commented on issue #{Issue}", group.Fingerprint.Hash, issue.Number);
    }

    private static string Title(ErrorGroup group)
    {
        var what = string.IsNullOrWhiteSpace(group.Fingerprint.NormalizedTemplate)
            ? group.Sample.RenderedMessage
            : group.Fingerprint.NormalizedTemplate;
        var title = string.IsNullOrEmpty(group.Fingerprint.ExceptionType)
            ? what
            : $"{group.Fingerprint.ExceptionType}: {what}";

        title = title.ReplaceLineEndings(" ").Trim();
        return title.Length <= MaxTitleLength ? title : title[..MaxTitleLength] + "…";
    }

    /// <summary>
    /// Context mode: the error, its stack trace and the code around each application frame.
    /// It points at the code and interprets nothing, so there is no analysis section and
    /// nothing to disclaim.
    /// </summary>
    private static string ContextBody(ErrorGroup group, string errorReport, CodeContext code)
    {
        var body = new StringBuilder();

        // Fenced, so nothing in a log line is read as markdown or as a mention of a GitHub user.
        body.Append("## Error\n\n````text\n").Append(errorReport.Trim()).Append("\n````\n\n");

        if (!string.IsNullOrWhiteSpace(group.Sample.Exception))
        {
            body.Append("## Stack trace\n\n````text\n").Append(StackTrace(group.Sample.Exception)).Append("\n````\n\n");
        }

        body.Append("## Kod runt felet\n\n");
        if (code.Excerpts.Count == 0)
        {
            body.Append("_No file in the repository could be matched to the stack trace._\n\n");
        }

        foreach (var excerpt in code.Excerpts)
        {
            body.Append($"**`{excerpt.Path}`** line {excerpt.FocusLine}, in `{excerpt.Frame}`\n\n")
                .Append("````text\n").Append(excerpt.Text.TrimEnd('\n')).Append("\n````\n\n");
        }

        body.Append("---\n_Filed automatically by CLog.");
        if (code.Commit is { } commit)
        {
            body.Append(commit.FromEvent
                ? $" Code read at commit `{commit.Hash}`, the build the error came from."
                : $" Code read at commit `{commit.Hash}`, HEAD of the local clone, which may differ from the build the error came from.");
        }

        return body.Append("_\n").ToString();
    }

    /// <summary>The scrubbed exception text, cut off where a deep trace would crowd the code out of the issue.</summary>
    private static string StackTrace(string exception)
    {
        var lines = exception.ReplaceLineEndings("\n").Trim().Split('\n');
        return lines.Length <= MaxStackLines
            ? string.Join('\n', lines)
            : string.Join('\n', lines.Take(MaxStackLines)) + $"\n... ({lines.Length - MaxStackLines} more line(s))";
    }

    private static string Body(string errorReport, AnalysisResult analysis)
    {
        var body = new StringBuilder();

        // Fenced, so nothing in a log line is read as markdown or as a mention of a GitHub user.
        body.Append("## Error\n\n````text\n").Append(errorReport.Trim()).Append("\n````\n\n## Analysis\n\n");
        body.Append(analysis.Text ?? "_The model gave no analysis for this error._").Append("\n\n");

        if (analysis.Code.Excerpts.Count > 0)
        {
            var commit = analysis.Code.Commit!;
            body.Append("## Code read\n\n");
            foreach (var excerpt in analysis.Code.Excerpts)
            {
                body.Append($"- `{excerpt.Path}` lines {excerpt.StartLine}–{excerpt.EndLine}, for `{excerpt.Frame}`\n");
            }

            body.Append(commit.FromEvent
                ? $"\nRead at commit `{commit.Hash}`, the build the error came from.\n\n"
                : $"\nRead at `{commit.Hash}` (HEAD of the local clone), which may differ from the build the error came from.\n\n");
        }

        body.Append("---\n_Filed automatically by CLog. The analysis is a model's suggestion, not a verified diagnosis._\n");
        return body.ToString();
    }
}
