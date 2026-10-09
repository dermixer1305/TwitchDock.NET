namespace TwitchSdk.Helix.Models;

public sealed class GetTeamsRequest
{
    public string? Name { get; init; }
    public string? Id { get; init; }
}

public sealed class ChannelTeam
{
    public required string BroadcasterId { get; init; }
    public required string BroadcasterLogin { get; init; }
    public required string BroadcasterName { get; init; }
    public string? BackgroundImageUrl { get; init; }
    public string? Banner { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public required string Info { get; init; }
    public required string ThumbnailUrl { get; init; }
    public required string TeamName { get; init; }
    public required string TeamDisplayName { get; init; }
    public required string Id { get; init; }
}

public sealed class TwitchTeam
{
    public IReadOnlyList<TeamMember> Users { get; init => field = value ?? []; } = [];
    public string? BackgroundImageUrl { get; init; }
    public string? Banner { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public required string Info { get; init; }
    public required string ThumbnailUrl { get; init; }
    public required string TeamName { get; init; }
    public required string TeamDisplayName { get; init; }
    public required string Id { get; init; }
}

public sealed class TeamMember
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
}
