using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Watcher.Model;

namespace Watcher.Output;

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

    public ConsoleAndFileTriageSink(string path, ILogger<ConsoleAndFileTriageSink> logger)
    {
        _path = Path.GetFullPath(path);
        _logger = logger;

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task WriteAsync(TriageRecord record, CancellationToken cancellationToken = default)
    {
        LogToConsole(record);

        var line = JsonSerializer.Serialize(record, JsonOptions);

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(_path, line + Environment.NewLine, Encoding.UTF8, cancellationToken);
        }
        catch (IOException ex)
        {
            // The console already has the result; losing the file line is not worth a crash.
            _logger.LogError(ex, "Could not append to {Path}", _path);
        }
        finally
        {
            _writeGate.Release();
        }
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
