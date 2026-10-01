using CLog.Model;

namespace CLog.Seq;

public interface ISeqClient
{
    /// <summary>Fetches Error-level events logged at or after <paramref name="since"/>.</summary>
    Task<IReadOnlyList<SeqEvent>> GetErrorEventsAsync(DateTimeOffset since, CancellationToken cancellationToken = default);
}
