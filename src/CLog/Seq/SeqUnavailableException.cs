namespace CLog.Seq;

/// <summary>
/// Seq could not be read this cycle. A separate type so the worker can report it as the
/// routine condition it is — one warning line — instead of a stack trace every five minutes,
/// while a genuine bug still surfaces as an error with its full detail.
/// </summary>
public sealed class SeqUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
