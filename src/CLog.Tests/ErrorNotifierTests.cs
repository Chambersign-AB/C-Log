using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CLog.Analysis;
using CLog.Configuration;
using CLog.Knowledge;
using CLog.Model;
using CLog.Notifications;
using CLog.Tests.Fakes;
using CLog.Triage;

namespace CLog.Tests;

/// <summary>
/// Notices inside a cycle: the real service, issue reporter and notifier, with the model,
/// GitHub and the channel faked at the edges.
/// </summary>
public class ErrorNotifierTests
{
    private const string KnownErrorsText =
        """
        ## Receipt rendering is not configured
        **Solution** Set PostProcessing:BaseUrl
        and restart the service.
        """;

    private readonly ManualTimeProvider _time = new();
    private readonly InMemoryFingerprintStore _store = new();
    private readonly RecordingTriageSink _sink = new();
    private readonly FakeIssueTracker _tracker = new();
    private readonly FakeNotifier _channel = new();
    private readonly RecordingOllamaClient _model = new();
    private readonly TestLogger<ErrorNotifier> _logger = new();

    private static SeqEvent ReceiptError(string id = "event-1") => TestEvents.Error(
        id: id,
        template: "HTTP {RequestMethod} {RequestPath} responded {StatusCode}",
        exception:
        "System.InvalidOperationException: receipt for anna.svensson@example.com could not be rendered\n"
        + "   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.Execute()\n"
        + @"   at CSign.Web.Controllers.SigningSessionsController.GetReceipt(Guid id, CancellationToken cancellationToken) in C:\agent\src\SigningSessionsController.cs:line 88");

    private static bool IsKnownQuestion(string prompt) => prompt.Contains("Kunskapsbas", StringComparison.Ordinal);

    private void Judge(Verdict verdict) => _model.Reply = verdict switch
    {
        Verdict.Known => _ => "Ja",
        Verdict.Noise => prompt => IsKnownQuestion(prompt) ? "Nej" : "Ja",
        _ => _ => "Nej"
    };

    private TriageService Service(bool stepTwo = true, params SeqEvent[] events)
    {
        var options = Options.Create(new CLogOptions
        {
            Analysis = new AnalysisOptions
            {
                Enabled = stepTwo,
                GitHub = new GitHubOptions { Repository = "example-org/signing" }
            },
            Notify = new NotifyOptions { Channels = "Mail" }
        });

        IssueReporter? reporter = null;
        if (stepTwo)
        {
            var analyst = new Analyst(
                new CodeContextResolver(new FakeSourceRepository(), options),
                new FakeAnalysisModel(),
                options,
                new TestLogger<Analyst>());
            reporter = new IssueReporter(analyst, _tracker, _store, options, new TestLogger<IssueReporter>());
        }

        return new TriageService(
            new FakeSeqClient(events.Length == 0 ? [ReceiptError()] : events),
            _store,
            _model,
            _sink,
            new StaticKnowledgeSource(KnownErrorsText),
            new StaticRuleSetProvider(RuleSet.Parse("""{ "ignore": [] }""")),
            options,
            new TestLogger<TriageService>(),
            _time,
            reporter,
            new ErrorNotifier([_channel], _store, options, _logger));
    }

    private void Wait(int minutes) => _time.Tick(TimeSpan.FromMinutes(minutes));

    [Fact]
    public async Task An_error_judged_ANALYZE_is_one_line_with_what_where_how_often_and_the_issue()
    {
        Judge(Verdict.Analyze);

        await Service(events: [ReceiptError("a"), ReceiptError("b")]).RunOnceAsync();

        var line = Assert.Single(Assert.Single(_channel.Sent).Lines);
        Assert.Equal(
            "[ANALYZE] InvalidOperationException i SigningSessionsController.GetReceipt — ×2 — "
            + $"https://github.com/example-org/signing/issues/{FakeIssueTracker.FirstIssueNumber}",
            line);
    }

    [Fact]
    public async Task An_error_judged_KNOWN_is_one_line_that_ends_with_the_solution_from_the_knowledge_base()
    {
        Judge(Verdict.Known);

        await Service().RunOnceAsync();

        var line = Assert.Single(Assert.Single(_channel.Sent).Lines);
        Assert.Equal(
            "[KNOWN] InvalidOperationException i SigningSessionsController.GetReceipt — ×1 — "
            + "Lösning: Set PostProcessing:BaseUrl and restart the service.",
            line);
    }

    [Fact]
    public async Task An_error_judged_NOISE_sends_nothing()
    {
        Judge(Verdict.Noise);
        var service = Service();

        await service.RunOnceAsync();
        Wait(2 * 24 * 60);
        await service.RunOnceAsync();

        Assert.Equal(Verdict.Noise, Assert.Single(_sink.Records).Verdict);
        Assert.Empty(_channel.Sent);
    }

