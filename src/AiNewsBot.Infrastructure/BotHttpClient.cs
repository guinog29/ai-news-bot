using System.Net;

namespace AiNewsBot.Infrastructure;

/// <summary>Creates the single <see cref="HttpClient"/> shared by the whole run.</summary>
/// <remarks>
/// Deliberately not built through <c>IHttpClientFactory</c>: its default logging writes full request URLs,
/// which for Discord are secret webhook URLs (D9).
/// </remarks>
public static class BotHttpClient
{
    /// <summary>Creates a client with decompression, a timeout and an identifying User-Agent.</summary>
    public static HttpClient Create()
    {
        var handler = new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ai-news-bot/0.1 (+https://github.com/guinog29/ai-news-bot)");
        return client;
    }
}
