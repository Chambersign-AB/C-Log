using CLog.Model;
using CLog.Ollama;

namespace CLog.Tests.Fakes;

/// <summary>
/// Records every judgement request so a test can assert both how often the model was asked
/// and exactly what text it was given.
/// </summary>
public sealed class RecordingOllamaClient(TriageVerdict? answer = null) : IOllamaClient
{
    private readonly List<string> _reports = [];
    private readonly List<string> _knownErrors = [];

    /// <summary>What the fake answers with. Null stands for an unusable answer.</summary>
    public TriageVerdict? Answer { get; set; } = answer;

    public IReadOnlyList<string> Reports => _reports;
    public IReadOnlyList<string> KnownErrors => _knownErrors;
    public int CallCount => _reports.Count;

    public Task<TriageVerdict?> JudgeAsync(
        string errorReport,
        string knownErrors,
        CancellationToken cancellationToken = default)
    {
        _reports.Add(errorReport);
        _knownErrors.Add(knownErrors);
        return Task.FromResult(Answer);
    }

    private readonly List<string> _questions = [];

    /// <summary>What the fake says to a yes/no question, given the prompt. Null stands for an unreachable model.</summary>
    public Func<string, string?> Reply { get; set; } = _ => "Nej";

    public IReadOnlyList<string> Questions => _questions;

    public Task<string?> AskAsync(string prompt, CancellationToken cancellationToken = default)
    {
        _questions.Add(prompt);
        return Task.FromResult(Reply(prompt));
    }
}
