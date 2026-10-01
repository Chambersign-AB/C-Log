using System.Diagnostics;
using CLog.Analysis;
using CLog.Tests.Fakes;

namespace CLog.Tests;

/// <summary>
/// The git-backed repository against a real, throwaway repository on disk. Needs git on the
/// PATH and nothing else: no network, no remote.
/// </summary>
public sealed class GitSourceRepositoryTests : IDisposable
{
    private const string Signer = "src/CSign.Core/Signer.cs";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "c-log-tests", Guid.NewGuid().ToString("N"));

    private readonly string _repoPath;
    private readonly string _firstCommit;
    private readonly string _headCommit;

    public GitSourceRepositoryTests()
    {
        _repoPath = Path.Combine(_root, "signing");
        Directory.CreateDirectory(Path.Combine(_repoPath, "src", "CSign.Core"));
        File.WriteAllText(Path.Combine(_root, "outside.txt"), "not part of the repository");

        Git("init", "--quiet");
        _firstCommit = Commit("// as first deployed\n");
        _headCommit = Commit("// as it is now\n");
    }

    private GitSourceRepository Repository(string? path = null) =>
        new(path ?? _repoPath, new TestLogger<GitSourceRepository>());

    private string Commit(string signerText)
    {
        File.WriteAllText(Path.Combine(_repoPath, Signer), signerText);
        Git("add", "--all");
        Git("commit", "--quiet", "-m", "a change");
        return Git("rev-parse", "HEAD").Trim();
    }

    private string Git(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Nothing from the machine's own git configuration may decide how this repository behaves.
        foreach (var argument in (string[])
                 [
                     "-C", _repoPath,
                     "-c", "user.name=CLog Tests",
                     "-c", "user.email=clog-tests@example.com",
                     "-c", "commit.gpgsign=false",
                     "-c", "core.autocrlf=false",
                     .. arguments
                 ])
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {arguments[0]} failed: {error}");
        return output;
    }

    [Fact]
    public async Task A_commit_the_clone_has_is_read_as_it_was_then()
    {
        var repository = Repository();

        var commit = await repository.ResolveCommitAsync(_firstCommit);

        Assert.Equal(new SourceCommit(_firstCommit, FromEvent: true), commit);
        Assert.Equal("// as first deployed\n", await repository.ReadFileAsync(commit!.Hash, Signer));
    }

    [Fact]
    public async Task A_short_hash_resolves_to_the_whole_commit()
    {
        var commit = await Repository().ResolveCommitAsync(_firstCommit[..8]);

        Assert.Equal(new SourceCommit(_firstCommit, FromEvent: true), commit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]
    public async Task Without_a_commit_the_clone_has_HEAD_is_read(string? requested)
    {
        var repository = Repository();

        var commit = await repository.ResolveCommitAsync(requested);

        Assert.Equal(new SourceCommit(_headCommit, FromEvent: false), commit);
        Assert.Equal("// as it is now\n", await repository.ReadFileAsync(commit!.Hash, Signer));
    }

    [Theory]
    [InlineData("--upload-pack=calc")]
    [InlineData("HEAD~1")]
    [InlineData("main; echo owned")]
    [InlineData("$(whoami)")]
    public async Task A_commit_value_that_is_not_a_hash_is_never_handed_to_git(string requested)
    {
        var commit = await Repository().ResolveCommitAsync(requested);

        Assert.Equal(new SourceCommit(_headCommit, FromEvent: false), commit);
    }

    [Fact]
    public async Task Files_are_listed_with_repository_relative_paths()
    {
        Assert.Equal(new[] { Signer }, await Repository().ListFilesAsync(_headCommit));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("..\\outside.txt")]
    [InlineData("src/CSign.Core/../../../outside.txt")]
    [InlineData("src/CSign.Core/Missing.cs")]
    [InlineData(".git/config")]
    public async Task A_path_the_commit_does_not_list_is_not_read(string path)
    {
        Assert.Null(await Repository().ReadFileAsync(_headCommit, path));
    }

    [Fact]
    public async Task An_absolute_path_outside_the_repository_is_not_read()
    {
        Assert.Null(await Repository().ReadFileAsync(_headCommit, Path.Combine(_root, "outside.txt")));
    }

    [Fact]
    public async Task Reading_an_older_commit_leaves_the_working_copy_as_it_was()
    {
        var repository = Repository();

        await repository.ReadFileAsync(_firstCommit, Signer);

        Assert.Equal("// as it is now\n", File.ReadAllText(Path.Combine(_repoPath, Signer)));
        Assert.Equal(_headCommit, Git("rev-parse", "HEAD").Trim());
        Assert.Equal("", Git("status", "--porcelain").Trim());
    }

    [Fact]
    public async Task A_missing_directory_gives_no_commit_and_no_fault()
    {
        Assert.Null(await Repository(Path.Combine(_root, "not-there")).ResolveCommitAsync(null));
    }

    [Fact]
    public async Task A_directory_that_is_not_a_repository_gives_no_commit_and_no_fault()
    {
        var plain = Path.Combine(_root, "plain");
        Directory.CreateDirectory(plain);

        // Stops git from walking up into whatever repository the temp directory may sit in.
        Environment.SetEnvironmentVariable("GIT_CEILING_DIRECTORIES", _root);
        try
        {
            Assert.Null(await Repository(plain).ResolveCommitAsync(null));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CEILING_DIRECTORIES", null);
        }
    }

    public void Dispose()
    {
        try
        {
            if (!Directory.Exists(_root))
            {
                return;
            }

            // Git marks its object files read-only, which stops a plain recursive delete on Windows.
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A locked file in the temp directory is not worth failing a test over.
        }
    }
}
