using CLog.Analysis;

namespace CLog.Tests.Fakes;

/// <summary>Records every analysis prompt and answers with a canned analysis.</summary>
public sealed class FakeAnalysisModel : IAnalysisModel
{
    public const string DefaultAnswer = "**Trolig orsak:** låset tas i fel ordning.\n\n**Var:** `Signer.cs:120`";

    private readonly List<string> _prompts = [];

    public IReadOnlyList<string> Prompts => _prompts;

    /// <summary>Null stands for a model that could not be reached.</summary>
    public string? Answer { get; set; } = DefaultAnswer;

    public Task<string?> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        _prompts.Add(prompt);
        return Task.FromResult(Answer);
    }
}
