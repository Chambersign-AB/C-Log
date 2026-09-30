using System.Net;
using System.Text;

namespace Watcher.Tests.Fakes;

/// <summary>Answers every request with a canned response, so HTTP clients can be tested offline.</summary>
public sealed class StubHttpMessageHandler(
    HttpStatusCode statusCode,
    string body,
    string contentType = "application/json") : HttpMessageHandler
{
    private readonly List<string> _requestBodies = [];

    public IReadOnlyList<string> RequestBodies => _requestBodies;
    public int CallCount { get; private set; }
    public Uri? LastRequestUri { get; private set; }

    /// <summary>Set to throw instead of answering, standing in for an unreachable service.</summary>
    public Exception? ThrowOnSend { get; set; }

    public static StubHttpMessageHandler Unreachable() =>
        new(HttpStatusCode.OK, "") { ThrowOnSend = new HttpRequestException("connection refused") };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequestUri = request.RequestUri;

        if (request.Content is not null)
        {
            _requestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
        }

        if (ThrowOnSend is not null)
        {
            throw ThrowOnSend;
        }

        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType)
        };
    }
}
