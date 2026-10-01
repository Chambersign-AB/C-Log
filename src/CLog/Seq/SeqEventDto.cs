using System.Text.Json;
using System.Text.Json.Serialization;
using CLog.Model;

namespace CLog.Seq;

/// <summary>
/// Seq's wire format for a single event. Only the fields triage needs are mapped; Seq is
/// free to add more without breaking us.
/// </summary>
internal sealed class SeqEventDto
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("Timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("Level")]
    public string? Level { get; set; }

    [JsonPropertyName("RenderedMessage")]
    public string? RenderedMessage { get; set; }

    /// <summary>Present on some Seq endpoints; otherwise rebuilt from the tokens below.</summary>
    [JsonPropertyName("MessageTemplate")]
    public string? MessageTemplate { get; set; }

    [JsonPropertyName("MessageTemplateTokens")]
    public List<MessageTemplateTokenDto>? MessageTemplateTokens { get; set; }

    [JsonPropertyName("Exception")]
    public string? Exception { get; set; }

    [JsonPropertyName("Properties")]
    public List<SeqPropertyDto>? Properties { get; set; }

    public SeqEvent ToSeqEvent()
    {
        var properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in Properties ?? [])
        {
            if (!string.IsNullOrEmpty(property.Name))
            {
                properties[property.Name] = property.ValueAsString();
            }
        }

        var template = MessageTemplate;
        if (string.IsNullOrEmpty(template) && MessageTemplateTokens is { Count: > 0 })
        {
            template = string.Concat(MessageTemplateTokens.Select(t => t.ToTemplateText()));
        }

        return new SeqEvent
        {
            Id = Id ?? "",
            Timestamp = ParseTimestamp(Timestamp),
            Level = Level ?? "",
            MessageTemplate = template ?? "",
            RenderedMessage = RenderedMessage ?? "",
            Exception = Exception,
            Properties = properties
        };
    }

    private static DateTimeOffset ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
}

internal sealed class MessageTemplateTokenDto
{
    [JsonPropertyName("Text")]
    public string? Text { get; set; }

    [JsonPropertyName("PropertyName")]
    public string? PropertyName { get; set; }

    [JsonPropertyName("RawText")]
    public string? RawText { get; set; }

    /// <summary>Rebuilds the template hole, so the template keeps its shape and loses its values.</summary>
    public string ToTemplateText()
    {
        if (Text is not null)
        {
            return Text;
        }

        if (!string.IsNullOrEmpty(RawText))
        {
            return RawText;
        }

        return string.IsNullOrEmpty(PropertyName) ? "" : "{" + PropertyName + "}";
    }
}

internal sealed class SeqPropertyDto
{
    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("Value")]
    public JsonElement Value { get; set; }

    /// <summary>Flattens any JSON value to text; structured values keep their JSON form.</summary>
    public string? ValueAsString() => Value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => Value.GetString(),
        _ => Value.GetRawText()
    };
}
