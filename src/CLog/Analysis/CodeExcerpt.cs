namespace CLog.Analysis;

/// <summary>The lines around the place one stack frame points at.</summary>
/// <param name="Path">Repository-relative path.</param>
/// <param name="FocusLine">The line the frame names.</param>
/// <param name="Frame">The method the frame belongs to.</param>
/// <param name="Text">The lines, each prefixed with its number; the focus line is marked.</param>
public sealed record CodeExcerpt(string Path, int StartLine, int EndLine, int FocusLine, string Frame, string Text)
{
    public int LineCount => EndLine - StartLine + 1;
}
