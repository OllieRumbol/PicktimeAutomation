using System.Net;
using System.Text;

namespace PicktimeAutomation.ServicesTests;

/// <summary>
/// Answers every request from a test-supplied function, so no request reaches the real Picktime API.
/// Records each request, so a test can inspect what was sent.
/// </summary>
internal sealed class StubHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public static StubHttpMessageHandler Returning(HttpStatusCode statusCode, string body)
    {
        return new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }));
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        return respond(request, cancellationToken);
    }
}
