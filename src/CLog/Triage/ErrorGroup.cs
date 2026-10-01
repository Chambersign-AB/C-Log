using CLog.Model;

namespace CLog.Triage;

/// <summary>Every event in one cycle that shares a fingerprint, plus one scrubbed sample.</summary>
public sealed class ErrorGroup(ErrorFingerprint fingerprint, SeqEvent sample)
{
    public ErrorFingerprint Fingerprint { get; } = fingerprint;

    /// <summary>A representative event, already scrubbed of personal data.</summary>
    public SeqEvent Sample { get; } = sample;

    /// <summary>How many events in this cycle shared the fingerprint.</summary>
    public int Count { get; set; } = 1;

    /// <summary>
    /// Scrubs first, then groups. Personal data never reaches the fingerprint, the store,
    /// the model or the output, and one error arrives in one group however many people it
    /// happened to.
    /// </summary>
    public static List<ErrorGroup> FromEvents(IReadOnlyList<SeqEvent> events)
    {
        var groups = new Dictionary<string, ErrorGroup>(StringComparer.Ordinal);

        foreach (var rawEvent in events)
        {
            var scrubbed = Sanitizer.ScrubEvent(rawEvent);
            var fingerprint = Fingerprinter.Compute(scrubbed);

            if (groups.TryGetValue(fingerprint.Hash, out var existing))
            {
                existing.Count++;
                continue;
            }

            groups[fingerprint.Hash] = new ErrorGroup(fingerprint, scrubbed);
        }

        return [.. groups.Values.OrderBy(g => g.Sample.Timestamp)];
    }
}
