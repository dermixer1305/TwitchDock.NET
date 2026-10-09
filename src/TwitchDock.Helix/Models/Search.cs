namespace TwitchDock.Helix.Models;

public sealed record SearchCategoriesRequest
{
    /// <summary>Unencoded search text; the SDK handles URI encoding.</summary>
    public required string Query { get; init; }
    public int? First { get; init; }
    public string? After { get; init; }
}

public sealed record SearchChannelsRequest
{
    public required string Query { get; init; }
    public bool? LiveOnly { get; init; }
    public int? First { get; init; }
    public string? After { get; init; }
}

public sealed class CategorySearchResult
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string BoxArtUrl { get; init; }
}

public sealed class ChannelSearchResult
{
    public required string BroadcasterLanguage { get; init; }
    public required string BroadcasterLogin { get; init; }
    public required string DisplayName { get; init; }
    public required string GameId { get; init; }
    public required string GameName { get; init; }
    public required string Id { get; init; }
    public bool IsLive { get; init; }
    /// <summary>Deprecated by Twitch; use Tags.</summary>
    public IReadOnlyList<string> TagIds { get; init => field = value ?? []; } = [];
    public IReadOnlyList<string> Tags { get; init => field = value ?? []; } = [];
    public required string ThumbnailUrl { get; init; }
    public required string Title { get; init; }
    /// <summary>RFC3339 timestamp or an empty string when the channel is offline.</summary>
    public required string StartedAt { get; init; }
}
