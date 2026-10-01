namespace CLog.Storage;

/// <summary>The issue filed for a fingerprint, and when the issue last heard about it.</summary>
public sealed record IssueLink(int Number, DateTimeOffset LastReportedAt);
