namespace Watcher.Model;

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
    public override string ToString() => Hash;
}
