namespace CLog.Model;

/// <summary>A single log event as fetched from Seq, flattened to what triage needs.</summary>
public sealed record SeqEvent
{
    public string Id { get; init; } = "";
    public DateTimeOffset Timestamp { get; init; }
    public string Level { get; init; } = "";

    /// <summary>The message template ("Order {OrderId} failed") when Seq supplies one.</summary>
    public string MessageTemplate { get; init; } = "";

    /// <summary>The template with values substituted in.</summary>
    public string RenderedMessage { get; init; } = "";

    /// <summary>Raw exception text, type and stack trace included, as Seq stores it.</summary>
    public string? Exception { get; init; }

    public IReadOnlyDictionary<string, string?> Properties { get; init; } =
        new Dictionary<string, string?>();
}
