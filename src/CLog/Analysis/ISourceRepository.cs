namespace CLog.Analysis;

/// <summary>The commit code is read at, and whether it is the one the event named.</summary>
public sealed record SourceCommit(string Hash, bool FromEvent);

/// <summary>Read-only access to the source the errors come from, at a given commit.</summary>
public interface ISourceRepository
{
    /// <summary>
    /// The commit to read: the requested one when the clone has it, otherwise HEAD. Null when
    /// the repository cannot be read at all.
    /// </summary>
    Task<SourceCommit?> ResolveCommitAsync(string? requestedHash, CancellationToken cancellationToken = default);

    /// <summary>Every file in the commit, as repository-relative paths with forward slashes.</summary>
    Task<IReadOnlyList<string>> ListFilesAsync(string commit, CancellationToken cancellationToken = default);

    /// <summary>The file's text at that commit. Null for a path that is not one of the commit's files.</summary>
    Task<string?> ReadFileAsync(string commit, string path, CancellationToken cancellationToken = default);
}
