namespace CLog.Notifications;

/// <summary>What one cycle has to tell: a subject and one line per error.</summary>
public sealed record Notification(string Subject, IReadOnlyList<string> Lines)
{
    public string Text => string.Join("\n", Lines);
}
