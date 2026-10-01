using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CLog.Configuration;
using CLog.Knowledge;
using CLog.Model;
using CLog.Ollama;
using CLog.Output;
using CLog.Seq;
using CLog.Storage;

namespace CLog.Triage;

/// <summary>
/// One polling cycle: fetch errors, scrub them, group them by fingerprint, drop the ones a
/// rule covers, and ask the model about the ones never seen before.
/// </summary>
public sealed class TriageService(
    ISeqClient seq,
    IFingerprintStore store,
    IOllamaClient ollama,
    ITriageSink sink,
    IKnowledgeSource knowledge,
    IRuleSetProvider rules,
    IOptions<CLogOptions> options,
    ILogger<TriageService> logger,
    TimeProvider? time = null)
{
    private readonly CLogOptions _options = options.Value;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly TwoStepJudge _twoStep = new(ollama, options.Value.Triage, logger);

    public async Task<TriageCycleResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var since = now.AddMinutes(-Math.Max(1, _options.LookbackMinutes));

        var events = await seq.GetErrorEventsAsync(since, cancellationToken);
        if (events.Count == 0)
        {
            logger.LogInformation("No error events since {Since:u}", since);
            return new TriageCycleResult();
        }

        var groups = ErrorGroup.FromEvents(events);
        var ruleSet = rules.Current;
        var knownErrors = knowledge.Read();
        var budget = Math.Max(1, _options.MaxJudgementsPerRun);

        var filtered = 0;
        var judged = 0;
        var failures = 0;
        var deferred = 0;

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Rules come first, so an error the team has already chosen to ignore never costs
            // an AI call and never counts against the per-cycle budget.
            var rule = ruleSet.FirstMatch(group.Sample, group.Fingerprint);
            if (rule is not null)
            {
                filtered++;
                if (!await CountIfSeenAsync(group.Fingerprint.Hash, now, cancellationToken))
                {
                    await sink.WriteAsync(TriageReports.Filtered(group, rule, now), cancellationToken);
                    await MarkSeenAsync(group.Fingerprint.Hash, now);
                }

                continue;
            }

            if (judged >= budget)
            {
                // Deliberately left unmarked: an error deferred here is still new next cycle.
                deferred++;
                continue;
            }

            if (await CountIfSeenAsync(group.Fingerprint.Hash, now, cancellationToken))
            {
                continue;
            }

            judged++;
            var report = TriageReports.BuildErrorReport(group);
            var verdict = _options.Triage.Mode == TriageMode.SingleCall
                ? await ollama.JudgeAsync(report, knownErrors, cancellationToken)
                : await _twoStep.JudgeAsync(report, knownErrors, cancellationToken);
            if (verdict is null)
            {
                failures++;
            }

            await sink.WriteAsync(TriageReports.Judged(group, verdict, now), cancellationToken);
            await MarkSeenAsync(group.Fingerprint.Hash, now);
        }

        if (deferred > 0)
        {
            logger.LogWarning(
                "Judgement budget of {Budget} spent; {Deferred} error group(s) left for the next cycle",
                budget, deferred);
        }

        logger.LogInformation(
            "Cycle done: {Events} event(s), {Groups} fingerprint(s), {Filtered} filtered, {Judged} judged, {Failures} unjudged",
            events.Count, groups.Count, filtered, judged, failures);

        return new TriageCycleResult
        {
            EventsFetched = events.Count,
            Fingerprints = groups.Count,
            FilteredByRules = filtered,
            Judged = judged,
            JudgementFailures = failures,
            Deferred = deferred
        };
    }

    /// <summary>Counts another sighting of a fingerprint that already has an outcome.</summary>
    private async Task<bool> CountIfSeenAsync(string fingerprint, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!await store.IsSeenAsync(fingerprint, cancellationToken))
        {
            return false;
        }

        await store.TryMarkSeenAsync(fingerprint, now, cancellationToken);
        return true;
    }

    /// <summary>
    /// A fingerprint is marked seen only once its outcome is written. A judgement can take
    /// minutes, and marking first meant a restart or a fault in that time left the error seen
    /// but never reported. Not cancellable: a shutdown arriving between the write and the mark
    /// would report the error twice. The worst case now is a duplicate line, never a lost one.
    /// </summary>
    private Task MarkSeenAsync(string fingerprint, DateTimeOffset now) =>
        store.TryMarkSeenAsync(fingerprint, now, CancellationToken.None);
}
