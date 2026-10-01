using Microsoft.Extensions.Configuration;
using CLog.Configuration;

namespace CLog.Tests;

public class TriageOptionsTests
{
    private static CLogOptions Bind(IConfiguration configuration) =>
        configuration.GetSection(CLogOptions.SectionName).Get<CLogOptions>() ?? new CLogOptions();

    private static string ShippedSettingsPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "C-Log.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "src", "CLog", "appsettings.json");
    }

    private static string Normalise(string text) => text.ReplaceLineEndings("\n").Trim();

    [Theory]
    [InlineData("TwoStep", TriageMode.TwoStep)]
    [InlineData("SingleCall", TriageMode.SingleCall)]
    [InlineData("singlecall", TriageMode.SingleCall)]
    public void The_mode_is_chosen_in_configuration(string configured, TriageMode expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CLog:Triage:Mode"] = configured })
            .Build();

        Assert.Equal(expected, Bind(configuration).Triage.Mode);
    }

    [Fact]
    public void Model_and_prompts_can_be_swapped_without_touching_code()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CLog:Ollama:Model"] = "mistral-small",
                ["CLog:Triage:KnownPrompt"] = "known? {knowledge} {error}",
                ["CLog:Triage:NoisePrompt"] = "outside? {error}"
            })
            .Build();

        var options = Bind(configuration);

        Assert.Equal("mistral-small", options.Ollama.Model);
        Assert.Equal("known? {knowledge} {error}", options.Triage.KnownPrompt);
        Assert.Equal("outside? {error}", options.Triage.NoisePrompt);
    }

    [Fact]
    public void The_shipped_settings_and_the_built_in_defaults_ask_the_same_questions()
    {
        // The prompts exist twice, in appsettings.json to be editable and in code as the
        // fallback. If they drift, which one runs depends on how the service is deployed.
        var shipped = Bind(new ConfigurationBuilder().AddJsonFile(ShippedSettingsPath()).Build()).Triage;
        var defaults = new TriageOptions();

        Assert.Equal(defaults.Mode, shipped.Mode);
        Assert.Equal(Normalise(defaults.KnownPrompt), Normalise(shipped.KnownPrompt));
        Assert.Equal(Normalise(defaults.NoisePrompt), Normalise(shipped.NoisePrompt));
    }

    [Fact]
    public void The_default_prompts_carry_their_placeholders()
    {
        var defaults = new TriageOptions();

        Assert.Contains(TriageOptions.KnowledgePlaceholder, defaults.KnownPrompt);
        Assert.Contains(TriageOptions.ErrorPlaceholder, defaults.KnownPrompt);
        Assert.Contains(TriageOptions.ErrorPlaceholder, defaults.NoisePrompt);
    }
}
