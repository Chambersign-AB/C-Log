using Microsoft.Extensions.Options;
using CLog.Analysis;
using CLog.Configuration;
using CLog.Model;
using CLog.Tests.Fakes;

namespace CLog.Tests;

public class CodeContextResolverTests
{
    private const string BuildRoot = @"C:\agent\_work\1\s\";
    private const string Controller = "src/CSign.Api/Controllers/SignController.cs";
    private const string Signer = "src/CSign.Core/Signer.cs";
    private const string KeyStore = "src/CSign.Core/KeyStore.cs";
    private const string Fourth = "src/CSign.Core/Fourth.cs";

    private readonly FakeSourceRepository _repository = new FakeSourceRepository()
        .WithNumberedFile(Controller, 200)
        .WithNumberedFile(Signer, 200)
        .WithNumberedFile(KeyStore, 200)
        .WithNumberedFile(Fourth, 200);

    private CodeContextResolver Resolver(AnalysisOptions? analysis = null) =>
        new(_repository, Options.Create(new CLogOptions { Analysis = analysis ?? new AnalysisOptions() }));

    private static string Frame(string method, string repoPath, int line) =>
        $"   at {method} in {BuildRoot}{repoPath.Replace('/', '\\')}:line {line}";

    private static SeqEvent Event(string stack, string? commit = null) => new()
    {
        Id = "event-1",
        Exception = "System.InvalidOperationException: boom\n" + stack,
        Properties = commit is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { ["CommitHash"] = commit }
    };

    private static readonly string FullStack = string.Join('\n',
        "   at System.Data.SqlClient.SqlCommand.ExecuteNonQuery()",
        Frame("CSign.Api.Controllers.SignController.Sign(Request request)", Controller, 120),
        "   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.Execute()",
        Frame("CSign.Core.Signer.Apply(Document document)", Signer, 10),
        @"   at Contoso.Library.Thing.Do() in C:\lib\Thing.cs:line 5",
        Frame("CSign.Core.KeyStore.Load()", KeyStore, 195),
        Frame("CSign.Core.Fourth.Go()", Fourth, 3));

    [Fact]
    public async Task The_top_three_application_frames_are_matched_to_their_files()
    {
        var context = await Resolver().ResolveAsync(Event(FullStack));

        Assert.Equal(new[] { Controller, Signer, KeyStore }, context.Excerpts.Select(e => e.Path));
        Assert.Equal(new[] { Controller, Signer, KeyStore }, _repository.Reads);
    }

    [Fact]
    public async Task Forty_lines_are_read_on_each_side_of_the_line_a_frame_names()
    {
        var context = await Resolver().ResolveAsync(Event(FullStack));
        var middle = context.Excerpts[0];

        Assert.Equal((80, 160, 120), (middle.StartLine, middle.EndLine, middle.FocusLine));
        Assert.Contains("SignController.cs line 80\n", middle.Text);
        Assert.Contains("SignController.cs line 160\n", middle.Text);
        Assert.DoesNotContain("SignController.cs line 79\n", middle.Text);
        Assert.DoesNotContain("SignController.cs line 161\n", middle.Text);
    }

    [Fact]
    public async Task The_window_stops_at_the_start_and_the_end_of_the_file()
    {
        var context = await Resolver().ResolveAsync(Event(FullStack));

        Assert.Equal((1, 50), (context.Excerpts[1].StartLine, context.Excerpts[1].EndLine));
        Assert.Equal((155, 200), (context.Excerpts[2].StartLine, context.Excerpts[2].EndLine));
    }

    [Fact]
    public async Task Every_line_is_numbered_and_the_failing_line_is_marked()
    {
        var context = await Resolver().ResolveAsync(Event(FullStack));

        Assert.Contains("  120 > // SignController.cs line 120\n", context.Excerpts[0].Text);
        Assert.Contains("  119   // SignController.cs line 119\n", context.Excerpts[0].Text);
    }

    [Fact]
    public async Task By_default_no_more_than_four_files_and_four_hundred_lines_are_read()
    {
        var context = await Resolver().ResolveAsync(Event(FullStack));

        Assert.InRange(context.Excerpts.Select(e => e.Path).Distinct().Count(), 1, 4);
        Assert.InRange(context.Excerpts.Sum(e => e.LineCount), 1, 400);
    }

    [Fact]
    public async Task The_line_limit_holds_however_many_frames_there_are()
    {
        var context = await Resolver(new AnalysisOptions { TopFrames = 4, MaxLines = 100 }).ResolveAsync(Event(FullStack));

        Assert.Equal(100, context.Excerpts.Sum(e => e.LineCount));
        Assert.All(context.Excerpts, e => Assert.InRange(e.FocusLine, e.StartLine, e.EndLine));
    }

