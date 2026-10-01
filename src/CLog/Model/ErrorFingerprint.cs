namespace CLog.Model;

/// <summary>
/// Identity of an error class. Built only from parts that stay the same between two
/// occurrences of the same bug, so it survives differing timestamps, ids and personal data.
/// </summary>
public sealed record ErrorFingerprint(
    string Hash,
    string ExceptionType,
    string NormalizedTemplate,
    string TopFrame)
{
    /// <summary>
    /// The hash this error had while the message template was part of every identity. Null
    /// when it is no different from <see cref="Hash"/>. Lets what was already seen or filed
    /// under the old hash be recognised instead of reported a second time.
    /// </summary>
    public string? PreviousHash { get; init; }

    public override string ToString() => Hash;
}
