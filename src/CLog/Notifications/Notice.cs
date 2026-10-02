using CLog.Storage;

namespace CLog.Notifications;

/// <summary>One error's line in a notification, with what has to be remembered once it is sent.</summary>
public sealed record Notice(string Fingerprint, NoticeState State, string Line);
