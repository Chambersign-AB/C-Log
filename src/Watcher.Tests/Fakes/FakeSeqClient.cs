using Watcher.Model;
using Watcher.Seq;

namespace Watcher.Tests.Fakes;

public sealed class FakeSeqClient(params SeqEvent[] events) : ISeqClient
{
    private readonly List<SeqEvent> _events = [.. events];

    public int CallCount { get; private set; }
    public DateTimeOffset? LastSince { get; private set; }

    public Task<IReadOnlyList<SeqEvent>> GetErrorEventsAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastSince = since;
        return Task.FromResult<IReadOnlyList<SeqEvent>>(_events);
    }
}
