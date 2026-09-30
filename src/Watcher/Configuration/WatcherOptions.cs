namespace Watcher.Configuration;

/// <summary>Root configuration, bound from the "Watcher" section.</summary>
public sealed class WatcherOptions
{
    public const string SectionName = "Watcher";

    public SeqOptions Seq { get; set; } = new();
    public OllamaOptions Ollama { get; set; } = new();

    /// <summary>How often the watcher polls Seq.</summary>
    public int IntervalMinutes { get; set; } = 5;

    /// <summary>Upper bound on AI calls per cycle, so a burst of new errors cannot flood Ollama.</summary>
    public int MaxJudgementsPerRun { get; set; } = 10;

    /// <summary>How far back to ask Seq for errors. Should exceed IntervalMinutes to cover a missed cycle.</summary>
    public int LookbackMinutes { get; set; } = 10;

    public string KnowledgeFile { get; set; } = "knowledge/known-errors.md";
    public string RulesFile { get; set; } = "knowledge/rules.json";
    public string DatabasePath { get; set; } = "data/watcher.db";
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
    public int TimeoutSeconds { get; set; } = 120;
}
