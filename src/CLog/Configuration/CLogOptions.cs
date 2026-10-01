namespace CLog.Configuration;

/// <summary>Root configuration, bound from the "CLog" section.</summary>
public sealed class CLogOptions
{
    public const string SectionName = "CLog";

    public SeqOptions Seq { get; set; } = new();
    public OllamaOptions Ollama { get; set; } = new();
    public TriageOptions Triage { get; set; } = new();

    /// <summary>How often the watcher polls Seq.</summary>
    public int IntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Upper bound on errors judged per cycle, so a burst of new errors cannot flood Ollama.
    /// In TwoStep mode one judgement is two questions, more when a known error is pinned down.
    /// </summary>
    public int MaxJudgementsPerRun { get; set; } = 10;

    /// <summary>How far back the first fetch after a start reaches. Later fetches continue from the last completed one.</summary>
    public int LookbackMinutes { get; set; } = 10;

    public string KnowledgeFile { get; set; } = "knowledge/known-errors.md";
    public string RulesFile { get; set; } = "knowledge/rules.json";
    public string DatabasePath { get; set; } = "data/clog.db";
    public string TriageLogPath { get; set; } = "data/triage.jsonl";
}

public sealed class SeqOptions
{
    public string Url { get; set; } = "http://localhost:5341";

    /// <summary>Seq API key. Supply via environment variable or user-secrets, never in a committed file.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Optional Seq filter expression; combined with the Error level filter.</summary>
    public string? Filter { get; set; }

    /// <summary>Maximum events fetched per cycle.</summary>
    public int MaxEvents { get; set; } = 200;
}

public sealed class OllamaOptions
{
    public string Url { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "mistral";
    /// <summary>Per question. A 7B model on a CPU needs a minute or more, longer while it is still loading.</summary>
    public int TimeoutSeconds { get; set; } = 300;
}

public enum TriageMode
{
    /// <summary>Two yes/no questions: is it a known error, then is it a failed call from outside.</summary>
    TwoStep,

    /// <summary>The original single prompt asking the model to pick NOISE, KNOWN or ANALYZE. Kept for comparison.</summary>
    SingleCall
}

public sealed class TriageOptions
{
    public const string KnowledgePlaceholder = "{knowledge}";
    public const string ErrorPlaceholder = "{error}";

    /// <summary>
    /// TwoStep is the default because 7B models could not pick between three verdicts in one
    /// call, but answer a single yes/no question reliably.
    /// </summary>
    public TriageMode Mode { get; set; } = TriageMode.TwoStep;

    /// <summary>First TwoStep question. {knowledge} and {error} are filled in; yes means KNOWN.</summary>
    public string KnownPrompt { get; set; } =
        """
        Beskriver kunskapsbasen nedan exakt detta fel? Svara ENDAST Ja eller Nej.

        # Kunskapsbas
        {knowledge}

        # Fel
        {error}
        """;

    /// <summary>Second TwoStep question. {error} is filled in; yes means NOISE.</summary>
    public string NoisePrompt { get; set; } =
        """
        Är detta ett misslyckat anrop utifrån — fel API-nyckel, 401/404, avbruten begäran — snarare än ett fel i vår kod? Svara ENDAST Ja eller Nej.

        # Fel
        {error}
        """;
}
