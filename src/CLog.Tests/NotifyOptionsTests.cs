using Microsoft.Extensions.Configuration;
using CLog.Configuration;

namespace CLog.Tests;

public class NotifyOptionsTests
{
    private static NotifyOptions Complete(string channels = "Mail") => new()
    {
        Channels = channels,
        Mailgun = new MailgunOptions
        {
            ApiKey = "test-key",
            Domain = "mg.example.com",
            To = "oncall@example.com",
            From = "clog@mg.example.com"
        }
    };

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",")]
    public void With_no_channel_named_notices_are_off_and_nothing_is_missing(string channels)
    {
        var options = new NotifyOptions { Channels = channels };

        Assert.False(options.Enabled);
        Assert.Null(options.Problem());
    }

    [Theory]
    [InlineData("Mail")]
    [InlineData("mail")]
    [InlineData(" Mail , ")]
    public void Mail_fully_configured_is_on_with_nothing_missing(string channels)
    {
        var options = Complete(channels);

        Assert.True(options.Enabled);
        Assert.True(options.Uses(NotifyOptions.Mail));
        Assert.Null(options.Problem());
    }

    [Fact]
    public void Each_missing_mailgun_setting_is_named()
    {
        var noKey = Complete();
        noKey.Mailgun.ApiKey = "";
        var noDomain = Complete();
        noDomain.Mailgun.Domain = "";
        var noRecipient = Complete();
        noRecipient.Mailgun.To = " ";
        var noSender = Complete();
        noSender.Mailgun.From = "";

        Assert.Contains("Mailgun:ApiKey", noKey.Problem());
        Assert.Contains("user-secrets", noKey.Problem());
        Assert.Contains("Mailgun:Domain", noDomain.Problem());
        Assert.Contains("Mailgun:To", noRecipient.Problem());
        Assert.Contains("Mailgun:From", noSender.Problem());
    }

    [Theory]
    [InlineData("Slack")]
    [InlineData("Slack,Mail")]
    public void Slack_is_refused_until_it_exists_rather_than_silently_sending_nothing(string channels)
    {
        Assert.Contains("Slack, which is not available yet", Complete(channels).Problem());
    }

    [Fact]
    public void A_channel_nobody_has_heard_of_is_refused_by_name()
    {
        Assert.Contains("\"Pager\"", Complete("Mail,Pager").Problem());
    }

    [Fact]
    public void The_settings_bind_from_the_Notify_section()
    {
        var options = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CLog:Notify:Channels"] = "Mail",
                ["CLog:Notify:RepeatMinutes"] = "60",
                ["CLog:Notify:Mailgun:ApiKey"] = "test-key",
                ["CLog:Notify:Mailgun:Domain"] = "mg.example.com",
                ["CLog:Notify:Mailgun:To"] = "oncall@example.com",
                ["CLog:Notify:Mailgun:From"] = "clog@mg.example.com"
            })
            .Build()
            .GetSection(CLogOptions.SectionName)
            .Get<CLogOptions>()!
            .Notify;

        Assert.Null(options.Problem());
        Assert.Equal(60, options.RepeatMinutes);
        Assert.Equal("https://api.mailgun.net", options.Mailgun.ApiUrl);
    }

    [Fact]
    public void The_shipped_settings_leave_notices_off_with_no_key()
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
            .Notify;

        Assert.False(shipped.Enabled);
        Assert.Equal("", shipped.Mailgun.ApiKey);
        Assert.Equal(1440, shipped.RepeatMinutes);
    }

    [Theory]
    [InlineData("https://api.github.com", "https://github.com/example-org/signing/issues/7")]
    [InlineData("https://api.github.com/", "https://github.com/example-org/signing/issues/7")]
    [InlineData("https://git.example.com/api/v3", "https://git.example.com/example-org/signing/issues/7")]
    public void The_link_to_an_issue_is_the_page_a_person_opens_not_the_api_address(string apiUrl, string expected)
    {
        var gitHub = new GitHubOptions { ApiUrl = apiUrl, Repository = "example-org/signing" };

        Assert.Equal(expected, gitHub.IssueUrl(7));
    }
}
