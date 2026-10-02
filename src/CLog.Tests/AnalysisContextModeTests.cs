using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using CLog.Analysis;
using CLog.Configuration;
using CLog.Knowledge;
using CLog.Model;
using CLog.Tests.Fakes;
using CLog.Triage;

namespace CLog.Tests;

/// <summary>
/// Context mode: step two files the error with the code around it and asks no model. The
/// real service, analyst and issue reporter run, with Ollama, the repository and GitHub faked.
/// </summary>
public class AnalysisContextModeTests
{
    private const string BuildRoot = @"C:\agent\_work\1\s\";
    private const string AnalysisModelName = "analysis-model";

    private static readonly string[] Files =
    [
        "src/CSign.Api/SignController.cs",
        "src/CSign.Core/Signer.cs",
        "src/CSign.Core/KeyStore.cs",
        "src/CSign.Core/Vault.cs",
        "src/CSign.Core/Hsm.cs"
    ];

    private readonly ManualTimeProvider _time = new();
    private readonly InMemoryFingerprintStore _store = new();
    private readonly RecordingTriageSink _sink = new();
    private readonly FakeIssueTracker _tracker = new();
    private readonly RecordingOllamaClient _ollama = new() { Reply = _ => "Nej" };
    private readonly FakeSourceRepository _repository = new();

    public AnalysisContextModeTests()
    {
        foreach (var file in Files)
        {
            _repository.WithNumberedFile(file, 200);
        }
    }

    private static string Frame(string method, string repoPath, int line) =>
        $"   at {method} in {BuildRoot}{repoPath.Replace('/', '\\')}:line {line}";

    /// <summary>Five application frames in five files, with framework frames between them.</summary>
    private static SeqEvent SigningError() => TestEvents.Error(
        template: "",
        rendered: "Could not sign for anna.svensson@example.com",
        exception: string.Join('\n',
            "System.InvalidOperationException: signing failed for 900101-1234",
            "   at System.Security.Cryptography.RSA.SignData(Byte[] data)",
            Frame("CSign.Core.Signer.Apply(Document document)", Files[1], 120),
            Frame("CSign.Core.KeyStore.Load()", Files[2], 60),
            "   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.Execute()",
            Frame("CSign.Core.Vault.Open()", Files[3], 100),
            Frame("CSign.Api.SignController.Sign(Request request)", Files[0], 30),
            Frame("CSign.Core.Hsm.Connect()", Files[4], 100)));

    private TriageService Service(AnalysisMode? mode = null)
    {
        var analysis = new AnalysisOptions { Enabled = true, Model = AnalysisModelName };
        if (mode is not null)
        {
            analysis.Mode = mode.Value;
        }

        var options = Options.Create(new CLogOptions { Analysis = analysis });

        // The analysis model is the real Ollama one over the same client as triage, so a
        // call for analysis would show up among the questions asked.
        var analyst = new Analyst(
            new CodeContextResolver(_repository, options),
            new OllamaAnalysisModel(_ollama, options),
            options,
            new TestLogger<Analyst>());
        var reporter = new IssueReporter(analyst, _tracker, _store, options, new TestLogger<IssueReporter>());

        return new TriageService(
            new FakeSeqClient([SigningError()]),
            _store,
            _ollama,
            _sink,
            new StaticKnowledgeSource("# Known errors"),
            new StaticRuleSetProvider(RuleSet.Parse("""{ "ignore": [] }""")),
            options,
            new TestLogger<TriageService>(),
            _time,
            reporter);
    }

    private async Task<string> FiledBodyAsync(AnalysisMode? mode = null)
    {
        await Service(mode).RunOnceAsync();
        return Assert.Single(_tracker.Issues).Body;
    }

    private static string CodeLine(string file, int line, bool focus = false) =>
        $"{(focus ? ">>>" : "   ")}{line,6}  // {Path.GetFileName(file)} line {line}\n";

    [Fact]
    public async Task Context_is_the_mode_when_none_is_configured_and_it_asks_no_model_for_an_analysis()
    {
        await Service().RunOnceAsync();

        Assert.Single(_tracker.Issues);
        Assert.DoesNotContain(AnalysisModelName, _ollama.Models);

        // Step one is untouched: its one question, to the triage model, and nothing else.
        Assert.Null(Assert.Single(_ollama.Models));
        Assert.Contains("klientens eget fel", Assert.Single(_ollama.Questions));
    }

    [Fact]
    public async Task Model_mode_still_asks_the_analysis_model()
    {
        await Service(AnalysisMode.Model).RunOnceAsync();

        Assert.Single(_tracker.Issues);
        Assert.Contains(AnalysisModelName, _ollama.Models);
    }

    [Fact]
    public async Task The_mark_is_on_the_line_the_frame_names_and_on_no_other()
    {
        var body = await FiledBodyAsync();

        Assert.Contains(CodeLine(Files[1], 120, focus: true), body);
        Assert.Contains(CodeLine(Files[1], 119), body);
        Assert.Contains(CodeLine(Files[1], 121), body);
        Assert.Contains(CodeLine(Files[2], 60, focus: true), body);
        Assert.Contains(CodeLine(Files[3], 100, focus: true), body);
        Assert.Contains(CodeLine(Files[0], 30, focus: true), body);

        var marked = body.Split('\n').Where(l => l.StartsWith(CodeContextResolver.FocusMarker, StringComparison.Ordinal));
        Assert.Equal(4, marked.Count());
    }

