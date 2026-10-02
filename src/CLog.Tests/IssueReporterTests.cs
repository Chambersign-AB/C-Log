using Microsoft.Extensions.Options;
using CLog.Analysis;
using CLog.Configuration;
using CLog.Knowledge;
using CLog.Model;
using CLog.Tests.Fakes;
using CLog.Triage;

namespace CLog.Tests;

/// <summary>
/// Step two inside a cycle: the real service, analyst and issue reporter, with the models,
/// the source repository and GitHub faked at the edges.
/// </summary>
public class IssueReporterTests
{
    private const string Signer = "src/CSign.Core/Signer.cs";

    private const string KnownErrorsText =
        """
        ## Deadlock when saving an order
        **Solution** Restart the sync job.
        """;

    private readonly ManualTimeProvider _time = new();
    private readonly InMemoryFingerprintStore _store = new();
    private readonly RecordingTriageSink _sink = new();
    private readonly FakeIssueTracker _tracker = new();
    private readonly FakeAnalysisModel _analysisModel = new();
    private readonly RecordingOllamaClient _triageModel = new();
    private readonly FakeSourceRepository _repository = new FakeSourceRepository().WithNumberedFile(Signer, 200);

    private static SeqEvent SigningError(string id = "event-1") => TestEvents.Error(
        id: id,
        template: "",
        rendered: "Could not sign for anna.svensson@example.com",
        exception:
        "System.InvalidOperationException: signing failed for 900101-1234\n"
        + @"   at CSign.Core.Signer.Apply(Document document) in C:\agent\_work\1\s\src\CSign.Core\Signer.cs:line 120");

    private static bool IsKnownQuestion(string prompt) => prompt.Contains("Kunskapsbas", StringComparison.Ordinal);

    /// <summary>The triage model's answers that lead to each verdict.</summary>
    private void TriageAnswers(Verdict verdict) => _triageModel.Reply = verdict switch
    {
        Verdict.Known => _ => "Ja",
        Verdict.Noise => prompt => IsKnownQuestion(prompt) ? "Nej" : "Ja",
        _ => _ => "Nej"
    };

    private TriageService Service(string rulesJson = """{ "ignore": [] }""", params SeqEvent[] events)
    {
        // Model mode: these tests cover the analysed issue. Context mode has its own tests.
        var options = Options.Create(new CLogOptions
        {
            Analysis = new AnalysisOptions { Enabled = true, Mode = AnalysisMode.Model }
        });
        var analyst = new Analyst(
            new CodeContextResolver(_repository, options), _analysisModel, options, new TestLogger<Analyst>());
        var reporter = new IssueReporter(analyst, _tracker, _store, options, new TestLogger<IssueReporter>());

        return new TriageService(
            new FakeSeqClient(events.Length == 0 ? [SigningError()] : events),
            _store,
            _triageModel,
            _sink,
            new StaticKnowledgeSource(KnownErrorsText),
            new StaticRuleSetProvider(RuleSet.Parse(rulesJson)),
            options,
            new TestLogger<TriageService>(),
            _time,
            reporter);
    }

    private void Wait(int minutes) => _time.Tick(TimeSpan.FromMinutes(minutes));

    [Fact]
    public async Task An_error_judged_ANALYZE_is_filed_as_one_issue_with_the_error_and_the_analysis()
    {
        TriageAnswers(Verdict.Analyze);

        await Service().RunOnceAsync();

        var issue = Assert.Single(_tracker.Issues);
        Assert.StartsWith("System.InvalidOperationException", issue.Title);
        Assert.Contains("Exception type: System.InvalidOperationException", issue.Body);
        Assert.Contains("Top application frame: CSign.Core.Signer.Apply(Document document)", issue.Body);
        Assert.Contains(FakeAnalysisModel.DefaultAnswer, issue.Body);
        Assert.Contains($"`{Signer}` lines 80–160", issue.Body);
        Assert.Single(_analysisModel.Prompts);
    }

    [Fact]
    public async Task The_issue_number_is_kept_on_the_fingerprint_and_in_the_triage_record()
    {
        TriageAnswers(Verdict.Analyze);

        await Service().RunOnceAsync();

        var record = Assert.Single(_sink.Records);
        Assert.Equal(Verdict.Analyze, record.Verdict);
        Assert.Equal(FakeIssueTracker.FirstIssueNumber, record.IssueNumber);
        Assert.Equal(FakeIssueTracker.FirstIssueNumber, _store.Issues[record.Fingerprint].Number);
    }

    [Fact]
    public async Task Nothing_unscrubbed_reaches_the_issue()
    {
        TriageAnswers(Verdict.Analyze);

        await Service().RunOnceAsync();

        var issue = Assert.Single(_tracker.Issues);
        foreach (var secret in new[] { "anna.svensson@example.com", "900101-1234" })
        {
            Assert.DoesNotContain(secret, issue.Title);
            Assert.DoesNotContain(secret, issue.Body);
        }
    }

    [Theory]
    [InlineData(Verdict.Noise)]
    [InlineData(Verdict.Known)]
    public async Task An_error_judged_NOISE_or_KNOWN_is_neither_analysed_nor_filed(Verdict verdict)
    {
        TriageAnswers(verdict);

        await Service().RunOnceAsync();

        var record = Assert.Single(_sink.Records);
        Assert.Equal(verdict, record.Verdict);
        Assert.Null(record.IssueNumber);
        Assert.Empty(_analysisModel.Prompts);
        Assert.Empty(_repository.Reads);
        Assert.Empty(_tracker.Issues);
        Assert.Empty(_tracker.Comments);
    }

