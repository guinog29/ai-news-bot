using AiNewsBot;
using AiNewsBot.Core;
using AiNewsBot.Infrastructure;
using AiNewsBot.Infrastructure.Config;
using AiNewsBot.Infrastructure.Discord;
using AiNewsBot.Infrastructure.Sources;
using AiNewsBot.Infrastructure.State;
using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(builder => builder
    .SetMinimumLevel(LogLevel.Information)
    .AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    }));
var logger = loggerFactory.CreateLogger("AiNewsBot");

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

try
{
    var command = CommandLine.Parse(args);
    var config = FeedConfigLoader.Load(command.ConfigPath);
    using var http = BotHttpClient.Create();
    var sources = config.Sources.Select(s => FeedSourceFactory.Create(s, http)).ToList();

    if (command.DryRun)
    {
        return await DryRunAsync(sources, logger, cancellation.Token);
    }

    var webhooks = DiscordWebhookConfig.Resolve(config.ChannelKeys, Environment.GetEnvironmentVariable);
    if (!webhooks.ContainsKey(ChannelKeys.Status))
    {
        logger.LogWarning(
            "{Variable} is not set: failure alerts are disabled.",
            ChannelKeys.WebhookVariable(ChannelKeys.Status));
    }

    var job = new NewsJob(
        sources,
        new JsonFileStateStore(command.StatePath),
        new DiscordWebhookPublisher(http, webhooks),
        config.Options,
        TimeProvider.System,
        loggerFactory.CreateLogger<NewsJob>());

    var result = await job.RunAsync(cancellation.Token);
    logger.LogInformation(
        "Run finished: {Published} posted, {Failed} of {Total} sources failed.",
        result.Published,
        result.Failed,
        result.Sources.Count);

    // D11: a run in which nothing worked is a critical failure, so GitHub emails about it.
    return result.AllFailed ? 1 : 0;
}
catch (OperationCanceledException)
{
    logger.LogError("Cancelled.");
    return 1;
}
catch (Exception ex)
{
    // Messages here never include webhook URLs: they are not part of any exception text we create (D9).
    logger.LogCritical("Run failed: {Error}", ex.Message);
    return 1;
}

static async Task<int> DryRunAsync(IReadOnlyList<IFeedSource> sources, ILogger logger, CancellationToken ct)
{
    var failed = 0;
    foreach (var source in sources)
    {
        try
        {
            var items = await source.FetchAsync(ct);
            var newest = items.OrderByDescending(i => i.PublishedAt ?? DateTimeOffset.MaxValue).FirstOrDefault();
            logger.LogInformation(
                "{Source}: {Count} items; newest: {Title} ({Date:yyyy-MM-dd})",
                source.Name,
                items.Count,
                newest?.Title,
                newest?.PublishedAt);
            failed += items.Count == 0 ? 1 : 0;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError("{Source}: {Error}", source.Name, ex.Message);
            failed++;
        }
    }

    return failed == 0 ? 0 : 1;
}
