namespace AiNewsBot;

/// <summary>Command-line arguments of the job.</summary>
/// <param name="ConfigPath">Feed configuration file.</param>
/// <param name="StatePath">State file (a checkout of the state branch in production).</param>
/// <param name="DryRun">Fetch and print every source without posting or touching state.</param>
internal sealed record CommandLine(string ConfigPath, string StatePath, bool DryRun)
{
    public const string Usage = "Usage: AiNewsBot [--config <path>] [--state <path>] [--dry-run]";

    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        var config = "config/feeds.json";
        var state = "state/state.json";
        var dryRun = false;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--config" when i + 1 < args.Count:
                    config = args[++i];
                    break;
                case "--state" when i + 1 < args.Count:
                    state = args[++i];
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument '{args[i]}'. {Usage}");
            }
        }

        return new CommandLine(config, state, dryRun);
    }
}
