using CLog.Configuration;
using CLog.Model;
using CLog.Tests.Fakes;
using CLog.Triage;

namespace CLog.Tests;

public class TwoStepJudgeTests
{
    private const string Error = "Exception type: System.InvalidOperationException\nMessage: Order could not be saved";

    private const string OneEntry =
        """
        ## Deadlock when saving an order
        **Symptom** System.InvalidOperationException at Contoso.Orders.OrderRepository.Save.
        **Solution** Restart the sync job.
        """;

    private const string TwoEntries =
        OneEntry +
        """


        ## Payment provider timeout
        **Symptom** System.TimeoutException at Contoso.Payments.Gateway.Charge.
        **Solution** Retry after a minute.
        """;

    private static (TwoStepJudge Judge, RecordingOllamaClient Ollama, TestLogger<TwoStepJudge> Logger) Build(
        Func<string, string?> reply,
        TriageOptions? options = null)
    {
        var ollama = new RecordingOllamaClient { Reply = reply };
        var logger = new TestLogger<TwoStepJudge>();
        return (new TwoStepJudge(ollama, options ?? new TriageOptions(), logger), ollama, logger);
    }

    private static bool IsKnownQuestion(string prompt) => prompt.Contains("Kunskapsbas", StringComparison.Ordinal);

    [Fact]
    public async Task A_yes_to_the_known_question_is_KNOWN_with_the_solution_from_the_knowledge_base()
    {
        // The model volunteers a fix of its own; it must not end up in the verdict.
        var (judge, ollama, _) = Build(_ => "Ja. Lösning: starta om databasen.");

        var verdict = await judge.JudgeAsync(Error, OneEntry);

        Assert.NotNull(verdict);
        Assert.Equal(Verdict.Known, verdict.Verdict);
        Assert.Equal("Restart the sync job.", verdict.KnownSolution);
        Assert.Contains("Deadlock when saving an order", verdict.Reason);

        var question = Assert.Single(ollama.Questions);
        Assert.Contains("Beskriver kunskapsbasen nedan exakt detta fel?", question);
        Assert.Contains("Deadlock when saving an order", question);
        Assert.Contains("Order could not be saved", question);
    }

    [Fact]
    public async Task A_no_to_known_and_a_yes_to_the_client_mistake_question_is_NOISE()
    {
        var (judge, ollama, _) = Build(prompt => IsKnownQuestion(prompt) ? "Nej" : "Ja");

        var verdict = await judge.JudgeAsync(Error, OneEntry);

        Assert.NotNull(verdict);
        Assert.Equal(Verdict.Noise, verdict.Verdict);
        Assert.Null(verdict.KnownSolution);

        Assert.Equal(2, ollama.Questions.Count);
        Assert.Contains("avvisades på grund av klientens eget fel", ollama.Questions[1]);
        Assert.Contains("Order could not be saved", ollama.Questions[1]);
        Assert.DoesNotContain("Deadlock when saving an order", ollama.Questions[1]);
    }

