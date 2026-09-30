using System.Text.Json;
using Watcher.Model;

namespace Watcher.Ollama;

/// <summary>
/// Reads the model's answer. A local model is not a reliable JSON emitter, so the parser
/// tolerates code fences and surrounding prose, and reports failure rather than throwing:
/// a bad answer costs us one judgement, never the service.
/// </summary>
public static class VerdictParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static bool TryParse(string? content, out TriageVerdict? verdict, out string error)
    {
        verdict = null;

        if (string.IsNullOrWhiteSpace(content))
        {
            error = "the model returned an empty answer";
            return false;
        }

        if (!TryExtractJsonObject(content, out var json))
        {
            error = "no JSON object found in the answer";
            return false;
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            error = "malformed JSON: " + ex.Message;
            return false;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            error = "the answer was not a JSON object";
            return false;
        }

        if (!TryGetString(root, "verdict", out var verdictText))
        {
            error = "the answer has no \"verdict\" field";
            return false;
        }

        if (!TryParseVerdict(verdictText, out var parsedVerdict))
        {
            error = $"\"{verdictText}\" is not one of NOISE, KNOWN or ANALYZE";
            return false;
        }

        TryGetString(root, "reason", out var reason);
        TryGetString(root, "known_solution", out var knownSolution);

        verdict = new TriageVerdict(
            parsedVerdict,
            string.IsNullOrWhiteSpace(reason) ? "" : reason.Trim(),
            string.IsNullOrWhiteSpace(knownSolution) ? null : knownSolution.Trim());

        error = "";
        return true;
    }

    private static bool TryParseVerdict(string text, out Verdict verdict)
    {
        switch (text.Trim().ToUpperInvariant())
        {
            case "NOISE":
                verdict = Verdict.Noise;
                return true;
            case "KNOWN":
                verdict = Verdict.Known;
                return true;
            case "ANALYZE":
            case "ANALYSE":
                verdict = Verdict.Analyze;
                return true;
            default:
                verdict = Verdict.Analyze;
                return false;
        }
    }

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = "";
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? "",
                JsonValueKind.Null or JsonValueKind.Undefined => "",
                _ => property.Value.GetRawText()
            };
            return value.Length > 0;
        }

        return false;
    }

    /// <summary>
    /// Pulls the first complete JSON object out of the text, ignoring anything the model
    /// wrapped it in. Braces inside strings are not counted as nesting.
    /// </summary>
    private static bool TryExtractJsonObject(string content, out string json)
    {
        json = "";
        var start = content.IndexOf('{');
        if (start < 0)
        {
            return false;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < content.Length; i++)
        {
            var c = content[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        json = content[start..(i + 1)];
                        return true;
                    }

                    break;
            }
        }

        return false;
    }
}
