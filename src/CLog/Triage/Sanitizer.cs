using System.Text.RegularExpressions;
using CLog.Model;

namespace CLog.Triage;

/// <summary>
/// Removes personal data before anything leaves the process. Nothing here is reversible:
/// the placeholders keep a log line readable without keeping the person in it.
/// </summary>
public static partial class Sanitizer
{
    public const string PersonalIdPlaceholder = "[PERSONNUMMER]";
    public const string EmailPlaceholder = "[EMAIL]";
    public const string NamePlaceholder = "[NAME]";

    /// <summary>Field names whose value is a person's name, in any casing or separator style.</summary>
    private static readonly string[] NameFields =
        ["name", "firstname", "first_name", "lastname", "last_name", "fullname", "full_name", "givenname", "surname"];

    // Swedish personal identity number: 10 or 12 digits, hyphen or plus optional.
    // The 12-digit form is listed first so it wins over its own 10-digit tail, and the
    // digit lookarounds stop a longer number from matching in part.
    [GeneratedRegex(@"(?<![\d-])(?:\d{8}[-+]?\d{4}|\d{6}[-+]?\d{4})(?![\d-])", RegexOptions.Compiled)]
    private static partial Regex PersonalIdRegex();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    /// <summary>JSON-ish name field: "firstName": "Anna".</summary>
    [GeneratedRegex(
        @"(?<key>""(?:name|firstName|first_name|lastName|last_name|fullName|full_name|givenName|surname)""\s*:\s*)""(?<value>(?:[^""\\]|\\.)*)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex JsonNameFieldRegex();

    /// <summary>
    /// Structured-log or query-string name field: firstName=Anna, lastName: Svensson.
    /// An unquoted value runs to the next delimiter rather than to the next space, because a
    /// name has more than one word in it and stopping at the space leaves the surname behind.
    /// That redacts a little more than strictly necessary, which is the safe direction here.
    /// </summary>
    [GeneratedRegex(
        @"\b(?<key>name|firstName|first_name|lastName|last_name|fullName|full_name|givenName|surname)\b(?<sep>\s*[=:]\s*)(?<value>""[^""]*""|'[^']*'|[^,;&}\]|\r\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex KeyValueNameFieldRegex();

    /// <summary>Scrubs personal identity numbers, e-mail addresses and name fields from free text.</summary>
    public static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? "";
        }

        // Names first: an e-mail inside a name field should be replaced as a name, not left behind.
        var result = JsonNameFieldRegex().Replace(text, m => $"{m.Groups["key"].Value}\"{NamePlaceholder}\"");
        result = KeyValueNameFieldRegex().Replace(result, ReplaceNameValue);
        result = EmailRegex().Replace(result, EmailPlaceholder);
        result = PersonalIdRegex().Replace(result, PersonalIdPlaceholder);
        return result;
    }

    /// <summary>
    /// Replaces a name field's value, unless the JSON pass above already did it: rewriting
    /// "name": "[NAME]" a second time would only strip the quotes off the placeholder.
    /// </summary>
    private static string ReplaceNameValue(Match match)
    {
        var value = match.Groups["value"].Value.Trim().Trim('"', '\'');
        return value == NamePlaceholder
            ? match.Value
            : $"{match.Groups["key"].Value}{match.Groups["sep"].Value}{NamePlaceholder}";
    }

    /// <summary>True when a property key holds a person's name and the whole value must go.</summary>
    public static bool IsNameField(string key) =>
        NameFields.Contains(key.Replace("-", "").Replace("_", "").Replace(" ", ""), StringComparer.OrdinalIgnoreCase)
        || NameFields.Contains(key, StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns a copy of the event with every text field and every property scrubbed.</summary>
    public static SeqEvent ScrubEvent(SeqEvent source)
    {
        var properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in source.Properties)
        {
            properties[key] = IsNameField(key) ? NamePlaceholder : Scrub(value);
        }

        return source with
        {
            MessageTemplate = Scrub(source.MessageTemplate),
            RenderedMessage = Scrub(source.RenderedMessage),
            Exception = source.Exception is null ? null : Scrub(source.Exception),
            Properties = properties
        };
    }
}