    [Fact]
    public async Task An_error_the_model_could_not_judge_sends_nothing()
    {
        _model.Reply = _ => null;

        await Service().RunOnceAsync();

        Assert.Empty(_channel.Sent);
    }

    [Fact]
    public async Task Several_errors_in_one_cycle_are_one_notification_with_a_line_each()
    {
        Judge(Verdict.Analyze);

        await Service(events:
        [
            ReceiptError("a"),
            TestEvents.MessageOnly("Key store could not be opened", id: "b")
        ]).RunOnceAsync();

        var notification = Assert.Single(_channel.Sent);
        Assert.Equal(2, notification.Lines.Count);
        Assert.Equal("CLog: 2 fel (2 ANALYZE)", notification.Subject);
        Assert.StartsWith("[ANALYZE] Key store could not be opened — ×1 — https://", notification.Lines[1]);
    }

    [Fact]
    public async Task Without_step_two_the_line_has_no_link()
    {
        Judge(Verdict.Analyze);

        await Service(stepTwo: false).RunOnceAsync();

        Assert.Equal(
            "[ANALYZE] InvalidOperationException i SigningSessionsController.GetReceipt — ×1",
            Assert.Single(Assert.Single(_channel.Sent).Lines));
    }

    [Fact]
    public async Task An_error_that_keeps_recurring_is_notified_once_a_day_not_every_cycle()
    {
        Judge(Verdict.Analyze);
        var service = Service();
        await service.RunOnceAsync();

        for (var cycle = 0; cycle < 6; cycle++)
        {
            Wait(5);
            await service.RunOnceAsync();
        }

        Assert.Single(_channel.Sent);

        Wait(24 * 60);
        await service.RunOnceAsync();
        Wait(5);
        await service.RunOnceAsync();

        Assert.Equal(2, _channel.Sent.Count);
    }

    [Fact]
    public async Task The_repeated_notice_still_says_KNOWN_and_gives_the_solution()
    {
        Judge(Verdict.Known);
        var service = Service();
        await service.RunOnceAsync();
        var questionsAsked = _model.Questions.Count;

        Wait(24 * 60);
        await service.RunOnceAsync();

        // The error is not judged again; the notice is built from what was remembered.
        Assert.Equal(questionsAsked, _model.Questions.Count);
        Assert.Equal(2, _channel.Sent.Count);
        Assert.Equal(_channel.Sent[0].Lines, _channel.Sent[1].Lines);
    }

    [Fact]
    public async Task A_channel_that_is_down_is_a_warning_and_the_error_is_still_filed_and_recorded()
    {
        Judge(Verdict.Analyze);
        _channel.Down = true;

        var result = await Service().RunOnceAsync();

        Assert.Equal(0, result.Unreported);
        Assert.Single(_tracker.Issues);
        Assert.Equal(FakeIssueTracker.FirstIssueNumber, Assert.Single(_sink.Records).IssueNumber);
        Assert.Single(_store.Seen);
        Assert.Equal(LogLevel.Warning, Assert.Single(_logger.Warnings).Level);
    }

    [Fact]
    public async Task A_channel_that_throws_does_not_break_the_cycle()
    {
        Judge(Verdict.Analyze);
        _channel.Throw = new InvalidOperationException("the channel fell over");

        var result = await Service(events:
        [
            ReceiptError("a"),
            TestEvents.MessageOnly("Key store could not be opened", id: "b")
        ]).RunOnceAsync();

        Assert.Equal(2, result.Judged);
        Assert.Equal(2, _tracker.Issues.Count);
        Assert.Equal(2, _sink.Records.Count);
        Assert.Contains("failed in Fake", Assert.Single(_logger.Warnings).Message);
    }

    [Fact]
    public async Task A_notice_that_could_not_be_sent_goes_out_the_next_time_the_error_is_fetched()
    {
        Judge(Verdict.Analyze);
        _channel.Down = true;
        var service = Service();
        await service.RunOnceAsync();

        Wait(5);
        _channel.Down = false;
        await service.RunOnceAsync();

        Assert.Single(_channel.Sent);
        Assert.Single(_tracker.Issues);
    }

    [Fact]
    public async Task Nothing_unscrubbed_reaches_the_notice()
    {
        Judge(Verdict.Analyze);

        await Service(events:
        [
            TestEvents.MessageOnly("Could not sign for anna.svensson@example.com (900101-1234)")
        ]).RunOnceAsync();

        var notification = Assert.Single(_channel.Sent);
        Assert.DoesNotContain("anna.svensson@example.com", notification.Text);
        Assert.DoesNotContain("900101-1234", notification.Text);
    }

    [Fact]
    public async Task A_cycle_with_nothing_to_tell_sends_nothing()
    {
        Judge(Verdict.Analyze);
        var service = Service();
        await service.RunOnceAsync();

        Wait(5);
        await service.RunOnceAsync();

        Assert.Single(_channel.Sent);
    }
}
