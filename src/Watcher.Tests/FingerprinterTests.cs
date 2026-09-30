using Watcher.Triage;

namespace Watcher.Tests;

public class FingerprinterTests
{
    [Fact]
    public void Fingerprint_is_stable_when_personal_data_differs()
    {
        var first = TestEvents.Error(
            id: "event-1",
            template: "",
            rendered: "Could not save order for anna.svensson@example.com (900101-1234), name=Anna Svensson");

        var second = TestEvents.Error(
            id: "event-2",
            template: "",
            rendered: "Could not save order for bo.larsson@example.org (19801231-5678), name=Bo Larsson");

        Assert.Equal(
            Fingerprinter.Compute(first).Hash,
            Fingerprinter.Compute(second).Hash);
    }

    [Fact]
    public void Fingerprint_is_stable_when_timestamps_and_ids_differ()
    {
        var first = TestEvents.Error(
            template: "",
            rendered: "Order 4711 failed at 2026-09-30T08:00:00Z, correlation 6f9619ff-8b86-d011-b42d-00cf4fc964ff",
            timestamp: new DateTimeOffset(2026, 9, 30, 8, 0, 0, TimeSpan.Zero));

        var second = TestEvents.Error(
            template: "",
            rendered: "Order 9982 failed at 2026-10-01T23:45:12Z, correlation 1e2f3a4b-5c6d-7e8f-9a0b-1c2d3e4f5a6b",
            timestamp: new DateTimeOffset(2026, 10, 1, 23, 45, 12, TimeSpan.Zero));

        Assert.Equal(
            Fingerprinter.Compute(first).Hash,
            Fingerprinter.Compute(second).Hash);
    }

    [Fact]
    public void Fingerprint_is_the_same_before_and_after_scrubbing()
    {
        // The pipeline scrubs before it fingerprints. If scrubbing moved the hash, an error
        // logged before a scrubbing change would look new afterwards.
        var raw = TestEvents.Error(
            template: "",
            rendered: "Lookup failed for 900101-1234 / anna@example.com");

        var scrubbed = Sanitizer.ScrubEvent(raw);

        Assert.Equal(
            Fingerprinter.Compute(raw).Hash,
            Fingerprinter.Compute(scrubbed).Hash);
    }

    [Fact]
    public void Fingerprint_ignores_source_file_line_numbers()
    {
        var before = TestEvents.Error(exception: TestEvents.StackTrace);
        var after = TestEvents.Error(
            exception: TestEvents.StackTrace.Replace("line 84", "line 91").Replace("line 31", "line 37"));

        Assert.Equal(
            Fingerprinter.Compute(before).Hash,
            Fingerprinter.Compute(after).Hash);
    }

    [Fact]
    public void Top_frame_is_the_first_frame_outside_Microsoft_and_System()
    {
        var fingerprint = Fingerprinter.Compute(TestEvents.Error());

        Assert.Equal(TestEvents.TopApplicationFrame, fingerprint.TopFrame);
    }

    [Fact]
    public void Fingerprint_captures_the_exception_type()
    {
        var fingerprint = Fingerprinter.Compute(TestEvents.Error());

        Assert.Equal("System.InvalidOperationException", fingerprint.ExceptionType);
    }

    [Fact]
    public void Fingerprint_differs_when_the_exception_type_differs()
    {
        var invalidOperation = TestEvents.Error();
        var nullReference = TestEvents.Error(
            exception: TestEvents.StackTrace.Replace(
                "System.InvalidOperationException", "System.NullReferenceException"));

        Assert.NotEqual(
            Fingerprinter.Compute(invalidOperation).Hash,
            Fingerprinter.Compute(nullReference).Hash);
    }

    [Fact]
    public void Fingerprint_differs_when_the_failing_code_differs()
    {
        var inRepository = TestEvents.Error();
        var inController = TestEvents.Error(
            exception: TestEvents.StackTrace.Replace(
                "Contoso.Orders.OrderRepository.Save(Order order)",
                "Contoso.Orders.OrderController.Post(OrderRequest request)"));

        Assert.NotEqual(
            Fingerprinter.Compute(inRepository).Hash,
            Fingerprinter.Compute(inController).Hash);
    }

    [Fact]
    public void Fingerprint_differs_when_the_message_differs()
    {
        var saving = TestEvents.MessageOnly("Could not save the order");
        var loading = TestEvents.MessageOnly("Could not load the customer");

        Assert.NotEqual(
            Fingerprinter.Compute(saving).Hash,
            Fingerprinter.Compute(loading).Hash);
    }

    [Fact]
    public void Message_template_is_preferred_over_the_rendered_message()
    {
        // Two events from the same log statement carry the same template and different values.
        var first = TestEvents.Error(template: "Order {OrderId} failed", rendered: "Order 1 failed");
        var second = TestEvents.Error(template: "Order {OrderId} failed", rendered: "Order 2 failed");

        Assert.Equal(
            Fingerprinter.Compute(first).Hash,
            Fingerprinter.Compute(second).Hash);
    }

    [Fact]
    public void An_event_without_an_exception_still_gets_a_fingerprint()
    {
        var fingerprint = Fingerprinter.Compute(TestEvents.MessageOnly("Payment gateway returned 503"));

        Assert.NotEmpty(fingerprint.Hash);
        Assert.Equal("", fingerprint.ExceptionType);
        Assert.Equal("", fingerprint.TopFrame);
    }

    [Theory]
    [InlineData("Order 4711 failed", "Order 9 failed")]
    [InlineData("Retry 1 of 5 failed", "Retry 4 of 5 failed")]
    [InlineData("Timed out after 30.5s", "Timed out after 61.25s")]
    public void Numbers_in_a_message_do_not_change_the_fingerprint(string first, string second)
    {
        Assert.Equal(
            Fingerprinter.Compute(TestEvents.MessageOnly(first)).Hash,
            Fingerprinter.Compute(TestEvents.MessageOnly(second)).Hash);
    }
}
