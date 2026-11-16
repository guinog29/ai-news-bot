namespace AiNewsBot.Core;

/// <summary>Persists <see cref="BotState"/> between runs.</summary>
public interface IStateStore
{
    /// <summary>Loads the state, or returns <see langword="null"/> when none exists yet.</summary>
    Task<BotState?> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Saves the state.</summary>
    Task SaveAsync(BotState state, CancellationToken cancellationToken);
}
