using Watcher.Triage;

namespace Watcher.Tests;

public class RuleSetTests
{
    private static (Watcher.Model.SeqEvent Event, Watcher.Model.ErrorFingerprint Fingerprint) Sample(
        string rendered = "Order 4711 could not be saved")
    {
        var logEvent = TestEvents.Error(template: "", rendered: rendered);
        return (logEvent, Fingerprinter.Compute(logEvent));
    }

    [Fact]
    public void An_empty_rule_list_filters_nothing()
    {
        var (logEvent, fingerprint) = Sample();

        Assert.Null(RuleSet.Empty.FirstMatch(logEvent, fingerprint));
    }

    [Fact]
    public void A_rule_matches_on_exception_type()
    {
        var rules = RuleSet.Parse(
            """
            { "version": 1, "ignore": [
                { "id": "invalid-op", "reason": "expected", "exceptionType": "System.InvalidOperationException" }
            ] }
            """);
        var (logEvent, fingerprint) = Sample();

        Assert.Equal("invalid-op", rules.FirstMatch(logEvent, fingerprint)?.Id);
    }

    [Fact]
    public void A_rule_matches_on_a_message_substring_ignoring_case()
    {
        var rules = RuleSet.Parse(
            """
            { "ignore": [ { "id": "save", "messageContains": "COULD NOT BE SAVED" } ] }
            """);
        var (logEvent, fingerprint) = Sample();

        Assert.Equal("save", rules.FirstMatch(logEvent, fingerprint)?.Id);
    }

    [Fact]
    public void A_rule_matches_on_a_message_regex()
    {
        var rules = RuleSet.Parse(
            """
            { "ignore": [ { "id": "order-numbers", "messageMatches": "Order [0-9]+ could not" } ] }
            """);
        var (logEvent, fingerprint) = Sample();

        Assert.Equal("order-numbers", rules.FirstMatch(logEvent, fingerprint)?.Id);
    }

    [Fact]
    public void A_rule_matches_on_the_top_application_frame()
    {
        var rules = RuleSet.Parse(
            """
            { "ignore": [ { "id": "repository", "topFrameContains": "OrderRepository" } ] }
            """);
        var (logEvent, fingerprint) = Sample();

        Assert.Equal("repository", rules.FirstMatch(logEvent, fingerprint)?.Id);
    }

    [Fact]
    public void A_rule_matches_on_an_exact_fingerprint()
    {
        var (logEvent, fingerprint) = Sample();
        var rules = RuleSet.Parse(
            $$"""
              { "ignore": [ { "id": "pinned", "fingerprint": "{{fingerprint.Hash}}" } ] }
              """);

        Assert.Equal("pinned", rules.FirstMatch(logEvent, fingerprint)?.Id);
    }

    [Fact]
    public void Every_condition_on_a_rule_has_to_match()
    {
        // The exception type is right but the message is not, so the rule must not fire:
        // a rule that fired on any one condition would silence far more than intended.
        var rules = RuleSet.Parse(
            """
            { "ignore": [ {
                "id": "narrow",
                "exceptionType": "System.InvalidOperationException",
                "messageContains": "timeout"
            } ] }
            """);
        var (logEvent, fingerprint) = Sample();

        Assert.Null(rules.FirstMatch(logEvent, fingerprint));
    }

    [Fact]
    public void A_rule_with_no_conditions_never_matches()
    {
        var rules = RuleSet.Parse("""{ "ignore": [ { "id": "typo", "reason": "all fields misspelled" } ] }""");
        var (logEvent, fingerprint) = Sample();

        Assert.Null(rules.FirstMatch(logEvent, fingerprint));
    }

    [Fact]
    public void An_invalid_regex_in_a_rule_does_not_throw()
    {
        var rules = RuleSet.Parse("""{ "ignore": [ { "id": "broken", "messageMatches": "([unclosed" } ] }""");
        var (logEvent, fingerprint) = Sample();

        Assert.Null(rules.FirstMatch(logEvent, fingerprint));
    }

    [Fact]
    public void The_first_matching_rule_is_returned()
    {
        var rules = RuleSet.Parse(
            """
            { "ignore": [
                { "id": "first", "messageContains": "could not" },
                { "id": "second", "messageContains": "saved" }
            ] }
            """);
        var (logEvent, fingerprint) = Sample();

        Assert.Equal("first", rules.FirstMatch(logEvent, fingerprint)?.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_content_parses_to_an_empty_rule_set(string json)
    {
        Assert.Empty(RuleSet.Parse(json).Ignore);
    }

    [Fact]
    public void A_missing_rules_file_loads_as_an_empty_rule_set()
    {
        Assert.Empty(RuleSet.Load(Path.Combine(Path.GetTempPath(), "c-log-no-such-rules.json")).Ignore);
    }
}
