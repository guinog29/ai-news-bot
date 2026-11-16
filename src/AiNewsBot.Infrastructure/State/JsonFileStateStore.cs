using System.Text;
using System.Text.Json;
using AiNewsBot.Core;

namespace AiNewsBot.Infrastructure.State;

/// <summary>
/// Keeps the state in a JSON file (D15). In production the file lives in a checkout of the <c>state</c> branch,
/// and the workflow commits it.
/// </summary>
public sealed class JsonFileStateStore(string path) : IStateStore
{
    private const int SupportedVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        NewLine = "\n",
    };

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The file exists but cannot be read; never silently re-seeds over a corrupt file.</exception>
    public async Task<BotState?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var state = await JsonSerializer.DeserializeAsync<BotState>(stream, JsonOptions, cancellationToken);
            if (state is null || state.Version != SupportedVersion)
            {
                throw new InvalidOperationException($"Unsupported state file version in {path}.");
            }

            return state;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"State file {path} is corrupt: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(BotState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write to a temp file first so an interrupted run cannot leave a half-written state.
        var temp = path + ".tmp";
        var json = JsonSerializer.Serialize(state, JsonOptions) + "\n";
        await File.WriteAllTextAsync(temp, json, new UTF8Encoding(false), cancellationToken);
        File.Move(temp, path, overwrite: true);
    }
}
