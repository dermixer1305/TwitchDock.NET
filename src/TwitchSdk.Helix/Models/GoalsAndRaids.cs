namespace TwitchSdk.Helix.Models;

public sealed class CreatorGoal
{
    public required string Id { get; init; }
    public required string BroadcasterId { get; init; }
    public required string BroadcasterName { get; init; }
    public required string BroadcasterLogin { get; init; }
    public required string Type { get; init; }
    public required string Description { get; init; }
    public long CurrentAmount { get; init; }
    public long TargetAmount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class RaidResult
{
    public DateTimeOffset CreatedAt { get; init; }
    /// <summary>Deprecated by Twitch; always false.</summary>
    public bool IsMature { get; init; }
}
