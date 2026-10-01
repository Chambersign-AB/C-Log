using CLog.Model;
using CLog.Ollama;

namespace CLog.Tests.Fakes;

/// <summary>
/// A model that does not answer until the test lets it, standing in for one that takes longer
/// than the polling interval. No real waiting is involved.
/// </summary>
public sealed class GatedOllamaClient : IOllamaClient
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _asked = new(0);
    private readonly SemaphoreSlim _released = new(0);
    private volatile string _answer = "Nej";

    /// <summary>Completes once the model has been asked a question it has not yet answered.</summary>
    public async Task WaitUntilAskedAsync()
    {
        Assert.True(await _asked.WaitAsync(Patience), "the model was never asked");
    }

    /// <summary>Lets the model answer the question it is holding.</summary>
    public void Answer(string answer)
    {
        _answer = answer;
        _released.Release();
    }

    public async Task<string?> AskAsync(
        string prompt,
        CancellationToken cancellationToken = default,
        string? model = null)
    {
        _asked.Release();
        await _released.WaitAsync(cancellationToken);
        return _answer;
    }

    public Task<TriageVerdict?> JudgeAsync(
        string errorReport,
        string knownErrors,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The gated model only answers yes/no questions.");
}
