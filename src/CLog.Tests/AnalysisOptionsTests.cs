using Microsoft.Extensions.Configuration;
using CLog.Configuration;

namespace CLog.Tests;

public class AnalysisOptionsTests
{
    private static AnalysisOptions Configured() => new()
    {
        Enabled = true,
        RepoPath = "C:/src/signing",
        GitHub = new GitHubOptions { Repository = "example-org/signing", Token = "test-token" }
    };

    [Fact]
    public void Step_two_is_off_until_it_is_switched_on_and_then_nothing_is_demanded_of_it()
    {
        var options = new AnalysisOptions();

        Assert.False(options.Enabled);
        Assert.Null(options.Problem());
    }

    [Fact]
    public void A_fully_configured_step_two_has_no_problem()
    {
        Assert.Null(Configured().Problem());
    }

    [Fact]
    public void Switching_it_on_without_a_repository_path_is_named_as_the_problem()
    {
        var options = Configured();
        options.RepoPath = " ";

        Assert.Contains("RepoPath", options.Problem());
    }

    [Theory]
    [InlineData("")]
    [InlineData("signing")]
    [InlineData("https://github.com/example-org/signing")]
    [InlineData("example-org/signing/issues")]
    public void A_repository_that_is_not_owner_and_name_is_named_as_the_problem(string repository)
    {
        var options = Configured();
        options.GitHub.Repository = repository;

        Assert.Contains("Repository", options.Problem());
    }

    [Fact]
    public void A_missing_token_is_named_as_the_problem()
    {
        var options = Configured();
        options.GitHub.Token = "";

        Assert.Contains("Token", options.Problem());
    }

    [Fact]
    public void The_shipped_settings_leave_step_two_off_with_no_token_and_the_built_in_prompt()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "C-Log.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var shipped = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(directory.FullName, "src", "CLog", "appsettings.json"))
            .Build()
            .GetSection(CLogOptions.SectionName)
            .Get<CLogOptions>()!
            .Analysis;

        Assert.False(shipped.Enabled);
        Assert.Equal("", shipped.GitHub.Token);
        Assert.Equal(new AnalysisOptions().Prompt, shipped.Prompt);
        Assert.Equal("ai-triage", shipped.GitHub.Label);
    }
}
