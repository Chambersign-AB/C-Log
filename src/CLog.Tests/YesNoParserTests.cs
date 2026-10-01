using CLog.Ollama;

namespace CLog.Tests;

public class YesNoParserTests
{
    [Theory]
    [InlineData("Ja")]
    [InlineData("ja")]
    [InlineData("  JA  \n")]
    [InlineData("Ja.")]
    [InlineData("**Ja**")]
    [InlineData("\"Ja\"")]
    [InlineData("Yes")]
    [InlineData("YES!")]
    [InlineData("Ja, kunskapsbasen beskriver felet.")]
    public void A_yes_is_read_whatever_the_model_wraps_it_in(string answer)
    {
        Assert.True(YesNoParser.TryParse(answer, out var yes));
        Assert.True(yes);
    }

    [Theory]
    [InlineData("Nej")]
    [InlineData("nej")]
    [InlineData("\nNEJ.")]
    [InlineData("No")]
    [InlineData("no.")]
    [InlineData("`Nej`")]
    [InlineData("Nej, detta är ett fel i vår kod.")]
    public void A_no_is_read_whatever_the_model_wraps_it_in(string answer)
    {
        Assert.True(YesNoParser.TryParse(answer, out var yes));
        Assert.False(yes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("Kanske")]
    [InlineData("Jag vet inte")]
    [InlineData("Det är ett känt fel")]
    [InlineData("Ja och nej")]
    [InlineData("Ja/Nej")]
    [InlineData("No — or rather, yes")]
    [InlineData("{\"verdict\": \"KNOWN\"}")]
    public void Anything_that_is_not_clearly_yes_or_no_is_unreadable(string? answer)
    {
        Assert.False(YesNoParser.TryParse(answer, out var yes));
        Assert.False(yes);
    }
}
