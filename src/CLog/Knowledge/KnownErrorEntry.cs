namespace CLog.Knowledge;

/// <summary>One "## " entry of known-errors.md.</summary>
public sealed record KnownErrorEntry(string Title, string Body)
{
    private const string SolutionMarker = "**Solution**";

    /// <summary>The entry as the model reads it.</summary>
    public string Text => Body.Length == 0 ? $"## {Title}" : $"## {Title}\n{Body}";

    /// <summary>
    /// What follows **Solution**, or the whole body when the entry does not use the template's
    /// sections. Taken from the file as written, never from the model's retelling of it.
    /// </summary>
    public string Solution
    {
        get
        {
            var at = Body.IndexOf(SolutionMarker, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                return Body;
            }

            var solution = Body[(at + SolutionMarker.Length)..].TrimStart(':', ' ', '\t', '\r', '\n').TrimEnd();
            return solution.Length == 0 ? Body : solution;
        }
    }
}
