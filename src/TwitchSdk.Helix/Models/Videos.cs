namespace TwitchSdk.Helix.Models;

public sealed record GetVideosRequest
{
    public IReadOnlyList<string> Ids { get; init; } = [];
    public string? UserId { get; init; }
    public string? GameId { get; init; }
    public string? Language { get; init; }
    public string? Period { get; init; }
    public string? Sort { get; init; }
    public string? Type { get; init; }
    public int? First { get; init; }
    public string? After { get; init; }
    public string? Before { get; init; }
}

public sealed class TwitchVideo
{
    public required string Id { get; init; }
    public string? StreamId { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset PublishedAt { get; init; }
    public required string Url { get; init; }
    public required string ThumbnailUrl { get; init; }
    public required string Viewable { get; init; }
    public long ViewCount { get; init; }
    public required string Language { get; init; }
    public required string Type { get; init; }
    /// <summary>Twitch duration text, for example 3m21s; retained without assuming TimeSpan syntax.</summary>
    public required string Duration { get; init; }
    public IReadOnlyList<MutedVideoSegment>? MutedSegments { get; init; }
}

public sealed class MutedVideoSegment
{
    public int Duration { get; init; }
    public int Offset { get; init; }
}
