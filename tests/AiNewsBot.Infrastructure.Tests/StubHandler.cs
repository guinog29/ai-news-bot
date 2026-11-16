using System.Net;
using System.Text;

namespace AiNewsBot.Infrastructure.Tests;

/// <summary>Replaces the network in tests: answers each request with a canned response and records what was sent.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(Uri? Url, string Body)> Requests { get; } = [];

    public static HttpResponseMessage Respond(string body, string contentType, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, contentType) };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.RequestUri, body));
        return respond(request);
    }
}
