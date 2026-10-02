using CLog.Notifications;

namespace CLog.Tests.Fakes;

/// <summary>A channel that only remembers what it was asked to send.</summary>
public sealed class FakeNotifier : INotifier
{
    private readonly List<Notification> _sent = [];

    public IReadOnlyList<Notification> Sent => _sent;

    public string Name => "Fake";

    /// <summary>True stands for a channel that is down: nothing is sent and the caller is told so.</summary>
    public bool Down { get; set; }

    /// <summary>Set to throw instead, standing in for a channel that fails in a way nobody planned for.</summary>
    public Exception? Throw { get; set; }

    public Task<bool> SendAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        if (Throw is not null)
        {
            throw Throw;
        }

        if (Down)
        {
            return Task.FromResult(false);
        }

        _sent.Add(notification);
        return Task.FromResult(true);
    }
}
