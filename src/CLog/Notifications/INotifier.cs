namespace CLog.Notifications;

/// <summary>One channel a notice can be sent through.</summary>
public interface INotifier
{
    /// <summary>The channel's name, for the log.</summary>
    string Name { get; }

    /// <summary>
    /// Sends the notice. Returns false when it could not be delivered; a channel that is down
    /// is a normal condition and must not throw for it.
    /// </summary>
    Task<bool> SendAsync(Notification notification, CancellationToken cancellationToken = default);
}
