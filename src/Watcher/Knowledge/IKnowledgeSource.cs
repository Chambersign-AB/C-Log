namespace Watcher.Knowledge;

/// <summary>Supplies the known-errors document handed to the model.</summary>
public interface IKnowledgeSource
{
    string Read();
}

/// <summary>Fixed knowledge text, for tests.</summary>
public sealed class StaticKnowledgeSource(string text) : IKnowledgeSource
{
    public string Read() => text;
}
