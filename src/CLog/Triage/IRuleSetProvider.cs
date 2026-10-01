namespace CLog.Triage;

/// <summary>Supplies the current ignore rules.</summary>
public interface IRuleSetProvider
{
    RuleSet Current { get; }
}

/// <summary>A fixed set of rules, for tests and for callers that build rules in code.</summary>
public sealed class StaticRuleSetProvider(RuleSet rules) : IRuleSetProvider
{
    public RuleSet Current { get; } = rules;
}
