using System.Text.RegularExpressions;

namespace Watcher.Triage;

/// <summary>Exception type, message and stack frames pulled out of Seq's single exception string.</summary>
public sealed record ExceptionInfo(string Type, string Message, IReadOnlyList<string> Frames)
{
    public static readonly ExceptionInfo Empty = new("", "", []);

    private static readonly Regex FramePrefix = new(@"^\s*(?:at|vid|bei)\s+", RegexOptions.Compiled);

    /// <summary>Strips " in C:\path\File.cs:line 42" — the path and line move with every edit.</summary>
    private static readonly Regex FrameLocation =
        new(@"\s+(?:in|i)\s+.+?:(?:line|rad)\s+\d+\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Reads the first "Type: message" line and every "   at Frame" line below it.
    /// <para>
    /// When that line carries an inner exception after "---&gt; ", the inner type wins. A
    /// wrapper such as AggregateException or TargetInvocationException says nothing about
    /// what went wrong, and grouping by it would collapse unrelated bugs into one
    /// fingerprint; the exception it wraps is the one worth identifying.
    /// </para>
    /// </summary>
    public static ExceptionInfo Parse(string? exceptionText)
    {
        if (string.IsNullOrWhiteSpace(exceptionText))
        {
            return Empty;
        }

        var type = "";
        var message = "";
        var frames = new List<string>();

        foreach (var rawLine in exceptionText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').TrimEnd();
            if (line.Length == 0)
            {
                continue;
            }

            if (FramePrefix.IsMatch(line))
            {
                var frame = FrameLocation.Replace(FramePrefix.Replace(line, ""), "").Trim();
                if (frame.Length > 0)
                {
                    frames.Add(frame);
                }

                continue;
            }

            // A header line. Only the first is read; anything below it belongs to an inner
            // exception whose own header we have already taken from this line.
            if (type.Length > 0)
            {
                continue;
            }

            var header = line.TrimStart();
            const string innerMarker = "---> ";
            var markerAt = header.IndexOf(innerMarker, StringComparison.Ordinal);
            if (markerAt >= 0)
            {
                header = header[(markerAt + innerMarker.Length)..];
            }

            var colon = header.IndexOf(':');
            if (colon > 0 && !header.AsSpan(0, colon).ContainsAny(' ', '\t'))
            {
                type = header[..colon].Trim();
                message = header[(colon + 1)..].Trim();
            }
            else
            {
                type = header.Trim();
            }
        }

        return new ExceptionInfo(type, message, frames);
    }

    /// <summary>
    /// The topmost frame that is not framework code. Microsoft.* and System.* frames are the
    /// plumbing an error travelled through, not the place it came from.
    /// </summary>
    public string TopApplicationFrame()
    {
        foreach (var frame in Frames)
        {
            if (!IsFrameworkFrame(frame))
            {
                return frame;
            }
        }

        return Frames.Count > 0 ? Frames[0] : "";
    }

    private static bool IsFrameworkFrame(string frame) =>
        frame.StartsWith("Microsoft.", StringComparison.Ordinal)
        || frame.StartsWith("System.", StringComparison.Ordinal);
}
