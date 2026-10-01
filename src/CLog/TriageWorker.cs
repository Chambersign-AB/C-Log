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

        using var timer = new PeriodicTimer(interval, timeProvider);

        do
        {
            try
            {
                await triage.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
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
        while (await SafeWaitAsync(timer, stoppingToken));

        logger.LogInformation("CLog stopped");
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
