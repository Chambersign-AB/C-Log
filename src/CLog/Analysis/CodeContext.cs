namespace CLog.Analysis;

/// <summary>The code an error came from, as far as it could be found.</summary>
/// <param name="Commit">The commit the code was read at, or null when no code was read.</param>
public sealed record CodeContext(SourceCommit? Commit, IReadOnlyList<CodeExcerpt> Excerpts)
{
    public static readonly CodeContext Empty = new(null, []);
}
