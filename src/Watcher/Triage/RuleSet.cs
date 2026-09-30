using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Watcher.Model;

namespace Watcher.Triage;

/// <summary>
/// One ignore rule. Every condition that is set must match; conditions left out are not
/// considered. A rule with no condition at all never matches, so a typo cannot silence
/// the whole error stream.
/// </summary>
public sealed class IgnoreRule
{
    public string Id { get; set; } = "";

    /// <summary>Why this error is ignored. Written for the next person reading rules.json.</summary>
    public string Reason { get; set; } = "";

    /// <summary>Exact exception type, e.g. "System.OperationCanceledException".</summary>
    public string? ExceptionType { get; set; }

    /// <summary>Case-insensitive substring of the rendered message.</summary>
    public string? MessageContains { get; set; }

    /// <summary>Regular expression matched against the rendered message.</summary>
    public string? MessageMatches { get; set; }

    /// <summary>Case-insensitive substring of the topmost non-framework stack frame.</summary>
    public string? TopFrameContains { get; set; }

    /// <summary>An exact fingerprint hash, for silencing one specific error.</summary>
    public string? Fingerprint { get; set; }
}

public sealed class RuleSet
{
    public int Version { get; set; } = 1;

    [JsonPropertyName("ignore")]
    public List<IgnoreRule> Ignore { get; set; } = [];

    public static RuleSet Empty => new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static RuleSet Parse(string json) =>
        string.IsNullOrWhiteSpace(json)
            ? Empty
            : JsonSerializer.Deserialize<RuleSet>(json, JsonOptions) ?? Empty;

    public static RuleSet Load(string path) =>
        File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;

    /// <summary>The first rule that matches, or null when the error should go on to the model.</summary>
    public IgnoreRule? FirstMatch(SeqEvent logEvent, ErrorFingerprint fingerprint)
    {
        foreach (var rule in Ignore)
        {
            if (Matches(rule, logEvent, fingerprint))
            {
                return rule;
            }
        }

        return null;
    }

    private static bool Matches(IgnoreRule rule, SeqEvent logEvent, ErrorFingerprint fingerprint)
    {
        var hasCondition = false;

        if (!string.IsNullOrWhiteSpace(rule.Fingerprint))
        {
            hasCondition = true;
            if (!string.Equals(rule.Fingerprint, fingerprint.Hash, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(rule.ExceptionType))
        {
            hasCondition = true;
            if (!string.Equals(rule.ExceptionType, fingerprint.ExceptionType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(rule.MessageContains))
        {
            hasCondition = true;
            if (!logEvent.RenderedMessage.Contains(rule.MessageContains, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(rule.MessageMatches))
        {
            hasCondition = true;
            if (!SafeIsMatch(rule.MessageMatches, logEvent.RenderedMessage))
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(rule.TopFrameContains))
        {
            hasCondition = true;
            if (!fingerprint.TopFrame.Contains(rule.TopFrameContains, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return hasCondition;
    }

    /// <summary>A malformed or runaway pattern in rules.json must not take the service down.</summary>
    private static bool SafeIsMatch(string pattern, string input)
    {
        try
        {
            return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
