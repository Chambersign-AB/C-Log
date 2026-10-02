using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using CLog.Configuration;
using CLog.Notifications;
using CLog.Tests.Fakes;

namespace CLog.Tests;

public class MailgunNotifierTests
{
    private static readonly Notification Notice = new(
        "CLog: 1 fel (1 ANALYZE)",
        ["[ANALYZE] InvalidOperationException i Signer.Apply — ×2 — https://github.com/example-org/signing/issues/7"]);

    private readonly TestLogger<MailgunNotifier> _logger = new();

    private MailgunNotifier Notifier(StubHttpMessageHandler handler, string apiUrl = "https://api.mailgun.net") => new(
        new HttpClient(handler),
        Options.Create(new CLogOptions
        {
            Notify = new NotifyOptions
            {
                Channels = "Mail",
                Mailgun = new MailgunOptions
                {
                    ApiUrl = apiUrl,
                    ApiKey = "test-key",
                    Domain = "mg.example.com",
                    To = "oncall@example.com",
                    From = "CLog <clog@mg.example.com>"
                }
            }
        }),
        _logger);

    private static Dictionary<string, string> Form(string body) => body
        .Split('&')
        .Select(pair => pair.Split('=', 2))
        .ToDictionary(p => WebUtility.UrlDecode(p[0]), p => WebUtility.UrlDecode(p[1]));

    [Fact]
    public async Task The_notice_is_posted_to_the_domains_messages_endpoint_with_the_api_key()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """{ "id": "queued" }""");

        var sent = await Notifier(handler).SendAsync(Notice);

        Assert.True(sent);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("https://api.mailgun.net/v3/mg.example.com/messages", handler.LastRequestUri?.ToString());
        Assert.Equal(
            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("api:test-key")),
            handler.LastAuthorization);
    }

    [Fact]
    public async Task The_mail_carries_sender_recipient_subject_and_one_line_per_error()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "{}");

        await Notifier(handler).SendAsync(Notice with
        {
            Lines = [Notice.Lines[0], "[KNOWN] TimeoutException i GiiApiClient.GetSigningUrlAsync — ×1 — Lösning: Vänta."]
        });

        var form = Form(Assert.Single(handler.RequestBodies));
        Assert.Equal("CLog <clog@mg.example.com>", form["from"]);
        Assert.Equal("oncall@example.com", form["to"]);
        Assert.Equal("CLog: 1 fel (1 ANALYZE)", form["subject"]);
        Assert.Equal(
            Notice.Lines[0] + "\n[KNOWN] TimeoutException i GiiApiClient.GetSigningUrlAsync — ×1 — Lösning: Vänta.",
            form["text"]);
    }

    [Fact]
    public async Task A_domain_in_the_EU_region_is_reached_through_the_configured_address()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "{}");

        await Notifier(handler, "https://api.eu.mailgun.net/").SendAsync(Notice);

        Assert.Equal("https://api.eu.mailgun.net/v3/mg.example.com/messages", handler.LastRequestUri?.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_refusal_from_mailgun_is_a_warning_not_a_fault(HttpStatusCode status)
    {
        var sent = await Notifier(new StubHttpMessageHandler(status, """{ "message": "no" }""")).SendAsync(Notice);

        Assert.False(sent);
        Assert.Contains(((int)status).ToString(), Assert.Single(_logger.Warnings).Message);
    }

    [Fact]
    public async Task An_unreachable_mailgun_is_a_warning_not_a_fault()
    {
        var sent = await Notifier(StubHttpMessageHandler.Unreachable()).SendAsync(Notice);

        Assert.False(sent);
        Assert.Contains("Could not reach Mailgun", Assert.Single(_logger.Warnings).Message);
    }

    [Fact]
    public async Task Neither_the_key_nor_the_recipient_is_written_to_the_log()
    {
        await Notifier(new StubHttpMessageHandler(HttpStatusCode.Unauthorized, "")).SendAsync(Notice);
        await Notifier(StubHttpMessageHandler.Unreachable()).SendAsync(Notice);

        Assert.All(_logger.Entries, entry =>
        {
            Assert.DoesNotContain("test-key", entry.Message);
            Assert.DoesNotContain("oncall@example.com", entry.Message);
        });
    }
}
