using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using CLog.Model;

namespace CLog.Output;

/// <summary>
/// Writes every result to the console and appends it to triage.jsonl, one JSON object per
/// line so the file can be tailed, grepped and replayed without a parser.
/// </summary>
public sealed class ConsoleAndFileTriageSink : ITriageSink
{
    private readonly string _path;
    private readonly ILogger<ConsoleAndFileTriageSink> _logger;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false
    };

    /// <summary>One try and three retries.</summary>
    private const int Attempts = 4;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <param name="delay">How to wait between attempts. Replaced in tests so they need not wait.</param>
    public ConsoleAndFileTriageSink(
        string path,
        ILogger<ConsoleAndFileTriageSink> logger,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _path = Path.GetFullPath(path);
        _logger = logger;
        _delay = delay ?? Task.Delay;

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task<bool> WriteAsync(TriageRecord record, CancellationToken cancellationToken = default)
    {
        LogToConsole(record);

        var line = JsonSerializer.Serialize(record, JsonOptions);

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await AppendAsync(line, cancellationToken);
                    return true;
                }
                catch (IOException) when (attempt < Attempts)
                {
                    // Usually another program holding the file for a moment: an editor, a
                    // backup, a tail that locks what it reads. Worth waiting out.
                    await _delay(RetryDelay, cancellationToken);
                }
                catch (IOException ex)
                {
                    // The console has the result, but the file is the record. Saying so lets
                    // the caller keep the error new instead of remembering it as reported.
                    _logger.LogError(
                        ex,
                        "Could not append to {Path} after {Attempts} attempts; the result is not recorded",
                        _path,
                        Attempts);
                    return false;
                }
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task AppendAsync(string line, CancellationToken cancellationToken)
    {
        // FileShare.Read: others may read the file while a line is appended, nobody else may write.
        await using var stream = new FileStream(
            _path, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteAsync((line + Environment.NewLine).AsMemory(), cancellationToken);
    }

    private void LogToConsole(TriageRecord record)
    {
        var headline = string.IsNullOrEmpty(record.ExceptionType)
            ? record.Template
            : $"{record.ExceptionType}: {record.Template}";

        switch (record.Outcome)
        {
            case TriageOutcome.FilteredByRule:
                _logger.LogInformation(
                    "[{Fingerprint}] IGNORED by rule {RuleId} (x{Count}) {Headline}",
                    record.Fingerprint, record.RuleId, record.OccurrenceCount, headline);
                break;

            case TriageOutcome.JudgementFailed:
                _logger.LogWarning(
                    "[{Fingerprint}] UNJUDGED (x{Count}) {Headline} - {Reason}",
                    record.Fingerprint, record.OccurrenceCount, headline, record.Reason);
                break;

            case TriageOutcome.Judged when record.Verdict == Model.Verdict.Analyze:
                _logger.LogWarning(
                    "[{Fingerprint}] ANALYZE (x{Count}) {Headline}\n    at {TopFrame}\n    {Reason}",
                    record.Fingerprint, record.OccurrenceCount, headline, record.TopFrame, record.Reason);
                break;

            case TriageOutcome.Judged when record.Verdict == Model.Verdict.Known:
                _logger.LogInformation(
                    "[{Fingerprint}] KNOWN (x{Count}) {Headline}\n    {Reason}\n    Fix: {KnownSolution}",
                    record.Fingerprint, record.OccurrenceCount, headline, record.Reason, record.KnownSolution);
                break;

            default:
                _logger.LogInformation(
                    "[{Fingerprint}] NOISE (x{Count}) {Headline} - {Reason}",
                    record.Fingerprint, record.OccurrenceCount, headline, record.Reason);
                break;
        }
    }
}
