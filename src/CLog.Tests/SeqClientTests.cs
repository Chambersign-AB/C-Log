using System.Net;
using Microsoft.Extensions.Options;
using CLog.Configuration;
using CLog.Seq;
using CLog.Tests.Fakes;

namespace CLog.Tests;

public class SeqClientTests
{
    private static SeqClient Build(StubHttpMessageHandler handler, SeqOptions? seq = null) =>
        new(
            new HttpClient(handler),
            Options.Create(new CLogOptions { Seq = seq ?? new SeqOptions { ApiKey = "test-key" } }),
            new TestLogger<SeqClient>());

    private const string OneEvent =
        """
        [
          {
            "Id": "event-42",
            "Timestamp": "2026-09-30T08:00:00.0000000Z",
            "Level": "Error",
            "RenderedMessage": "Order 4711 could not be saved",
            "MessageTemplateTokens": [
              { "Text": "Order " },
              { "PropertyName": "OrderId", "RawText": "{OrderId}" },
              { "Text": " could not be saved" }
            ],
            "Exception": "System.InvalidOperationException: nope\n   at Contoso.Orders.Save()",
            "Properties": [
              { "Name": "OrderId", "Value": 4711 },
              { "Name": "Environment", "Value": "production" }
            ]
          }
        ]
        """;

    [Fact]
    public async Task An_event_is_mapped_from_the_wire_format()
    {
        var client = Build(new StubHttpMessageHandler(HttpStatusCode.OK, OneEvent));

        var events = await client.GetErrorEventsAsync(DateTimeOffset.UtcNow.AddMinutes(-10));

        var logEvent = Assert.Single(events);
        Assert.Equal("event-42", logEvent.Id);
        Assert.Equal("Error", logEvent.Level);
        Assert.Equal("Order 4711 could not be saved", logEvent.RenderedMessage);
        Assert.Equal("production", logEvent.Properties["Environment"]);
        Assert.Contains("InvalidOperationException", logEvent.Exception);
    }

    [Fact]
    public async Task The_message_template_is_rebuilt_from_its_tokens()
    {
        // The template is what makes two occurrences of one log statement group together,
        // so it has to come back with its holes intact rather than its values.
        var client = Build(new StubHttpMessageHandler(HttpStatusCode.OK, OneEvent));

        var events = await client.GetErrorEventsAsync(DateTimeOffset.UtcNow);

        Assert.Equal("Order {OrderId} could not be saved", events[0].MessageTemplate);
    }

    [Fact]
    public async Task A_response_wrapped_in_an_events_property_is_also_accepted()
    {
        const string body = """{ "Events": [ { "Id": "event-1", "Level": "Error" } ] }""";
        var client = Build(new StubHttpMessageHandler(HttpStatusCode.OK, body));

        Assert.Single(await client.GetErrorEventsAsync(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task An_unexpected_response_shape_yields_no_events_rather_than_throwing()
    {
        var client = Build(new StubHttpMessageHandler(HttpStatusCode.OK, "42"));

        Assert.Empty(await client.GetErrorEventsAsync(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task The_api_key_is_sent_as_a_header()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client = Build(handler, new SeqOptions { ApiKey = "test-key" });

        await client.GetErrorEventsAsync(DateTimeOffset.UtcNow);

        Assert.Contains("api/events", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public void The_request_asks_only_for_errors_since_the_given_time()
    {
        var client = Build(new StubHttpMessageHandler(HttpStatusCode.OK, "[]"));
        var since = new DateTimeOffset(2026, 9, 30, 7, 50, 0, TimeSpan.Zero);

        var uri = client.BuildRequestUri(since);

        Assert.Contains("render=true", uri);
        Assert.Contains(Uri.EscapeDataString("@Level = 'Error'"), uri);
        Assert.Contains(Uri.EscapeDataString("2026-09-30T07:50:00"), uri);
    }

    [Fact]
    public void A_configured_filter_is_combined_with_the_error_filter()
    {
        var client = Build(
            new StubHttpMessageHandler(HttpStatusCode.OK, "[]"),
            new SeqOptions { Filter = "Environment = 'production'" });

        var uri = client.BuildRequestUri(DateTimeOffset.UtcNow);

        Assert.Contains(Uri.EscapeDataString("@Level = 'Error'"), uri);
        Assert.Contains(Uri.EscapeDataString("Environment = 'production'"), uri);
    }
}