    [Fact]
    public async Task A_no_to_both_questions_is_ANALYZE()
    {
        var (judge, ollama, logger) = Build(_ => "Nej");

        var verdict = await judge.JudgeAsync(Error, OneEntry);

        Assert.NotNull(verdict);
        Assert.Equal(Verdict.Analyze, verdict.Verdict);
        Assert.Equal(2, ollama.Questions.Count);
        Assert.Empty(logger.Warnings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("# Known errors\n\nNothing understood yet.")]
    [InlineData("## Template — copy this for a new entry\n**Solution**\nWhat to do about it.")]
    public async Task An_empty_knowledge_base_skips_the_known_question(string knownErrors)
    {
        // "Ja" to everything: had the known question been asked, the verdict would be KNOWN.
        var (judge, ollama, _) = Build(_ => "Ja");

        var verdict = await judge.JudgeAsync(Error, knownErrors);

        Assert.NotNull(verdict);
        Assert.Equal(Verdict.Noise, verdict.Verdict);
        Assert.False(IsKnownQuestion(Assert.Single(ollama.Questions)));
    }

    [Theory]
    [InlineData("Kanske")]
    [InlineData("")]
    [InlineData("Ja och nej")]
    [InlineData("{\"verdict\": \"KNOWN\"}")]
    public async Task An_unreadable_answer_to_the_known_question_is_ANALYZE_with_a_warning(string answer)
    {
        var (judge, ollama, logger) = Build(_ => answer);

        var verdict = await judge.JudgeAsync(Error, OneEntry);

        Assert.NotNull(verdict);
        Assert.Equal(Verdict.Analyze, verdict.Verdict);
        Assert.Null(verdict.KnownSolution);
        Assert.Single(ollama.Questions);
        Assert.Contains(logger.Warnings, w => w.Message.Contains("did not answer yes or no"));
    }

    [Fact]
    public async Task An_unreadable_answer_to_the_client_mistake_question_is_ANALYZE_with_a_warning()
    {
        var (judge, _, logger) = Build(prompt => IsKnownQuestion(prompt) ? "Nej" : "Svårt att säga.");

        var verdict = await judge.JudgeAsync(Error, OneEntry);

        Assert.NotNull(verdict);
        Assert.Equal(Verdict.Analyze, verdict.Verdict);
        Assert.Contains(logger.Warnings, w => w.Message.Contains("did not answer yes or no"));
    }

    [Fact]
    public async Task An_english_yes_or_no_is_accepted()
    {
        var (judge, _, logger) = Build(prompt => IsKnownQuestion(prompt) ? "No." : "Yes");

        var verdict = await judge.JudgeAsync(Error, OneEntry);

        Assert.NotNull(verdict);
        Assert.Equal(Verdict.Noise, verdict.Verdict);
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public async Task An_unreachable_model_leaves_the_error_unjudged()
    {
        var (knownDown, _, _) = Build(_ => null);
        var (noiseDown, _, _) = Build(prompt => IsKnownQuestion(prompt) ? "Nej" : null);

        Assert.Null(await knownDown.JudgeAsync(Error, OneEntry));
        Assert.Null(await noiseDown.JudgeAsync(Error, OneEntry));
    }

    [Fact]
    public async Task With_several_entries_the_matching_one_is_pinned_down_for_its_solution()
    {
        // Yes to the whole knowledge base, then yes only when the payment entry is asked about alone.
        var (judge, ollama, _) = Build(prompt =>
            prompt.Contains("Deadlock when saving", StringComparison.Ordinal) &&
            !prompt.Contains("Payment provider timeout", StringComparison.Ordinal)
                ? "Nej"
                : "Ja");

        var verdict = await judge.JudgeAsync(Error, TwoEntries);

        Assert.NotNull(verdict);
        Assert.Equal(Verdict.Known, verdict.Verdict);
        Assert.Equal("Retry after a minute.", verdict.KnownSolution);
        Assert.Contains("Payment provider timeout", verdict.Reason);
        Assert.Equal(3, ollama.Questions.Count);
    }

    [Fact]
    public async Task A_yes_that_no_single_entry_confirms_is_ANALYZE_with_a_warning()
    {
        var bothEntries = 0;
        var (judge, _, logger) = Build(prompt =>
        {
            var whole = prompt.Contains("Deadlock when saving", StringComparison.Ordinal) &&
                        prompt.Contains("Payment provider timeout", StringComparison.Ordinal);
            if (whole)
            {
                bothEntries++;
            }

            return whole ? "Ja" : "Nej";
        });

        var verdict = await judge.JudgeAsync(Error, TwoEntries);

        Assert.NotNull(verdict);
        Assert.Equal(Verdict.Analyze, verdict.Verdict);
        Assert.Null(verdict.KnownSolution);
        Assert.Equal(1, bothEntries);
        Assert.Contains(logger.Warnings, w => w.Message.Contains("confirmed none"));
    }

    [Fact]
    public async Task The_prompt_templates_come_from_configuration()
    {
        var options = new TriageOptions
        {
            KnownPrompt = "KNOWN? kb=[{knowledge}] err=[{error}]",
            NoisePrompt = "CLIENT? err=[{error}]"
        };
        var (judge, ollama, _) = Build(_ => "Nej", options);

        await judge.JudgeAsync("the error", "## Entry\nbody");

        Assert.Equal(
            new[] { "KNOWN? kb=[## Entry\nbody] err=[the error]", "CLIENT? err=[the error]" },
            ollama.Questions);
    }

    [Fact]
    public async Task A_template_missing_its_placeholder_still_carries_the_error()
    {
        var options = new TriageOptions { NoisePrompt = "Was this rejected for the client's own mistake?" };
        var (judge, ollama, _) = Build(_ => "Nej", options);

        await judge.JudgeAsync("the error", "");

        Assert.Contains("the error", Assert.Single(ollama.Questions));
    }
}
