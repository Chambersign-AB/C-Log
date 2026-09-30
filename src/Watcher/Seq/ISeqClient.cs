using Watcher.Model;

namespace Watcher.Seq;

public interface ISeqClient
{
    /// <summary>Fetches Error-level events logged at or after <paramref name="since"/>.</summary>
    Task<IReadOnlyList<SeqEvent>> GetErrorEventsAsync(DateTimeOffset since, CancellationToken cancellationToken = default);
}
