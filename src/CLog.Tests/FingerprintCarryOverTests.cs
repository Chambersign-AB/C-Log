using Microsoft.Extensions.Options;
using CLog.Analysis;
using CLog.Configuration;
using CLog.Knowledge;
using CLog.Model;
using CLog.Tests.Fakes;
using CLog.Triage;

namespace CLog.Tests;

/// <summary>
/// The store holds hashes made while the template was part of every identity. An error seen
/// or filed under such a hash must not come back as new now that the hash has changed.
/// </summary>
public class FingerprintCarryOverTests
{
    private readonly ManualTimeProvider _time = new();
    private readonly InMemoryFingerprintStore _store = new();
    private readonly RecordingTriageSink _sink = new();
    private readonly FakeIssueTracker _tracker = new();
    private readonly FakeAnalysisModel _analysisModel = new();
    private readonly RecordingOllamaClient _triageModel = new() { Reply = _ => "Nej" };

    private static SeqEvent Error(string template = "HTTP {RequestMethod} {RequestPath} responded {StatusCode}") =>
        TestEvents.Error(template: template);

    private static ErrorFingerprint FingerprintOf(SeqEvent logEvent) =>
        Fingerprinter.Compute(Sanitizer.ScrubEvent(logEvent));

    private TriageService Service(params SeqEvent[] events)
    {
        var options = Options.Create(new CLogOptions { Analysis = new AnalysisOptions { Enabled = true } });
        var analyst = new Analyst(
            new CodeContextResolver(new FakeSourceRepository(), options), _analysisModel, options, new TestLogger<Analyst>());
        var reporter = new IssueReporter(analyst, _tracker, _store, options, new TestLogger<IssueReporter>());

        return new TriageService(
            new FakeSeqClient(events),
            _store,
            _triageModel,
            _sink,
            new StaticKnowledgeSource("# Known errors"),
            new StaticRuleSetProvider(RuleSet.Parse("""{ "ignore": [] }""")),
            options,
            new TestLogger<TriageService>(),
            _time,
            reporter);
    }

    [Fact]
    public async Task An_error_seen_under_its_old_hash_is_not_judged_again()
    {
        var fingerprint = FingerprintOf(Error());
        await _store.TryMarkSeenAsync(fingerprint.PreviousHash!, _time.GetUtcNow());

        await Service(Error()).RunOnceAsync();

        Assert.Empty(_triageModel.Questions);
        Assert.Empty(_sink.Records);
        Assert.True(await _store.IsSeenAsync(fingerprint.Hash));
    }

    [Fact]
    public async Task An_error_filed_under_its_old_hash_is_not_filed_again()
    {
        var fingerprint = FingerprintOf(Error());
        await _store.TryMarkSeenAsync(fingerprint.PreviousHash!, _time.GetUtcNow());
        await _store.SaveIssueAsync(fingerprint.PreviousHash!, 389, _time.GetUtcNow());

        await Service(Error()).RunOnceAsync();

        Assert.Empty(_tracker.Issues);
        Assert.Empty(_analysisModel.Prompts);
        Assert.Equal(389, _store.Issues[fingerprint.Hash].Number);
    }

    [Fact]
    public async Task When_it_recurs_the_comment_goes_to_the_issue_filed_under_the_old_hash()
    {
        var fingerprint = FingerprintOf(Error());
        await _store.TryMarkSeenAsync(fingerprint.PreviousHash!, _time.GetUtcNow());
        await _store.SaveIssueAsync(fingerprint.PreviousHash!, 389, _time.GetUtcNow());
        _time.Tick(TimeSpan.FromDays(2));

        await Service(Error()).RunOnceAsync();

        Assert.Empty(_tracker.Issues);
        Assert.Equal(389, Assert.Single(_tracker.Comments).IssueNumber);
    }

    [Fact]
    public async Task An_error_never_seen_under_either_hash_is_judged_and_filed_as_usual()
    {
        await Service(Error()).RunOnceAsync();

        Assert.NotEmpty(_triageModel.Questions);
        Assert.Single(_tracker.Issues);
        Assert.Single(_sink.Records);
    }

    [Fact]
    public async Task One_exception_logged_under_two_templates_is_filed_as_one_issue()
    {
        await Service(
            Error("HTTP {RequestMethod} {RequestPath} responded {StatusCode}"),
            Error("An unhandled exception has occurred while executing the request.")).RunOnceAsync();

        Assert.Single(_tracker.Issues);
        Assert.Equal(2, Assert.Single(_sink.Records).OccurrenceCount);
    }
}
