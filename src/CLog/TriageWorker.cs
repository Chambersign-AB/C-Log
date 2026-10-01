using Microsoft.Extensions.Options;
using CLog.Configuration;
using CLog.Seq;
using CLog.Storage;
using CLog.Triage;

namespace CLog;

/// <summary>
/// Runs a triage cycle on an interval. A cycle that throws is logged and the next one runs
/// as usual: an unreachable Seq or Ollama is a normal condition, not a reason to stop.
/// </summary>
public sealed class TriageWorker(
    TriageService triage,
    IFingerprintStore store,
    IOptions<CLogOptions> options,
    ILogger<TriageWorker> logger,
    TimeProvider timeProvider) : BackgroundService
{
    private readonly CLogOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));

        await store.InitializeAsync(stoppingToken);
        logger.LogInformation(
            "CLog started. Polling {SeqUrl} every {Interval}, judging with {Model} at {OllamaUrl} in {Mode} mode, at most {Max} judgement(s) per cycle",
            _options.Seq.Url, interval, _options.Ollama.Model, _options.Ollama.Url, _options.Triage.Mode, _options.MaxJudgementsPerRun);

        if (_options.Analysis.Enabled)
        {
            logger.LogInformation(
                "Step two is on: ANALYZE errors are read against {RepoPath}, analysed with {Model} and filed in {Repository}",
                _options.Analysis.RepoPath, _options.Analysis.Model, _options.Analysis.GitHub.Repository);
        }

        using var timer = new PeriodicTimer(interval, timeProvider);

        // One cycle at a time. A slow model can make a cycle outlast the interval; the poll
        // that falls due meanwhile is skipped and said so, rather than queued up behind it.
        Task? cycle = null;

        do
        {
            if (cycle is { IsCompleted: false })
            {
                logger.LogWarning("Cycle still running, skipping poll");
                continue;
            }

            cycle = RunCycleAsync(interval, stoppingToken);
        }
        while (await SafeWaitAsync(timer, stoppingToken));

        if (cycle is not null)
        {
            await cycle;
        }

        logger.LogInformation("CLog stopped");
    }

    private async Task RunCycleAsync(TimeSpan interval, CancellationToken stoppingToken)
    {
        try
        {
            await triage.RunOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down. The error being judged is not marked seen, so the next start picks it up.
        }
        catch (SeqUnavailableException ex)
        {
            // Expected whenever Seq restarts or the network blips. One line, no stack.
            logger.LogWarning("{Reason}. Retrying in {Interval}", ex.Message, interval);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Triage cycle failed; retrying in {Interval}", interval);
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
