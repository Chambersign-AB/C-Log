using CLog.Analysis;

namespace CLog.Tests.Fakes;

/// <summary>A source repository held in memory: commits, each a set of files, the last one added being HEAD.</summary>
public sealed class FakeSourceRepository : ISourceRepository
{
    public const string Head = "1111111111111111111111111111111111111111";

    private readonly Dictionary<string, Dictionary<string, string>> _commits = new(StringComparer.Ordinal)
    {
        [Head] = new Dictionary<string, string>(StringComparer.Ordinal)
    };

    private readonly List<string> _reads = [];

    /// <summary>Every path a read was attempted for, whether or not it exists.</summary>
    public IReadOnlyList<string> Reads => _reads;

    public int ResolveCount { get; private set; }

    /// <summary>False stands for a clone that is missing or not a repository.</summary>
    public bool Available { get; set; } = true;

    public FakeSourceRepository WithFile(string path, string text, string commit = Head)
    {
        if (!_commits.TryGetValue(commit, out var files))
        {
            _commits[commit] = files = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        files[path] = text;
        return this;
    }

    /// <summary>A file of numbered lines, so a test can tell exactly which lines were read.</summary>
    public FakeSourceRepository WithNumberedFile(string path, int lines, string commit = Head, string tag = "") =>
        WithFile(path, NumberedLines(path, lines, tag), commit);

    public static string NumberedLines(string path, int lines, string tag = "") =>
        string.Concat(Enumerable.Range(1, lines).Select(n => $"// {tag}{Path.GetFileName(path)} line {n}\n"));

    public Task<SourceCommit?> ResolveCommitAsync(string? requestedHash, CancellationToken cancellationToken = default)
    {
        ResolveCount++;
        if (!Available)
        {
            return Task.FromResult<SourceCommit?>(null);
        }

        return Task.FromResult<SourceCommit?>(
            requestedHash is not null && _commits.ContainsKey(requestedHash)
                ? new SourceCommit(requestedHash, FromEvent: true)
                : new SourceCommit(Head, FromEvent: false));
    }

    public Task<IReadOnlyList<string>> ListFilesAsync(string commit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(
            _commits.TryGetValue(commit, out var files) ? [.. files.Keys] : []);

    public Task<string?> ReadFileAsync(string commit, string path, CancellationToken cancellationToken = default)
    {
        _reads.Add(path);
        return Task.FromResult(
            _commits.TryGetValue(commit, out var files) ? files.GetValueOrDefault(path) : null);
    }
}
