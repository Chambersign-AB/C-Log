using CLog.Model;
using CLog.Seq;

namespace CLog.Tests.Fakes;

public sealed class FakeSeqClient(params SeqEvent[] events) : ISeqClient
{
    private readonly List<SeqEvent> _events = [.. events];
    private readonly List<DateTimeOffset> _sinces = [];

    public int CallCount { get; private set; }
    public DateTimeOffset? LastSince { get; private set; }

    /// <summary>The start of every fetch window asked for, in order.</summary>
    public IReadOnlyList<DateTimeOffset> Sinces => _sinces;

    /// <summary>When set, only events stamped at or after the requested time come back, as from Seq itself.</summary>
    public bool HonourSince { get; set; }

    /// <summary>How many of the coming fetches fail, standing in for an unreachable Seq.</summary>
    public int FailNextFetches { get; set; }

    public void Add(SeqEvent seqEvent) => _events.Add(seqEvent);

    public Task<IReadOnlyList<SeqEvent>> GetErrorEventsAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastSince = since;
        _sinces.Add(since);

        if (FailNextFetches > 0)
        {
            FailNextFetches--;
            throw new SeqUnavailableException("Seq is down");
        }

        IReadOnlyList<SeqEvent> result = HonourSince
            ? [.. _events.Where(e => e.Timestamp >= since)]
            : [.. _events];
        return Task.FromResult(result);
    }
}
