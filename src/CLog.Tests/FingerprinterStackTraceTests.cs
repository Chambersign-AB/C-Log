using CLog.Triage;

namespace CLog.Tests;

/// <summary>With a stack trace, where the exception came from is the identity; the message template is not.</summary>
public class FingerprinterStackTraceTests
{
    private const string RequestLoggingTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    private const string UnhandledTemplate = "An unhandled exception has occurred while executing the request.";

    [Fact]
    public void One_exception_logged_under_two_templates_is_one_fingerprint()
    {
        var byRequestLogging = TestEvents.Error(id: "event-1", template: RequestLoggingTemplate);
        var byExceptionHandler = TestEvents.Error(id: "event-2", template: UnhandledTemplate);

        Assert.Equal(
            Fingerprinter.Compute(byRequestLogging).Hash,
            Fingerprinter.Compute(byExceptionHandler).Hash);
    }

    [Fact]
    public void One_exception_logged_under_two_templates_is_one_group_in_a_cycle()
    {
        var group = Assert.Single(ErrorGroup.FromEvents(
        [
            TestEvents.Error(id: "event-1", template: RequestLoggingTemplate),
            TestEvents.Error(id: "event-2", template: UnhandledTemplate)
        ]));

        Assert.Equal(2, group.Count);
    }

    [Fact]
    public void The_two_logs_still_match_when_one_caught_the_exception_further_down_the_stack()
    {
        // The outer handler sees the frames the exception passed on its way out to it.
        var inner = TestEvents.Error(template: RequestLoggingTemplate);
        var outer = TestEvents.Error(
            template: UnhandledTemplate,
            exception: TestEvents.StackTrace
                + "\n   at Contoso.Web.TenantMiddleware.Invoke(HttpContext context) in C:/src/contoso/TenantMiddleware.cs:line 22");

        Assert.Equal(
            Fingerprinter.Compute(inner).Hash,
            Fingerprinter.Compute(outer).Hash);
    }

    [Fact]
    public void The_same_template_from_two_places_in_the_code_is_two_fingerprints()
    {
        var inRepository = TestEvents.Error(template: UnhandledTemplate);
        var inController = TestEvents.Error(
            template: UnhandledTemplate,
            exception: TestEvents.StackTrace.Replace(
                "Contoso.Orders.OrderRepository.Save(Order order)",
                "Contoso.Orders.OrderController.Post(OrderRequest request)"));

        Assert.NotEqual(
            Fingerprinter.Compute(inRepository).Hash,
            Fingerprinter.Compute(inController).Hash);
    }

    [Fact]
    public void Without_a_stack_trace_the_template_tells_two_errors_apart()
    {
        var saving = TestEvents.MessageOnly("", template: "Could not save order {OrderId}");
        var loading = TestEvents.MessageOnly("", template: "Could not load customer {CustomerId}");

        Assert.NotEqual(
            Fingerprinter.Compute(saving).Hash,
            Fingerprinter.Compute(loading).Hash);
    }

    [Fact]
    public void An_exception_that_arrives_without_frames_is_told_apart_by_its_template()
    {
        const string headerOnly = "System.OperationCanceledException: The operation was canceled.";
        var export = TestEvents.Error(template: "Export {ExportId} was abandoned", exception: headerOnly);
        var import = TestEvents.Error(template: "Import {ImportId} was abandoned", exception: headerOnly);

        Assert.NotEqual(
            Fingerprinter.Compute(export).Hash,
            Fingerprinter.Compute(import).Hash);
    }

    [Fact]
    public void The_template_is_still_reported_alongside_the_fingerprint()
    {
        var fingerprint = Fingerprinter.Compute(TestEvents.Error(template: "Order {OrderId} could not be saved"));

        Assert.Equal("Order {} could not be saved", fingerprint.NormalizedTemplate);
    }

    [Fact]
    public void An_error_with_a_stack_trace_remembers_the_hash_it_had_with_its_template()
    {
        var first = Fingerprinter.Compute(TestEvents.Error(template: RequestLoggingTemplate));
        var second = Fingerprinter.Compute(TestEvents.Error(template: UnhandledTemplate));

        Assert.NotNull(first.PreviousHash);
        Assert.NotEqual(first.Hash, first.PreviousHash);
        Assert.NotEqual(first.PreviousHash, second.PreviousHash);
    }

    [Fact]
    public void An_error_without_a_stack_trace_keeps_the_hash_it_always_had()
    {
        var fingerprint = Fingerprinter.Compute(TestEvents.MessageOnly("Payment gateway returned 503"));

        Assert.Null(fingerprint.PreviousHash);
    }
}
