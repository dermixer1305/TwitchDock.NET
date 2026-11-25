namespace TwitchDock.Helix.Models;

public sealed record GetVideosRequest
{
    public IReadOnlyList<string> Ids { get; init => field = value ?? []; } = [];
    public string? UserId { get; init; }
    public string? GameId { get; init; }
    /// <summary>One language code, or several separated by commas (for example <c>de,en</c>). Requires GameId or UserId.</summary>
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
    /// <summary>Null when the video was not published.</summary>
    public DateTimeOffset? PublishedAt { get; init; }
    public required string Url { get; init; }
    public required string ThumbnailUrl { get; init; }
    public required string Viewable { get; init; }
    public long ViewCount { get; init; }
    public required string Language { get; init; }
    /// <summary><c>archive</c>, <c>highlight</c> or <c>upload</c>; ID queries can also return <c>past_premiere</c> or <c>premiere_upload</c>.</summary>
    public required string Type { get; init; }
    /// <summary>Twitch duration text in hours, minutes and seconds without leading zero units, for example 3m21s or 1h0m5s.</summary>
    public required string Duration { get; init; }
    /// <summary>Always null with an app access token.</summary>
    public IReadOnlyList<MutedVideoSegment>? MutedSegments { get; init; }
}

public sealed class MutedVideoSegment
{
    public int Duration { get; init; }
    public int Offset { get; init; }
}
