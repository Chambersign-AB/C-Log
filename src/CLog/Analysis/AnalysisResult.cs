namespace CLog.Analysis;

/// <summary>What the analyst made of one error.</summary>
/// <param name="Text">The model's analysis in markdown, or null when no model was asked or it gave none.</param>
/// <param name="Code">The code found for the error.</param>
public sealed record AnalysisResult(string? Text, CodeContext Code);
