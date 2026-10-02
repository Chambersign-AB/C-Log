using System.Text;
using Microsoft.Extensions.Options;
using CLog.Configuration;
using CLog.Model;

namespace CLog.Analysis;

/// <summary>
/// Finds the code behind an error: the topmost application frames of its stack trace, each
/// matched to a file in the repository and read around the line it names.
/// </summary>
public sealed class CodeContextResolver(ISourceRepository repository, IOptions<CLogOptions> options)
{
    /// <summary>Put in the margin of the line a frame names, in Context mode.</summary>
    public const string FocusMarker = ">>>";

    private readonly AnalysisOptions _options = options.Value.Analysis;

    public async Task<CodeContext> ResolveAsync(SeqEvent sample, CancellationToken cancellationToken = default)
    {
        var applicationFrames = SourceFrame.Parse(sample.Exception)
            .Where(f => f.File is not null && f.Method.StartsWith(_options.NamespacePrefix, StringComparison.Ordinal));

        // A model is shown only the top few frames, because every line costs it time. With
        // no model to wait for, every application frame is worth showing, up to the limits.
        var frames = (_options.Mode == AnalysisMode.Model
                ? applicationFrames.Take(Math.Max(0, _options.TopFrames))
                : applicationFrames)
            .ToList();

        if (frames.Count == 0)
        {
            return CodeContext.Empty;
        }

        sample.Properties.TryGetValue(_options.CommitProperty, out var requested);
        var commit = await repository.ResolveCommitAsync(requested, cancellationToken);
        if (commit is null)
        {
            return CodeContext.Empty;
        }

        var files = await repository.ListFilesAsync(commit.Hash, cancellationToken);
        var excerpts = new List<CodeExcerpt>();
        var filesRead = new HashSet<string>(StringComparer.Ordinal);
        var placesRead = new HashSet<(string Path, int Line)>();
        var linesLeft = Math.Max(0, _options.MaxLines);

        foreach (var frame in frames)
        {
            if (linesLeft == 0)
            {
                break;
            }

            var path = MatchFile(files, frame.File!);
            if (path is null || (!filesRead.Contains(path) && filesRead.Count >= _options.MaxFiles))
            {
                continue;
            }

            // Recursion and async state machines repeat a frame; the same lines twice say nothing new.
            if (!placesRead.Add((path, frame.Line)))
            {
                continue;
            }

            var text = await repository.ReadFileAsync(commit.Hash, path, cancellationToken);
            if (text is null)
            {
                continue;
            }

            var excerpt = Cut(path, frame, text.Split('\n'), linesLeft);
            if (excerpt is null)
            {
                continue;
            }

            filesRead.Add(path);
            linesLeft -= excerpt.LineCount;
            excerpts.Add(excerpt);
        }

        return new CodeContext(commit, excerpts);
    }

    /// <summary>
    /// A frame carries the path the code had on the build machine. The repository file it
    /// refers to is the one sharing the longest tail of that path; a tail shared by several
    /// files is a guess, and no code is better than the wrong code. The answer is always one
    /// of the repository's own paths, so nothing outside it can be named.
    /// </summary>
    internal static string? MatchFile(IReadOnlyList<string> files, string framePath)
    {
        var segments = framePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (var take = segments.Length; take >= 1; take--)
        {
            var tail = string.Join('/', segments[^take..]);
            var matches = files
                .Where(f => f.Equals(tail, StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith("/" + tail, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();

            if (matches.Count > 0)
            {
                return matches.Count == 1 ? matches[0] : null;
            }
        }

        return null;
    }

    private CodeExcerpt? Cut(string path, SourceFrame frame, string[] lines, int linesLeft)
    {
        // A trailing newline leaves one empty element that is not a line of the file.
        var count = lines.Length > 0 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;

        // The file is shorter than the frame says: it has changed since the failing build.
        if (frame.Line < 1 || frame.Line > count)
        {
            return null;
        }

        var around = Math.Max(0, _options.ContextLines);
        var start = Math.Max(1, frame.Line - around);
        var end = Math.Min(count, frame.Line + around);

        if (end - start + 1 > linesLeft)
        {
            start = Math.Max(start, frame.Line - (linesLeft - 1) / 2);
            end = Math.Min(end, start + linesLeft - 1);
        }

        var text = new StringBuilder();
        for (var number = start; number <= end; number++)
        {
            var focus = number == frame.Line;

            // A person scanning an issue needs a marker that stands out in the margin; the
            // format the model is shown is left as it was.
            if (_options.Mode == AnalysisMode.Model)
            {
                text.Append(number.ToString().PadLeft(5)).Append(focus ? " > " : "   ");
            }
            else
            {
                text.Append(focus ? FocusMarker : "   ").Append(number.ToString().PadLeft(6)).Append("  ");
            }

            text.Append(lines[number - 1].TrimEnd('\r')).Append('\n');
        }

        return new CodeExcerpt(path, start, end, frame.Line, frame.Method, text.ToString());
    }
}
