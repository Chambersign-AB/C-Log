namespace CLog.Analysis;

/// <summary>What the analyst made of one error.</summary>
/// <param name="Text">The model's analysis in markdown, or null when the model gave none.</param>
/// <param name="Code">The code the model was shown.</param>
public sealed record AnalysisResult(string? Text, CodeContext Code);
