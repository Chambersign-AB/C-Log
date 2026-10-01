using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CLog.Analysis;
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
    TimeProvider? time = null,
    IssueReporter? issues = null)
{
    private readonly CLogOptions _options = options.Value;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly TwoStepJudge _twoStep = new(ollama, options.Value.Triage, logger);

    /// <summary>
    /// Events reach Seq a little after they are stamped, so a fetch starting exactly where the
    /// last one ended would miss the ones still in transit. Fetching the overlap twice is
    /// harmless: fingerprints already handled are only counted.
    /// </summary>
    private static readonly TimeSpan FetchOverlap = TimeSpan.FromMinutes(1);

    /// <summary>Where the next fetch starts. Null until Seq has been read once since start.</summary>
    private DateTimeOffset? _fetchFrom;

    public async Task<TriageCycleResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();

        // The window rolls from the last fetch whose errors all got an outcome, however long
        // that cycle took; a fixed lookback dropped whatever was logged during a long cycle.
        // LookbackMinutes only decides how far back the first fetch after a start reaches.
        var since = _fetchFrom ?? now.AddMinutes(-Math.Max(1, _options.LookbackMinutes));

        var events = await seq.GetErrorEventsAsync(since, cancellationToken);

        // Pinned until this cycle completes: if it is cut short, or leaves errors behind, the
        // next fetch starts from the same place and picks them up again.
        _fetchFrom = since;

        if (events.Count == 0)
        {
            logger.LogInformation("No error events since {Since:u}", since);
            _fetchFrom = now - FetchOverlap;
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
        var unreported = 0;

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CarryOverAsync(group.Fingerprint, now, cancellationToken);

            // Rules come first, so an error the team has already chosen to ignore never costs
            // an AI call and never counts against the per-cycle budget.
            var rule = ruleSet.FirstMatch(group.Sample, group.Fingerprint);
            if (rule is not null)
            {
                filtered++;
                if (!await CountIfSeenAsync(group.Fingerprint.Hash, now, cancellationToken)
                    && !await ReportAsync(TriageReports.Filtered(group, rule, now), cancellationToken))
                {
                    unreported++;
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
                if (issues is not null)
                {
                    await issues.CommentIfDueAsync(group, now, cancellationToken);
                }

                continue;
            }

            judged++;
            var report = TriageReports.BuildErrorReport(group);
            TriageVerdict? verdict;

            // An issue without a seen fingerprint: it was filed, and then the result could not
            // be recorded. The issue stands; asking the models again would only file a second.
            var issue = issues is null
                ? null
                : (await store.GetIssueAsync(group.Fingerprint.Hash, cancellationToken))?.Number;
            if (issue is not null)
            {
                verdict = new TriageVerdict(Verdict.Analyze, $"Already filed as issue #{issue}.", null);
            }
            else
            {
                verdict = _options.Triage.Mode == TriageMode.SingleCall
                    ? await ollama.JudgeAsync(report, knownErrors, cancellationToken)
                    : await _twoStep.JudgeAsync(report, knownErrors, cancellationToken);
                if (verdict is null)
                {
                    failures++;
                }

                // Step two, for ANALYZE only: NOISE and KNOWN need nobody's time.
                if (issues is not null && verdict?.Verdict == Verdict.Analyze)
                {
                    issue = await issues.FileAsync(group, report, knownErrors, now, cancellationToken);
                    if (issue is null)
                    {
                        // Not filed, so not an outcome: left unseen and tried again next cycle.
                        unreported++;
                        continue;
                    }
                }
            }

            if (!await ReportAsync(TriageReports.Judged(group, verdict, now) with { IssueNumber = issue }, cancellationToken))
            {
                unreported++;
            }
        }

        if (deferred > 0)
        {
            logger.LogWarning(
                "Judgement budget of {Budget} spent; {Deferred} error group(s) left for the next cycle",
                budget, deferred);
        }

        if (unreported > 0)
        {
            logger.LogWarning(
                "{Unreported} result(s) could not be written or filed; those errors stay new and are reported again next cycle",
                unreported);
        }

        if (deferred == 0 && unreported == 0)
        {
            _fetchFrom = now - FetchOverlap;
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
            Deferred = deferred,
            Unreported = unreported
        };
    }

    /// <summary>
    /// Writes an outcome and, only if that worked, marks the fingerprint seen. An outcome
    /// nobody can read is not an outcome: the error stays new and is reported again.
    /// </summary>
    private async Task<bool> ReportAsync(TriageRecord record, CancellationToken cancellationToken)
    {
        if (!await sink.WriteAsync(record, cancellationToken))
        {
            return false;
        }

        await MarkSeenAsync(record.Fingerprint, record.At);
        return true;
    }

    /// <summary>
    /// An error with a stack trace used to be hashed with its message template. What was seen
    /// or filed under that hash is taken over by the new one, so changing the identity does
    /// not report every old error once more, or file a second issue for it.
    /// </summary>
    private async Task CarryOverAsync(ErrorFingerprint fingerprint, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (fingerprint.PreviousHash is not { } previous
            || await store.IsSeenAsync(fingerprint.Hash, cancellationToken))
        {
            return;
        }

        var issue = await store.GetIssueAsync(previous, cancellationToken);
        if (issue is not null && await store.GetIssueAsync(fingerprint.Hash, cancellationToken) is null)
        {
            await store.SaveIssueAsync(fingerprint.Hash, issue.Number, issue.LastReportedAt, cancellationToken);
        }

        if (await store.IsSeenAsync(previous, cancellationToken))
        {
            await store.TryMarkSeenAsync(fingerprint.Hash, now, cancellationToken);
        }
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
