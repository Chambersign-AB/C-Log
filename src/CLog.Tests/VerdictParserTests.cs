using CLog.Model;
using CLog.Ollama;

namespace CLog.Tests;

public class VerdictParserTests
{
    [Theory]
    [InlineData("NOISE", Verdict.Noise)]
    [InlineData("KNOWN", Verdict.Known)]
    [InlineData("ANALYZE", Verdict.Analyze)]
    [InlineData("noise", Verdict.Noise)]
    [InlineData("Analyse", Verdict.Analyze)]
    public void A_well_formed_answer_is_read(string text, Verdict expected)
    {
        var content = $$"""{"verdict": "{{text}}", "reason": "because", "known_solution": ""}""";

        Assert.True(VerdictParser.TryParse(content, out var verdict, out var error));
        Assert.Equal("", error);
        Assert.Equal(expected, verdict!.Verdict);
        Assert.Equal("because", verdict.Reason);
    }

    [Fact]
    public void A_known_solution_is_carried_through()
    {
        const string content =
            """{"verdict": "KNOWN", "reason": "matches entry 2", "known_solution": "Restart the sync job"}""";

        Assert.True(VerdictParser.TryParse(content, out var verdict, out _));
        Assert.Equal(Verdict.Known, verdict!.Verdict);
        Assert.Equal("Restart the sync job", verdict.KnownSolution);
    }

    [Fact]
    public void An_empty_known_solution_becomes_null()
    {
        const string content = """{"verdict": "NOISE", "reason": "expected timeout", "known_solution": ""}""";

        Assert.True(VerdictParser.TryParse(content, out var verdict, out _));
        Assert.Null(verdict!.KnownSolution);
    }

    [Theory]
    [InlineData("""Here is my answer: {"verdict": "NOISE", "reason": "r"} Hope that helps!""")]
    [InlineData("""   {"verdict":"NOISE","reason":"r"}   """)]
    [InlineData("""{"verdict":"NOISE","reason":"r"} {"verdict":"ANALYZE"}""")]
    public void An_answer_wrapped_in_prose_is_still_read(string content)
    {
        // A local model rarely answers with bare JSON, and re-asking costs a whole cycle.
        Assert.True(VerdictParser.TryParse(content, out var verdict, out _));
        Assert.Equal(Verdict.Noise, verdict!.Verdict);
    }

    [Fact]
    public void An_answer_in_a_markdown_code_fence_is_still_read()
    {
        var content = string.Join(
            "\n",
            "```json",
            """{"verdict": "NOISE", "reason": "an expected timeout"}""",
            "```");

        Assert.True(VerdictParser.TryParse(content, out var verdict, out _));
        Assert.Equal(Verdict.Noise, verdict!.Verdict);
        Assert.Equal("an expected timeout", verdict.Reason);
    }

    [Fact]
    public void Braces_inside_strings_do_not_confuse_the_reader()
    {
        const string content = """{"verdict": "ANALYZE", "reason": "the template {OrderId} is wrong"}""";

        Assert.True(VerdictParser.TryParse(content, out var verdict, out _));
        Assert.Equal("the template {OrderId} is wrong", verdict!.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("I think this one is just noise, honestly.")]
    [InlineData("""{"verdict": "NOISE", "reason": """)]
    [InlineData("""{"verdict": }""")]
    [InlineData("""{"reason": "no verdict field here"}""")]
    [InlineData("""{"verdict": "MAYBE", "reason": "unsure"}""")]
    [InlineData("""["NOISE"]""")]
    public void An_unusable_answer_is_reported_rather_than_thrown(string? content)
    {
        var parsed = VerdictParser.TryParse(content, out var verdict, out var error);

        Assert.False(parsed);
        Assert.Null(verdict);
        Assert.NotEmpty(error);
    }
}
