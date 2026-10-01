using System.Text;
using CLog.Model;

namespace CLog.Triage;

/// <summary>Turns an error group into the text the model reads and the record we write out.</summary>
public static class TriageReports
{
    /// <summary>Everything the model is told about one error. Scrubbed before it gets here.</summary>
    public static string BuildErrorReport(ErrorGroup group)
    {
        var sample = group.Sample;
        var exception = ExceptionInfo.Parse(sample.Exception);
        var report = new StringBuilder();

        report.AppendLine($"Fingerprint: {group.Fingerprint.Hash}");
        report.AppendLine($"Occurrences in this batch: {group.Count}");

        if (!string.IsNullOrEmpty(group.Fingerprint.ExceptionType))
        {
            report.AppendLine($"Exception type: {group.Fingerprint.ExceptionType}");
        }

        if (!string.IsNullOrEmpty(exception.Message))
        {
            report.AppendLine($"Exception message: {exception.Message}");
        }

        report.AppendLine($"Message: {sample.RenderedMessage}");

        if (!string.IsNullOrEmpty(group.Fingerprint.TopFrame))
        {
            report.AppendLine($"Top application frame: {group.Fingerprint.TopFrame}");
        }

        if (exception.Frames.Count > 0)
        {
            report.AppendLine("Stack (first frames):");
            foreach (var frame in exception.Frames.Take(8))
            {
                report.AppendLine("  at " + frame);
            }
        }

        var properties = sample.Properties
            .Where(p => !string.IsNullOrEmpty(p.Value))
            .Take(15)
            .ToList();

        if (properties.Count > 0)
        {
            report.AppendLine("Properties:");
            foreach (var (key, value) in properties)
            {
                var text = value!.Length > 200 ? value[..200] + "..." : value;
                report.AppendLine($"  {key}: {text}");
            }
        }

        return report.ToString();
    }

    public static TriageRecord Filtered(ErrorGroup group, IgnoreRule rule, DateTimeOffset at) =>
        Base(group, at) with
        {
            Outcome = TriageOutcome.FilteredByRule,
            RuleId = string.IsNullOrEmpty(rule.Id) ? "(unnamed rule)" : rule.Id,
            Reason = rule.Reason
        };

    public static TriageRecord Judged(ErrorGroup group, TriageVerdict? verdict, DateTimeOffset at) =>
        verdict is null
            ? Base(group, at) with
            {
                Outcome = TriageOutcome.JudgementFailed,
                Reason = "the model gave no usable answer; the error is reported again if it recurs"
            }
            : Base(group, at) with
            {
                Outcome = TriageOutcome.Judged,
                Verdict = verdict.Verdict,
                Reason = verdict.Reason,
                KnownSolution = verdict.KnownSolution
            };

    private static TriageRecord Base(ErrorGroup group, DateTimeOffset at) => new()
    {
        At = at,
        Fingerprint = group.Fingerprint.Hash,
        ExceptionType = group.Fingerprint.ExceptionType,
        Template = group.Fingerprint.NormalizedTemplate,
        TopFrame = group.Fingerprint.TopFrame,
        SampleEventId = group.Sample.Id,
        OccurrenceCount = group.Count
    };
}
