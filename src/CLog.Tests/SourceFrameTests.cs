using CLog.Analysis;

namespace CLog.Tests;

public class SourceFrameTests
{
    [Fact]
    public void A_frame_with_symbols_gives_its_method_file_and_line()
    {
        var frame = Assert.Single(SourceFrame.Parse(
            @"   at CSign.Core.Signer.Apply(Document document) in C:\agent\_work\1\s\src\CSign.Core\Signer.cs:line 120"));

        Assert.Equal("CSign.Core.Signer.Apply(Document document)", frame.Method);
        Assert.Equal(@"C:\agent\_work\1\s\src\CSign.Core\Signer.cs", frame.File);
        Assert.Equal(120, frame.Line);
    }

    [Fact]
    public void A_frame_without_symbols_has_no_file()
    {
        var frame = Assert.Single(SourceFrame.Parse("   at CSign.Core.Signer.Apply(Document document)"));

        Assert.Equal("CSign.Core.Signer.Apply(Document document)", frame.Method);
        Assert.Null(frame.File);
    }

    [Fact]
    public void Frames_come_back_in_stack_order_and_the_header_line_is_not_a_frame()
    {
        var frames = SourceFrame.Parse(
            """
            System.InvalidOperationException: The order could not be saved
               at System.Data.SqlClient.SqlCommand.ExecuteNonQuery()
               at CSign.Core.Signer.Apply(Document document) in /src/CSign.Core/Signer.cs:line 12
               --- End of stack trace from previous location ---
               at CSign.Api.SignController.Sign(Request request) in /src/CSign.Api/SignController.cs:line 40
            """);

        Assert.Equal(
            new[] { "System.Data.SqlClient.SqlCommand.ExecuteNonQuery()", "CSign.Core.Signer.Apply(Document document)", "CSign.Api.SignController.Sign(Request request)" },
            frames.Select(f => f.Method));
        Assert.Equal(new[] { 0, 12, 40 }, frames.Select(f => f.Line));
    }

    [Fact]
    public void A_path_with_spaces_is_kept_whole()
    {
        var frame = Assert.Single(SourceFrame.Parse(
            @"   at CSign.Core.Signer.Apply() in C:\Build Agent\my work\Signer.cs:line 7"));

        Assert.Equal(@"C:\Build Agent\my work\Signer.cs", frame.File);
        Assert.Equal(7, frame.Line);
    }

    [Fact]
    public void A_frame_from_a_swedish_runtime_is_read_too()
    {
        var frame = Assert.Single(SourceFrame.Parse(@"   vid CSign.Core.Signer.Apply() i C:\src\Signer.cs:rad 31"));

        Assert.Equal("CSign.Core.Signer.Apply()", frame.Method);
        Assert.Equal(31, frame.Line);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("System.InvalidOperationException: no stack at all")]
    public void Text_without_frames_gives_none(string? text)
    {
        Assert.Empty(SourceFrame.Parse(text));
    }
}
