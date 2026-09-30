using Watcher.Model;

namespace Watcher.Ollama;

/// <summary>The local model's side of triage.</summary>
public interface IOllamaClient
{
    /// <summary>
    /// Asks the model to judge one error. Returns null when the model could not be reached
    /// or its answer could not be read; the caller treats that as an unjudged error, not a fault.
    /// </summary>
    Task<TriageVerdict?> JudgeAsync(string errorReport, string knownErrors, CancellationToken cancellationToken = default);
}
