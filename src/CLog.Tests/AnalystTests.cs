using Microsoft.Extensions.Options;
using CLog.Analysis;
using CLog.Configuration;
using CLog.Model;
using CLog.Tests.Fakes;
using CLog.Triage;

namespace CLog.Tests;

public class AnalystTests
{
    private const string Signer = "src/CSign.Core/Signer.cs";

    private const string KnownErrorsText =
        """
        ## Deadlock when saving an order
        **Solution** Restart the sync job.
        """;

    private readonly FakeSourceRepository _repository = new FakeSourceRepository().WithNumberedFile(Signer, 200);
    private readonly FakeAnalysisModel _model = new();

    private Analyst NewAnalyst(AnalysisOptions? analysis = null)
    {
        var options = Options.Create(new CLogOptions { Analysis = analysis ?? new AnalysisOptions() });
        return new Analyst(new CodeContextResolver(_repository, options), _model, options, new TestLogger<Analyst>());
    }

    /// <summary>An event as Seq delivers it, personal data and all, taken through the same scrubbing as in a cycle.</summary>
    private static ErrorGroup Scrubbed(SeqEvent raw) => Assert.Single(ErrorGroup.FromEvents([raw]));

    private static SeqEvent RawEvent() => TestEvents.Error(
        template: "",
        rendered: "Could not sign for anna.svensson@example.com, name=Anna Svensson",
        exception:
        "System.InvalidOperationException: signing failed for 900101-1234\n"
        + @"   at CSign.Core.Signer.Apply(Document document) in C:\agent\_work\1\s\src\CSign.Core\Signer.cs:line 120",
        properties: new Dictionary<string, string?>
        {
            ["firstName"] = "Anna",
            ["Email"] = "anna.svensson@example.com"
        });

    private async Task<(AnalysisResult Result, string Prompt)> AnalyzeAsync(AnalysisOptions? analysis = null)
    {
        var group = Scrubbed(RawEvent());
        var result = await NewAnalyst(analysis).AnalyzeAsync(
            group.Sample, TriageReports.BuildErrorReport(group), KnownErrorsText);
        return (result, Assert.Single(_model.Prompts));
    }

    [Fact]
    public async Task The_prompt_opens_with_the_instruction()
    {
        var (_, prompt) = await AnalyzeAsync();

        Assert.StartsWith(
            "Börja med felmeddelandet. Om det säger vad som gick fel, utgå från det — föreslå inte en annan orsak. "
            + "Säg uttryckligen om koden du fått inte räcker för att förklara felet. "
            + "Föreslå inga ändringar i filer som inte innehåller den kastande raden.\n\n"
            + "Här är ett fel och koden det uppstod i. Ange trolig orsak (1–3 meningar), var i koden (fil:rad), "
            + "och ett lösningsförslag. Svara i markdown.\n\n# Fel\n",
            prompt);
    }

    [Fact]
    public async Task The_prompt_carries_the_error_the_code_and_the_knowledge_base_in_that_order()
    {
        var (_, prompt) = await AnalyzeAsync();

        var error = prompt.IndexOf("Exception type: System.InvalidOperationException", StringComparison.Ordinal);
        var code = prompt.IndexOf("  120 > // Signer.cs line 120", StringComparison.Ordinal);
        var knowledge = prompt.IndexOf("## Deadlock when saving an order", StringComparison.Ordinal);

        Assert.True(error > 0, "the error report is missing");
        Assert.True(code > error, "the code excerpt is missing or misplaced");
        Assert.True(knowledge > code, "the knowledge base is missing or misplaced");
        Assert.Contains($"## {Signer} (rad 80–160, felet på rad 120)", prompt);
    }

    [Fact]
    public async Task Nothing_unscrubbed_reaches_the_analysis_prompt()
    {
        var (_, prompt) = await AnalyzeAsync();

        foreach (var secret in new[] { "anna.svensson@example.com", "Anna", "Svensson", "900101-1234" })
        {
            Assert.DoesNotContain(secret, prompt);
        }

        Assert.Contains("[EMAIL]", prompt);
        Assert.Contains("[PERSONNUMMER]", prompt);
    }

    [Fact]
    public async Task The_models_answer_is_the_analysis()
    {
        _model.Answer = "\n  Trolig orsak: nyckeln saknas.  \n";

        var (result, _) = await AnalyzeAsync();

        Assert.Equal("Trolig orsak: nyckeln saknas.", result.Text);
        Assert.Equal(Signer, Assert.Single(result.Code.Excerpts).Path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    public async Task A_model_that_gives_no_answer_gives_no_analysis_and_no_fault(string? answer)
    {
        _model.Answer = answer;

        var (result, _) = await AnalyzeAsync();

        Assert.Null(result.Text);
    }

    [Fact]
    public async Task An_error_whose_code_cannot_be_found_is_still_analysed_and_says_so()
    {
        _repository.Available = false;

        var (result, prompt) = await AnalyzeAsync();

        Assert.Empty(result.Code.Excerpts);
        Assert.Contains("(ingen kod kunde hittas för detta fel)", prompt);
        Assert.NotNull(result.Text);
    }

    [Fact]
    public async Task The_instruction_comes_from_configuration()
    {
        var (_, prompt) = await AnalyzeAsync(new AnalysisOptions { Prompt = "Explain this failure." });

        Assert.StartsWith("Explain this failure.", prompt);
    }

    [Fact]
    public async Task The_ollama_model_asks_the_model_named_for_analysis_not_the_triage_one()
    {
        var ollama = new RecordingOllamaClient { Reply = _ => "an analysis" };
        var model = new OllamaAnalysisModel(
            ollama,
            Options.Create(new CLogOptions
            {
                Ollama = new OllamaOptions { Model = "mistral" },
                Analysis = new AnalysisOptions { Model = "mistral-small" }
            }));

        var answer = await model.CompleteAsync("the prompt");

        Assert.Equal("an analysis", answer);
        Assert.Equal("the prompt", Assert.Single(ollama.Questions));
        Assert.Equal("mistral-small", Assert.Single(ollama.Models));
    }
}
