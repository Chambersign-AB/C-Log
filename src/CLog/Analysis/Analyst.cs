using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CLog.Configuration;
using CLog.Knowledge;
using CLog.Model;

namespace CLog.Analysis;

/// <summary>
/// Step two for one error: gathers the code it came from and, in Model mode, asks a model
/// for the likely cause, the place in the code and a fix. Only ever given the scrubbed event.
/// </summary>
public sealed class Analyst(
    CodeContextResolver resolver,
    IAnalysisModel model,
    IOptions<CLogOptions> options,
    ILogger<Analyst> logger)
{
    private readonly AnalysisOptions _options = options.Value.Analysis;

    /// <param name="sample">The scrubbed event; its stack trace locates the code.</param>
    /// <param name="errorReport">The scrubbed report the triage model was given.</param>
    public async Task<AnalysisResult> AnalyzeAsync(
        SeqEvent sample,
        string errorReport,
        string knownErrors,
        CancellationToken cancellationToken = default)
    {
        var code = await resolver.ResolveAsync(sample, cancellationToken);
        if (_options.Mode == AnalysisMode.Context)
        {
            // The issue points at the code and leaves the reading of it to a person.
            return new AnalysisResult(null, code);
        }

        var answer = await model.CompleteAsync(BuildPrompt(errorReport, code, knownErrors), cancellationToken);

        if (string.IsNullOrWhiteSpace(answer))
        {
            logger.LogWarning("The analysis model gave no answer; the error is filed without an analysis");
            return new AnalysisResult(null, code);
        }

        return new AnalysisResult(answer.Trim(), code);
    }

    internal string BuildPrompt(string errorReport, CodeContext code, string knownErrors)
    {
        var prompt = new StringBuilder();
        prompt.Append(_options.Prompt.Trim()).Append("\n\n# Fel\n").Append(errorReport.Trim()).Append("\n\n# Kod\n");

        if (code.Excerpts.Count == 0)
        {
            prompt.Append("(ingen kod kunde hittas för detta fel)\n");
        }

        foreach (var excerpt in code.Excerpts)
        {
            prompt.Append($"## {excerpt.Path} (rad {excerpt.StartLine}–{excerpt.EndLine}, felet på rad {excerpt.FocusLine})\n")
                .Append($"Stackram: {excerpt.Frame}\n")
                .Append(excerpt.Text)
                .Append('\n');
        }

        var entries = KnownErrors.Parse(knownErrors);
        prompt.Append("\n# Kunskapsbas\n")
            .Append(entries.Count == 0 ? "(tom)" : string.Join("\n\n", entries.Select(e => e.Text)))
            .Append('\n');

        return prompt.ToString();
    }
}
