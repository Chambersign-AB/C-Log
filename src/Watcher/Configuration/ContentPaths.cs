namespace Watcher.Configuration;

/// <summary>
/// Resolves the relative paths in configuration.
/// <para>
/// Running from source they are relative to the repository root, so <c>knowledge/rules.json</c>
/// is found whichever directory <c>dotnet run</c> was started from, rather than landing in
/// <c>src/Watcher/knowledge/</c>. In a published build there is no repository to find and the
/// content root is the base, which is where the files sit next to the binary.
/// </para>
/// </summary>
public static class ContentPaths
{
    private const string SolutionFileName = "C-Log.sln";

    public static string Resolve(string contentRoot, string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(FindBase(contentRoot), path);

    /// <summary>The repository root if we are inside one, otherwise the content root unchanged.</summary>
    public static string FindBase(string contentRoot)
    {
        var directory = new DirectoryInfo(contentRoot);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return contentRoot;
    }
}
