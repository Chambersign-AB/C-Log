using System.Text;
using System.Text.RegularExpressions;

namespace CLog.Knowledge;

/// <summary>
/// Splits known-errors.md into its entries. Only "## " headings count: the introduction, the
/// copy-me template and anything inside an HTML comment are instructions for people, and a
/// file holding nothing else is an empty knowledge base.
/// </summary>
public static partial class KnownErrors
{
    private const string TemplateTitle = "Template";

    public static IReadOnlyList<KnownErrorEntry> Parse(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return [];
        }

        var entries = new List<KnownErrorEntry>();
        string? title = null;
        var body = new StringBuilder();

        void Flush()
        {
            if (title is not null && !title.StartsWith(TemplateTitle, StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(new KnownErrorEntry(title, body.ToString().Trim()));
            }
        }

        foreach (var raw in HtmlComment().Replace(markdown, "").Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                title = line[3..].Trim();
                body.Clear();
                continue;
            }

            if (title is not null)
            {
                body.Append(line).Append('\n');
            }
        }

        Flush();
        return entries;
    }

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlComment();
}
