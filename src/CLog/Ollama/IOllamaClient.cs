using CLog.Model;

namespace CLog.Ollama;

/// <summary>The local model's side of triage.</summary>
public interface IOllamaClient
{
    /// <summary>
    /// Asks the model to judge one error. Returns null when the model could not be reached
    /// or its answer could not be read; the caller treats that as an unjudged error, not a fault.
    /// </summary>
    Task<TriageVerdict?> JudgeAsync(string errorReport, string knownErrors, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends one prompt and returns the model's answer as plain text, unparsed. Returns null
    /// when the model could not be reached; an empty answer is an answer, not a failure.
    /// <paramref name="model"/> overrides the configured triage model for this one question.
    /// </summary>
    Task<string?> AskAsync(string prompt, CancellationToken cancellationToken = default, string? model = null);
}
