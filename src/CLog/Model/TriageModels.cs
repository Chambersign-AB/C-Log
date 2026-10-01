namespace CLog.Model;

public enum Verdict
{
    /// <summary>Not worth a human's attention.</summary>
    Noise,

    /// <summary>Already described in known-errors.md.</summary>
    Known,

    /// <summary>New, and worth analysing.</summary>
    Analyze
}

/// <summary>The model's judgement of one error.</summary>
public sealed record TriageVerdict(Verdict Verdict, string Reason, string? KnownSolution);

/// <summary>What happened to one error during a cycle.</summary>
public enum TriageOutcome
{
    /// <summary>A rule in rules.json matched, so the model was never asked.</summary>
    FilteredByRule,

    /// <summary>The model answered.</summary>
    Judged,

    /// <summary>The model could not be reached, or its answer was unusable.</summary>
    JudgementFailed
}

/// <summary>One line of triage.jsonl. Written once per newly seen fingerprint.</summary>
public sealed record TriageRecord
{
    public DateTimeOffset At { get; init; }
    public string Fingerprint { get; init; } = "";
    public string ExceptionType { get; init; } = "";
    public string Template { get; init; } = "";
    public string TopFrame { get; init; } = "";
    public string SampleEventId { get; init; } = "";

    /// <summary>How many events in this cycle shared the fingerprint.</summary>
    public int OccurrenceCount { get; init; }

    public TriageOutcome Outcome { get; init; }
    public Verdict? Verdict { get; init; }
    public string? Reason { get; init; }
    public string? KnownSolution { get; init; }

    /// <summary>The rule that filtered this error, when the outcome is FilteredByRule.</summary>
    public string? RuleId { get; init; }

    /// <summary>The issue filed for this error, when step two is on and the verdict is ANALYZE.</summary>
    public int? IssueNumber { get; init; }
}

/// <summary>What one polling cycle did. Logged as the cycle summary.</summary>
public sealed record TriageCycleResult
{
    public int EventsFetched { get; init; }
    public int Fingerprints { get; init; }
    public int FilteredByRules { get; init; }
    public int Judged { get; init; }
    public int JudgementFailures { get; init; }

    /// <summary>New errors left for the next cycle because the judgement budget ran out.</summary>
    public int Deferred { get; init; }

    /// <summary>Errors whose result could not be written. Left unmarked, so they are reported again.</summary>
    public int Unreported { get; init; }
}
