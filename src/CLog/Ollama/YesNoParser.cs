using System.Text.RegularExpressions;

namespace CLog.Ollama;

/// <summary>
/// Reads a yes/no answer. A small model told to answer with one word still adds punctuation,
/// quotes, markdown or a trailing explanation, so the first word decides. An answer that says
/// both, or neither, is reported as unreadable rather than guessed at: a wrong "yes" hides an
/// error from people, so doubt must not become one.
/// </summary>
public static partial class YesNoParser
{
    private static readonly string[] YesWords = ["ja", "yes"];
    private static readonly string[] NoWords = ["nej", "no"];

    public static bool TryParse(string? content, out bool yes)
    {
        yes = false;

        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var words = Word().Matches(content).Select(m => m.Value).ToList();
        if (words.Count == 0)
        {
            return false;
        }

        var saysYes = IsOneOf(words[0], YesWords);
        var saysNo = IsOneOf(words[0], NoWords);
        if (!saysYes && !saysNo)
        {
            return false;
        }

        var opposite = saysYes ? NoWords : YesWords;
        if (words.Skip(1).Any(word => IsOneOf(word, opposite)))
        {
            return false;
        }

        yes = saysYes;
        return true;
    }

    private static bool IsOneOf(string word, string[] candidates) =>
        candidates.Contains(word, StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Word();
}
