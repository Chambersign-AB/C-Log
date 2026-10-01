using System.Text.RegularExpressions;

namespace CLog.Analysis;

/// <summary>
/// One stack frame with the file and line it points at. Fingerprinting throws the location
/// away because it moves with every edit; finding the code needs exactly that part.
/// </summary>
public sealed partial record SourceFrame(string Method, string? File, int Line)
{
    [GeneratedRegex(@"^\s*(?:at|vid|bei)\s+(?<method>.+?)(?:\s+(?:in|i)\s+(?<file>.+?):(?:line|rad)\s+(?<line>\d+))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Frame();

    /// <summary>Every frame of the stack trace, top first. File is null for a frame built without symbols.</summary>
    public static IReadOnlyList<SourceFrame> Parse(string? exceptionText)
    {
        if (string.IsNullOrWhiteSpace(exceptionText))
        {
            return [];
        }

        var frames = new List<SourceFrame>();
        foreach (var rawLine in exceptionText.Split('\n'))
        {
            var match = Frame().Match(rawLine.TrimEnd('\r'));
            if (!match.Success)
            {
                continue;
            }

            var hasLocation = match.Groups["file"].Success
                && int.TryParse(match.Groups["line"].Value, out _);
            frames.Add(new SourceFrame(
                match.Groups["method"].Value.Trim(),
                hasLocation ? match.Groups["file"].Value.Trim() : null,
                hasLocation ? int.Parse(match.Groups["line"].Value) : 0));
        }

        return frames;
    }
}