    [Fact]
    public async Task The_file_limit_holds_however_many_frames_there_are()
    {
        var context = await Resolver(new AnalysisOptions { TopFrames = 4, MaxFiles = 2 }).ResolveAsync(Event(FullStack));

        Assert.Equal(new[] { Controller, Signer }, context.Excerpts.Select(e => e.Path));
    }

    [Fact]
    public async Task A_frame_without_a_file_is_passed_over_for_the_next_one_that_has_one()
    {
        var stack = string.Join('\n',
            "   at CSign.Core.Signer.Apply(Document document)",
            Frame("CSign.Core.KeyStore.Load()", KeyStore, 20));

        var context = await Resolver().ResolveAsync(Event(stack));

        Assert.Equal(KeyStore, Assert.Single(context.Excerpts).Path);
    }

    [Theory]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData(@"..\..\..\secrets\passwords.cs")]
    [InlineData("/etc/passwd")]
    [InlineData(@"C:\agent\_work\1\s\..\..\outside\Signer.config")]
    public async Task A_frame_pointing_outside_the_repository_reads_nothing(string path)
    {
        var context = await Resolver().ResolveAsync(Event($"   at CSign.Core.Signer.Apply() in {path}:line 1"));

        Assert.Empty(context.Excerpts);
        Assert.Empty(_repository.Reads);
    }

    [Fact]
    public async Task Only_paths_the_repository_itself_lists_are_ever_read()
    {
        var stack = string.Join('\n',
            @"   at CSign.Core.Signer.Apply() in C:\somewhere\else\entirely\Signer.cs:line 5",
            @"   at CSign.Core.KeyStore.Load() in ..\..\KeyStore.cs:line 5");

        await Resolver().ResolveAsync(Event(stack));

        Assert.All(_repository.Reads, read => Assert.Contains(read, new[] { Controller, Signer, KeyStore, Fourth }));
    }

    [Fact]
    public async Task A_file_name_shared_by_two_files_is_not_guessed_at()
    {
        _repository.WithNumberedFile("src/CSign.Api/Helpers.cs", 50).WithNumberedFile("src/CSign.Core/Helpers.cs", 50);

        var context = await Resolver().ResolveAsync(
            Event(@"   at CSign.Core.Helpers.Hash() in C:\elsewhere\Helpers.cs:line 5"));

        Assert.Empty(context.Excerpts);
    }

    [Fact]
    public async Task The_directories_in_the_frame_tell_two_files_of_the_same_name_apart()
    {
        _repository.WithNumberedFile("src/CSign.Api/Helpers.cs", 50).WithNumberedFile("src/CSign.Core/Helpers.cs", 50);

        var context = await Resolver().ResolveAsync(
            Event(Frame("CSign.Core.Helpers.Hash()", "src/CSign.Core/Helpers.cs", 5)));

        Assert.Equal("src/CSign.Core/Helpers.cs", Assert.Single(context.Excerpts).Path);
    }

    [Fact]
    public async Task The_code_is_read_at_the_commit_the_event_names()
    {
        const string deployed = "abc1234abc1234abc1234abc1234abc1234abc123";
        _repository.WithNumberedFile(Signer, 200, commit: deployed, tag: "deployed ");

        var context = await Resolver().ResolveAsync(
            Event(Frame("CSign.Core.Signer.Apply()", Signer, 10), commit: deployed));

        Assert.Equal(new SourceCommit(deployed, FromEvent: true), context.Commit);
        Assert.Contains("deployed Signer.cs line 10", Assert.Single(context.Excerpts).Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("fffffff")]
    public async Task Without_a_commit_the_clone_has_HEAD_is_read(string? commit)
    {
        var context = await Resolver().ResolveAsync(
            Event(Frame("CSign.Core.Signer.Apply()", Signer, 10), commit));

        Assert.Equal(new SourceCommit(FakeSourceRepository.Head, FromEvent: false), context.Commit);
        Assert.Single(context.Excerpts);
    }

    [Fact]
    public async Task A_line_beyond_the_end_of_the_file_reads_nothing()
    {
        // The file has changed since the failing build; lines around a wrong place would mislead.
        var context = await Resolver().ResolveAsync(Event(Frame("CSign.Core.Signer.Apply()", Signer, 900)));

        Assert.Empty(context.Excerpts);
    }

    [Fact]
    public async Task A_stack_with_no_application_frames_does_not_touch_the_repository()
    {
        var context = await Resolver().ResolveAsync(
            Event("   at System.Data.SqlClient.SqlCommand.ExecuteNonQuery()\n   at Contoso.Library.Thing.Do() in C:\\lib\\Thing.cs:line 5"));

        Assert.Empty(context.Excerpts);
        Assert.Equal(0, _repository.ResolveCount);
    }

    [Fact]
    public async Task An_unavailable_repository_gives_no_code_and_no_fault()
    {
        _repository.Available = false;

        var context = await Resolver().ResolveAsync(Event(FullStack));

        Assert.Same(CodeContext.Empty, context);
    }
}
