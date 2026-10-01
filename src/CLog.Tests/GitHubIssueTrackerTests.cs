using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using CLog.Analysis;
using CLog.Configuration;
using CLog.Tests.Fakes;

namespace CLog.Tests;

public class GitHubIssueTrackerTests
{
    private static (GitHubIssueTracker Tracker, TestLogger<GitHubIssueTracker> Logger) Build(StubHttpMessageHandler handler)
    {
        var logger = new TestLogger<GitHubIssueTracker>();
        var options = Options.Create(new CLogOptions
        {
            Analysis = new AnalysisOptions
            {
                GitHub = new GitHubOptions { Repository = "example-org/signing", Token = "test-token" }
            }
        });

        return (new GitHubIssueTracker(new HttpClient(handler), options, logger), logger);
    }

    [Fact]
    public async Task An_issue_is_posted_to_the_repository_with_title_body_and_the_ai_triage_label()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.Created, """{ "number": 42, "title": "ignored" }""");
        var (tracker, _) = Build(handler);

        var number = await tracker.CreateIssueAsync("the title", "the body");

        Assert.Equal(42, number);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("https://api.github.com/repos/example-org/signing/issues", handler.LastRequestUri?.ToString());

        using var request = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.Equal("the title", request.RootElement.GetProperty("title").GetString());
        Assert.Equal("the body", request.RootElement.GetProperty("body").GetString());
        Assert.Equal(
            "ai-triage",
            Assert.Single(request.RootElement.GetProperty("labels").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task The_token_is_sent_as_a_bearer_token()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.Created, """{ "number": 42 }""");
        var (tracker, _) = Build(handler);

        await tracker.CreateIssueAsync("the title", "the body");

        Assert.Equal("Bearer test-token", handler.LastAuthorization);
    }

    [Fact]
    public async Task A_comment_is_posted_to_the_issue_it_belongs_to()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.Created, """{ "id": 9001 }""");
        var (tracker, _) = Build(handler);

        Assert.True(await tracker.AddCommentAsync(42, "seen again"));

        Assert.Equal(
            "https://api.github.com/repos/example-org/signing/issues/42/comments",
            handler.LastRequestUri?.ToString());
        using var request = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        Assert.Equal("seen again", request.RootElement.GetProperty("body").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task An_error_status_files_nothing_is_logged_and_does_not_throw(HttpStatusCode status)
    {
        var (tracker, logger) = Build(new StubHttpMessageHandler(status, """{ "message": "no" }"""));

        Assert.Null(await tracker.CreateIssueAsync("the title", "the body"));
        Assert.False(await tracker.AddCommentAsync(42, "seen again"));
        Assert.Contains(logger.Warnings, w => w.Message.Contains("GitHub answered"));
    }

    [Fact]
    public async Task An_unreachable_github_files_nothing_is_logged_and_does_not_throw()
    {
        var (tracker, logger) = Build(StubHttpMessageHandler.Unreachable());

        Assert.Null(await tracker.CreateIssueAsync("the title", "the body"));
        Assert.False(await tracker.AddCommentAsync(42, "seen again"));
        Assert.Contains(logger.Warnings, w => w.Message.Contains("Could not reach GitHub"));
    }

    [Fact]
    public async Task The_token_never_appears_in_the_log()
    {
        var (tracker, logger) = Build(new StubHttpMessageHandler(HttpStatusCode.Unauthorized, "{}"));

        await tracker.CreateIssueAsync("the title", "the body");

        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("test-token"));
    }
}
