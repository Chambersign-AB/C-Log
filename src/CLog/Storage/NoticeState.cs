using CLog.Model;

namespace CLog.Storage;

/// <summary>
/// What a notice about a fingerprint has to say and when one was last sent. The verdict and
/// the solution are kept because an error that comes back is not judged again, and the
/// notice about it still has to say what it is.
/// </summary>
/// <param name="LastNotifiedAt">Null until a notice has actually gone out.</param>
public sealed record NoticeState(Verdict Verdict, string? Solution, DateTimeOffset? LastNotifiedAt);
