using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Watcher.Model;

namespace Watcher.Triage;

/// <summary>
/// Turns an error into a stable identity: exception type + normalised message template +
/// topmost non-framework stack frame. Everything that varies between two occurrences of the
/// same bug - timestamps, ids, personal data, file line numbers - is normalised away first,
/// so the same bug always produces the same hash.
/// </summary>
public static partial class Fingerprinter
{
    private const string Hole = "{}";

    [GeneratedRegex(
        @"\d{4}-\d{2}-\d{2}(?:[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:?\d{2})?)?",
        RegexOptions.Compiled)]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"\d{1,2}:\d{2}(?::\d{2})?(?:\.\d+)?", RegexOptions.Compiled)]
    private static partial Regex TimeOfDayRegex();

    [GeneratedRegex(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
        RegexOptions.Compiled)]
    private static partial Regex GuidRegex();

    /// <summary>A Serilog template hole: {Order}, {@State}, {Count,-5:000}.</summary>
    [GeneratedRegex(@"\{[@$]?\w+(?:,-?\d+)?(?::[^}]*)?\}", RegexOptions.Compiled)]
    private static partial Regex TemplateHoleRegex();

    [GeneratedRegex(@"\[(?:PERSONNUMMER|EMAIL|NAME)\]", RegexOptions.Compiled)]
    private static partial Regex ScrubPlaceholderRegex();

    /// <summary>
    /// A number, with no boundary required after it: "30.5s" and "61.25s" have to reduce to
    /// the same shape, and requiring a word boundary there would leave the decimals behind.
    /// The leading guard keeps it from biting into an identifier such as "v2" or "utf8".
    /// </summary>
    [GeneratedRegex(@"\b(?:0x)?[0-9a-fA-F]{16,}\b|(?<!\w)\d+(?:[.,]\d+)?", RegexOptions.Compiled)]
    private static partial Regex NumberRegex();

    /// <summary>Adjacent holes, left by something like a date or an id pair, become one hole.</summary>
    [GeneratedRegex(@"\{\}(?:[\s\-:/,.]*\{\})+", RegexOptions.Compiled)]
    private static partial Regex AdjacentHolesRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    public static ErrorFingerprint Compute(SeqEvent logEvent)
    {
        var exception = ExceptionInfo.Parse(logEvent.Exception);

        // The template is preferred when Seq supplies one: it holds no values to begin with.
        // Otherwise the rendered message is normalised down to the same shape.
        var source = !string.IsNullOrWhiteSpace(logEvent.MessageTemplate)
            ? logEvent.MessageTemplate
            : logEvent.RenderedMessage;

        // An exception logged without a message of its own still identifies itself by its own text.
        if (string.IsNullOrWhiteSpace(source))
        {
            source = exception.Message;
        }

        var template = NormalizeTemplate(source);
        var topFrame = exception.TopApplicationFrame();
        var hash = Hash(exception.Type, template, topFrame);

        return new ErrorFingerprint(hash, exception.Type, template, topFrame);
    }

    /// <summary>Reduces a message to the part that is the same for every occurrence.</summary>
    public static string NormalizeTemplate(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "";
        }

        // Personal data is replaced before it can influence the hash; the placeholders it
        // leaves then collapse into ordinary holes, so a scrubbed and an unscrubbed event
        // of the same error agree.
        var text = Sanitizer.Scrub(message);
        text = ScrubPlaceholderRegex().Replace(text, Hole);
        text = TemplateHoleRegex().Replace(text, Hole);
        text = GuidRegex().Replace(text, Hole);
        text = TimestampRegex().Replace(text, Hole);
        text = TimeOfDayRegex().Replace(text, Hole);
        text = NumberRegex().Replace(text, Hole);
        text = AdjacentHolesRegex().Replace(text, Hole);
        text = WhitespaceRegex().Replace(text, " ").Trim();
        return text;
    }

    private static string Hash(string exceptionType, string template, string topFrame)
    {
        var payload = string.Join("\n", exceptionType, template, topFrame);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(digest)[..16];
    }
}