    [Fact]
    public async Task An_error_the_triage_model_could_not_judge_is_not_filed()
    {
        _triageModel.Reply = _ => null;

        await Service().RunOnceAsync();

        Assert.Equal(TriageOutcome.JudgementFailed, Assert.Single(_sink.Records).Outcome);
        Assert.Empty(_analysisModel.Prompts);
        Assert.Empty(_tracker.Issues);
    }

    [Fact]
    public async Task An_error_a_rule_filters_is_not_filed()
    {
        TriageAnswers(Verdict.Analyze);

        await Service("""{ "ignore": [ { "id": "quiet", "exceptionType": "System.InvalidOperationException" } ] }""")
            .RunOnceAsync();

        Assert.Empty(_analysisModel.Prompts);
        Assert.Empty(_tracker.Issues);
    }

    [Fact]
    public async Task An_error_filed_without_an_analysis_says_so_rather_than_staying_unfiled()
    {
        TriageAnswers(Verdict.Analyze);
        _analysisModel.Answer = null;

        await Service().RunOnceAsync();

        Assert.Contains("gave no analysis", Assert.Single(_tracker.Issues).Body);
    }

    [Fact]
    public async Task A_recurring_error_gets_a_comment_on_its_issue_not_a_second_issue()
    {
        TriageAnswers(Verdict.Analyze);
        var service = Service();
        await service.RunOnceAsync();

        Wait(24 * 60);
        await service.RunOnceAsync();

        Assert.Single(_tracker.Issues);
        var comment = Assert.Single(_tracker.Comments);
        Assert.Equal(FakeIssueTracker.FirstIssueNumber, comment.IssueNumber);
        Assert.Contains("Seen again: 1 occurrence(s)", comment.Body);
        Assert.Single(_analysisModel.Prompts);
    }

    [Fact]
    public async Task An_error_that_keeps_recurring_is_commented_on_once_per_interval_not_every_cycle()
    {
        TriageAnswers(Verdict.Analyze);
        var service = Service();
        await service.RunOnceAsync();

        for (var cycle = 0; cycle < 6; cycle++)
        {
            Wait(5);
            await service.RunOnceAsync();
        }

        Assert.Empty(_tracker.Comments);

        Wait(24 * 60);
        await service.RunOnceAsync();
        Wait(5);
        await service.RunOnceAsync();

        Assert.Single(_tracker.Comments);
    }

    [Fact]
    public async Task A_comment_that_could_not_be_posted_is_posted_on_a_later_cycle()
    {
        TriageAnswers(Verdict.Analyze);
        var service = Service();
        await service.RunOnceAsync();

        Wait(24 * 60);
        _tracker.Down = true;
        await service.RunOnceAsync();
        Assert.Empty(_tracker.Comments);

        Wait(5);
        _tracker.Down = false;
        await service.RunOnceAsync();

        Assert.Single(_tracker.Comments);
    }

    [Fact]
    public async Task With_github_down_the_error_is_not_marked_seen_and_is_filed_once_github_is_back()
    {
        TriageAnswers(Verdict.Analyze);
        _tracker.Down = true;
        var service = Service();

        var first = await service.RunOnceAsync();

        Assert.Equal(1, first.Unreported);
        Assert.Empty(_store.Seen);
        Assert.Empty(_store.Issues);
        Assert.Empty(_sink.Records);

        Wait(5);
        _tracker.Down = false;
        await service.RunOnceAsync();

        Assert.Single(_tracker.Issues);
        Assert.Single(_store.Seen);
        Assert.Equal(FakeIssueTracker.FirstIssueNumber, Assert.Single(_sink.Records).IssueNumber);
    }

    [Fact]
    public async Task An_issue_filed_just_before_the_result_could_not_be_written_is_not_filed_twice()
    {
        TriageAnswers(Verdict.Analyze);
        _sink.FailNextWrites = 1;
        var service = Service();

        await service.RunOnceAsync();

        Assert.Single(_tracker.Issues);
        Assert.Empty(_store.Seen);
        var questionsAsked = _triageModel.Questions.Count;

        Wait(5);
        await service.RunOnceAsync();

        // The issue is remembered apart from "seen", so neither model is asked again.
        Assert.Single(_tracker.Issues);
        Assert.Single(_analysisModel.Prompts);
        Assert.Equal(questionsAsked, _triageModel.Questions.Count);
        Assert.Equal(FakeIssueTracker.FirstIssueNumber, Assert.Single(_sink.Records).IssueNumber);
        Assert.Single(_store.Seen);
    }

    [Fact]
    public async Task Two_different_errors_get_an_issue_each()
    {
        TriageAnswers(Verdict.Analyze);

        await Service(events:
        [
            SigningError("a"),
            TestEvents.MessageOnly("Key store could not be opened", id: "b")
        ]).RunOnceAsync();

        Assert.Equal(2, _tracker.Issues.Count);
        Assert.Equal(
            new int?[] { FakeIssueTracker.FirstIssueNumber, FakeIssueTracker.FirstIssueNumber + 1 },
            _sink.Records.Select(r => r.IssueNumber));
    }
}
