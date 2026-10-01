using Microsoft.Extensions.Logging;
using CLog.Configuration;
using CLog.Knowledge;
using CLog.Model;
using CLog.Ollama;

namespace CLog.Triage;

/// <summary>
/// Judges an error with two yes/no questions instead of one three-way choice: is it in the
/// knowledge base, and if not, is it a failed call from outside. A small model answers yes or
/// no far more reliably than it picks between NOISE, KNOWN and ANALYZE.
/// </summary>
public sealed class TwoStepJudge(IOllamaClient ollama, TriageOptions options, ILogger logger)
{
    private enum Answer
    {
        Yes,
        No,
        Unclear,
        Unreachable
    }

    private static readonly TriageVerdict UnclearAnswer = new(
        Verdict.Analyze,
        "The model did not answer yes or no; flagged for analysis to be safe.",
        null);

    /// <summary>
    /// Returns null when the model could not be reached. An answer that is neither yes nor no
    /// is ANALYZE: when in doubt a human looks.
    /// </summary>
    public async Task<TriageVerdict?> JudgeAsync(
        string errorReport,
        string knownErrors,
        CancellationToken cancellationToken = default)
    {
        var entries = KnownErrors.Parse(knownErrors);
        if (entries.Count > 0)
        {
            switch (await AskAsync(KnownPrompt(entries, errorReport), "known error", cancellationToken))
            {
                case Answer.Unreachable:
                    return null;
                case Answer.Unclear:
                    return UnclearAnswer;
                case Answer.Yes:
                    return await KnownVerdictAsync(entries, errorReport, cancellationToken);
            }
        }

        return await AskAsync(NoisePrompt(errorReport), "outside call", cancellationToken) switch
        {
            Answer.Unreachable => null,
            Answer.Unclear => UnclearAnswer,
            Answer.Yes => new TriageVerdict(
                Verdict.Noise,
                "A failed call from outside rather than a fault in our code.",
                null),
            _ => new TriageVerdict(
                Verdict.Analyze,
                "Not a known error and not a failed call from outside.",
                null)
        };
    }

    /// <summary>
    /// The solution is read from the knowledge base, so it has to be clear which entry matched.
    /// With several entries each is asked about on its own; if none is confirmed the model has
    /// contradicted itself, and that is doubt, not a known error.
    /// </summary>
    private async Task<TriageVerdict> KnownVerdictAsync(
        IReadOnlyList<KnownErrorEntry> entries,
        string errorReport,
        CancellationToken cancellationToken)
    {
        var match = entries.Count == 1 ? entries[0] : null;

        for (var i = 0; match is null && i < entries.Count; i++)
        {
            var answer = await ollama.AskAsync(KnownPrompt([entries[i]], errorReport), cancellationToken);
            if (YesNoParser.TryParse(answer, out var yes) && yes)
            {
                match = entries[i];
            }
        }

        if (match is null)
        {
            logger.LogWarning(
                "The model said the error is in the knowledge base but confirmed none of its {Count} entries; flagging the error for analysis",
                entries.Count);
            return new TriageVerdict(
                Verdict.Analyze,
                "The model called this a known error but matched no single entry; flagged for analysis to be safe.",
                null);
        }

        return new TriageVerdict(Verdict.Known, $"Matches the known error \"{match.Title}\".", match.Solution);
    }

    private async Task<Answer> AskAsync(string prompt, string question, CancellationToken cancellationToken)
    {
        var raw = await ollama.AskAsync(prompt, cancellationToken);
        if (raw is null)
        {
            return Answer.Unreachable;
        }

        if (YesNoParser.TryParse(raw, out var yes))
        {
            return yes ? Answer.Yes : Answer.No;
        }

        logger.LogWarning(
            "The model did not answer yes or no to the {Question} question; flagging the error for analysis. Raw answer: {Answer}",
            question,
            raw.Length <= 200 ? raw : raw[..200] + "...");
        return Answer.Unclear;
    }

    private string KnownPrompt(IReadOnlyList<KnownErrorEntry> entries, string errorReport) =>
        Fill(
            options.KnownPrompt,
            (TriageOptions.KnowledgePlaceholder, string.Join("\n\n", entries.Select(e => e.Text))),
            (TriageOptions.ErrorPlaceholder, errorReport.Trim()));

    private string NoisePrompt(string errorReport) =>
        Fill(options.NoisePrompt, (TriageOptions.ErrorPlaceholder, errorReport.Trim()));

    /// <summary>
    /// A template edited in config may have lost a placeholder. The text is appended then, so
    /// a typo cannot leave the model answering a question about nothing.
    /// </summary>
    private static string Fill(string template, params (string Placeholder, string Value)[] parts)
    {
        foreach (var (placeholder, value) in parts)
        {
            template = template.Contains(placeholder, StringComparison.Ordinal)
                ? template.Replace(placeholder, value, StringComparison.Ordinal)
                : template.TrimEnd() + "\n\n" + value;
        }

        return template;
    }
}
