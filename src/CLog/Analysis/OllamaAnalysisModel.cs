using Microsoft.Extensions.Options;
using CLog.Configuration;
using CLog.Ollama;

namespace CLog.Analysis;

/// <summary>Analysis by a local model through the same Ollama client triage uses, with its own model name.</summary>
public sealed class OllamaAnalysisModel(IOllamaClient ollama, IOptions<CLogOptions> options) : IAnalysisModel
{
    private readonly string _model = options.Value.Analysis.Model;

    public Task<string?> CompleteAsync(string prompt, CancellationToken cancellationToken = default) =>
        ollama.AskAsync(prompt, cancellationToken, _model);
}