    [Fact]
    public async Task The_issue_holds_the_error_the_stack_trace_and_the_code_under_its_heading()
    {
        var body = await FiledBodyAsync();

        var error = body.IndexOf("## Error\n", StringComparison.Ordinal);
        var stack = body.IndexOf("## Stack trace\n", StringComparison.Ordinal);
        var code = body.IndexOf("## Kod runt felet\n", StringComparison.Ordinal);

        Assert.Equal(0, error);
        Assert.True(stack > error, "the stack trace is missing or misplaced");
        Assert.True(code > stack, "the code is missing or misplaced");

        Assert.Contains("Exception type: System.InvalidOperationException", body[..stack]);
        Assert.Contains(@"   at CSign.Core.Signer.Apply(Document document) in C:\agent\_work\1\s\src\CSign.Core\Signer.cs:line 120", body[stack..code]);
        Assert.Contains("**`src/CSign.Core/Signer.cs`** line 120, in `CSign.Core.Signer.Apply(Document document)`", body[code..]);
        Assert.Contains(CodeLine(Files[1], 80), body[code..]);
        Assert.Contains(CodeLine(Files[1], 160), body[code..]);
        Assert.DoesNotContain(CodeLine(Files[1], 79), body);
        Assert.DoesNotContain(CodeLine(Files[1], 161), body);
    }

    [Fact]
    public async Task The_issue_holds_no_sentence_from_a_model_and_no_disclaimer_about_one()
    {
        // A model that would answer if asked: none of what it says may reach the issue.
        _ollama.Reply = prompt => prompt.Contains("klientens eget fel", StringComparison.Ordinal)
            ? "Nej"
            : "Trolig orsak: nyckeln saknas.";

        var body = await FiledBodyAsync();

        Assert.DoesNotContain("Trolig orsak", body);
        Assert.DoesNotContain("## Analysis", body);
        Assert.DoesNotContain("model", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("diagnosis", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_footnote_says_who_filed_it_and_the_commit_the_code_was_read_at()
    {
        var body = await FiledBodyAsync();

        var footnote = body[body.LastIndexOf("---\n", StringComparison.Ordinal)..];
        Assert.StartsWith("---\n_Filed automatically by CLog.", footnote);
        Assert.Contains($"`{FakeSourceRepository.Head}`", footnote);
    }

    [Fact]
    public async Task Every_application_frame_gets_its_code_up_to_four_files_and_four_hundred_lines()
    {
        await Service().RunOnceAsync();

        // Five frames in five files: the fifth file is never read.
        Assert.Equal(new[] { Files[1], Files[2], Files[3], Files[0] }, _repository.Reads);

        var body = Assert.Single(_tracker.Issues).Body;
        var codeLines = body.Split('\n').Count(l => l.Contains(".cs line ", StringComparison.Ordinal) && l.Contains("  // ", StringComparison.Ordinal));
        Assert.InRange(codeLines, 1, 400);
        Assert.DoesNotContain("Hsm.cs line", body);
    }

    [Fact]
    public async Task Nothing_unscrubbed_reaches_the_issue()
    {
        await Service().RunOnceAsync();

        var issue = Assert.Single(_tracker.Issues);
        foreach (var secret in new[] { "anna.svensson@example.com", "900101-1234" })
        {
            Assert.DoesNotContain(secret, issue.Title);
            Assert.DoesNotContain(secret, issue.Body);
        }

        Assert.Contains("signing failed for [PERSONNUMMER]", issue.Body);
    }

    [Fact]
    public async Task An_error_whose_code_cannot_be_found_is_still_filed_and_says_so()
    {
        _repository.Available = false;

        var body = await FiledBodyAsync();

        Assert.Contains("## Kod runt felet\n\n_No file in the repository could be matched to the stack trace._", body);
        Assert.EndsWith("_Filed automatically by CLog._\n", body);
    }

    [Fact]
    public async Task An_error_judged_NOISE_is_not_filed_and_no_code_is_read()
    {
        _ollama.Reply = _ => "Ja";

        await Service().RunOnceAsync();

        Assert.Equal(Verdict.Noise, Assert.Single(_sink.Records).Verdict);
        Assert.Empty(_tracker.Issues);
        Assert.Empty(_repository.Reads);
    }

    [Theory]
    [InlineData("Model", AnalysisMode.Model)]
    [InlineData("Context", AnalysisMode.Context)]
    [InlineData(null, AnalysisMode.Context)]
    public void The_mode_comes_from_configuration_and_is_Context_when_not_set(string? configured, AnalysisMode expected)
    {
        var options = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CLog:Analysis:Enabled"] = "true",
                ["CLog:Analysis:Mode"] = configured
            })
            .Build()
            .GetSection(CLogOptions.SectionName)
            .Get<CLogOptions>()!;

        Assert.Equal(expected, options.Analysis.Mode);
    }
}
