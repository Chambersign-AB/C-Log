namespace CLog.Analysis;

/// <summary>
/// The model that writes the analysis. Its own seam, apart from the triage model, so a
/// stronger model behind another API can take over step two without touching step one.
/// </summary>
public interface IAnalysisModel
{
    /// <summary>The model's answer to the prompt, or null when it could not be reached.</summary>
    Task<string?> CompleteAsync(string prompt, CancellationToken cancellationToken = default);
}
