using Watcher.Triage;

namespace Watcher.Tests;

public class ExceptionInfoTests
{
    [Fact]
    public void The_type_and_message_are_read_from_the_first_line()
    {
        var info = ExceptionInfo.Parse(TestEvents.StackTrace);

        Assert.Equal("System.InvalidOperationException", info.Type);
        Assert.Equal("The order could not be saved", info.Message);
    }

    [Fact]
    public void Frames_are_read_and_their_file_and_line_stripped()
    {
        var info = ExceptionInfo.Parse(TestEvents.StackTrace);

        Assert.Equal(4, info.Frames.Count);
        Assert.Contains(TestEvents.TopApplicationFrame, info.Frames);
        Assert.All(info.Frames, frame => Assert.DoesNotContain(":line", frame));
    }

    [Fact]
    public void Framework_frames_are_skipped_when_looking_for_the_failing_code()
    {
        Assert.Equal(TestEvents.TopApplicationFrame, ExceptionInfo.Parse(TestEvents.StackTrace).TopApplicationFrame());
    }

    [Fact]
    public void A_stack_of_only_framework_frames_falls_back_to_the_first_frame()
    {
        const string trace =
            """
            System.Exception: everything is framework
               at System.Threading.Tasks.Task.Wait()
               at Microsoft.AspNetCore.Server.Kestrel.Run()
            """;

        Assert.Equal("System.Threading.Tasks.Task.Wait()", ExceptionInfo.Parse(trace).TopApplicationFrame());
    }

    [Fact]
    public void The_inner_exception_type_wins_over_the_wrapper()
    {
        // AggregateException says nothing about what failed. Grouping by it would put every
        // wrapped failure in the application into one fingerprint.
        const string trace =
            """
            System.AggregateException: One or more errors occurred. ---> System.TimeoutException: timed out
               at Contoso.Orders.OrderService.PlaceOrder(OrderRequest request)
            """;

        var info = ExceptionInfo.Parse(trace);

        Assert.Equal("System.TimeoutException", info.Type);
        Assert.Equal("Contoso.Orders.OrderService.PlaceOrder(OrderRequest request)", info.TopApplicationFrame());
    }

    [Fact]
    public void Two_different_failures_behind_the_same_wrapper_stay_distinct()
    {
        var timeout = ExceptionInfo.Parse(
            "System.AggregateException: One or more errors occurred. ---> System.TimeoutException: timed out");
        var nullReference = ExceptionInfo.Parse(
            "System.AggregateException: One or more errors occurred. ---> System.NullReferenceException: oops");

        Assert.NotEqual(timeout.Type, nullReference.Type);
    }

    [Fact]
    public void An_exception_with_no_message_still_yields_its_type()
    {
        var info = ExceptionInfo.Parse("System.OperationCanceledException");

        Assert.Equal("System.OperationCanceledException", info.Type);
        Assert.Equal("", info.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_exception_text_parses_to_nothing(string? text)
    {
        var info = ExceptionInfo.Parse(text);

        Assert.Equal("", info.Type);
        Assert.Empty(info.Frames);
        Assert.Equal("", info.TopApplicationFrame());
    }
}
