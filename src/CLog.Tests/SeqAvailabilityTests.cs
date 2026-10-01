using System.Net;
using Microsoft.Extensions.Options;
using CLog.Configuration;
using CLog.Seq;
using CLog.Tests.Fakes;

namespace CLog.Tests;

/// <summary>
/// Seq being unreachable is routine. It has to be reported as such, and it must not look like
/// "no errors happened" — that would quietly hide the whole error stream.
/// </summary>
public class SeqAvailabilityTests
{
    private static SeqClient Build(StubHttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            Options.Create(new CLogOptions { Seq = new SeqOptions { ApiKey = "test-key" } }),
            new TestLogger<SeqClient>());

    [Fact]
    public async Task An_unreachable_seq_is_reported_as_unavailable_not_as_an_empty_batch()
    {
        var client = Build(StubHttpMessageHandler.Unreachable());

        var exception = await Assert.ThrowsAsync<SeqUnavailableException>(
            () => client.GetErrorEventsAsync(DateTimeOffset.UtcNow));

        Assert.Contains("could not be read", exception.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task An_error_status_is_reported_as_unavailable(HttpStatusCode statusCode)
    {
        var client = Build(new StubHttpMessageHandler(statusCode, "nope"));

        await Assert.ThrowsAsync<SeqUnavailableException>(
            () => client.GetErrorEventsAsync(DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_rejected_api_key_says_so_and_says_where_to_set_it(HttpStatusCode statusCode)
    {
        // A wrong key is the likeliest setup mistake, and "Seq answered 401" alone does not
        // tell the person what to do about it.
        var client = Build(new StubHttpMessageHandler(statusCode, ""));

        var exception = await Assert.ThrowsAsync<SeqUnavailableException>(
            () => client.GetErrorEventsAsync(DateTimeOffset.UtcNow));

        Assert.Contains("API key", exception.Message);
        Assert.Contains("CLog:Seq:ApiKey", exception.Message);
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_reported_as_unavailable()
    {
        var client = Build(new StubHttpMessageHandler(HttpStatusCode.OK, "<html>a proxy error page</html>"));

        await Assert.ThrowsAsync<SeqUnavailableException>(
            () => client.GetErrorEventsAsync(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task An_empty_batch_from_a_healthy_seq_is_not_an_error()
    {
        var client = Build(new StubHttpMessageHandler(HttpStatusCode.OK, "[]"));

        Assert.Empty(await client.GetErrorEventsAsync(DateTimeOffset.UtcNow));
    }
}
