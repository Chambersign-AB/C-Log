using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace CLog.Analysis;

/// <summary>
/// Reads a local clone through git. Files are read with "git show commit:path" rather than by
/// checking the commit out: nothing in the clone changes, so it can be somebody's working
/// copy, and a file as it was at the failing commit is read without disturbing HEAD.
/// </summary>
public sealed partial class GitSourceRepository(string repoPath, ILogger<GitSourceRepository> logger)
    : ISourceRepository
{
    private readonly Lock _gate = new();
    private string? _listedCommit;
    private IReadOnlyList<string> _listedFiles = [];

    /// <summary>A hash and nothing else: the value comes from a log event and ends up on a command line.</summary>
    [GeneratedRegex("^[0-9a-fA-F]{7,40}$")]
    private static partial Regex CommitHash();

    public async Task<SourceCommit?> ResolveCommitAsync(
        string? requestedHash,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(repoPath))
        {
            logger.LogWarning("Source repository {RepoPath} not found; analysing without code", repoPath);
            return null;
        }

        var requested = requestedHash?.Trim() ?? "";
        if (CommitHash().IsMatch(requested))
        {
            var found = await RunAsync(["rev-parse", "--verify", "--quiet", requested + "^{commit}"], cancellationToken);
            if (found is not null)
            {
                return new SourceCommit(found.Trim(), FromEvent: true);
            }

            logger.LogInformation("Commit {Commit} is not in {RepoPath}; reading HEAD instead", requested, repoPath);
        }

        var head = await RunAsync(["rev-parse", "--verify", "--quiet", "HEAD^{commit}"], cancellationToken);
        if (head is null)
        {
            logger.LogWarning("{RepoPath} is not a git repository with a commit; analysing without code", repoPath);
            return null;
        }

        return new SourceCommit(head.Trim(), FromEvent: false);
    }

    public async Task<IReadOnlyList<string>> ListFilesAsync(string commit, CancellationToken cancellationToken = default)
    {
        if (!CommitHash().IsMatch(commit))
        {
            return [];
        }

        lock (_gate)
        {
            if (_listedCommit == commit)
            {
                return _listedFiles;
            }
        }

        var output = await RunAsync(["ls-tree", "-r", "--name-only", "-z", commit], cancellationToken);
        IReadOnlyList<string> files = output is null ? [] : output.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        lock (_gate)
        {
            _listedCommit = commit;
            _listedFiles = files;
        }

        return files;
    }

    public async Task<string?> ReadFileAsync(string commit, string path, CancellationToken cancellationToken = default)
    {
        // Only a path the commit itself lists is read, so no path can reach outside the repository.
        var files = await ListFilesAsync(commit, cancellationToken);
        if (!files.Contains(path, StringComparer.Ordinal))
        {
            return null;
        }

        return await RunAsync(["show", $"{commit}:{path}"], cancellationToken);
    }

    /// <summary>Runs git in the repository and returns its output, or null when it failed.</summary>
    private async Task<string?> RunAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(repoPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0)
            {
                return await output;
            }

            logger.LogDebug("git {Command} exited with {ExitCode}: {Error}", arguments[0], process.ExitCode, await error);
            return null;
        }
        catch (Win32Exception ex)
        {
            logger.LogWarning(ex, "git could not be started; analysing without code");
            return null;
        }
    }
}
