namespace TwitchDock.Helix.Models;

public sealed class CreateClipRequest
{
    public required string BroadcasterId { get; init; }
    public string? Title { get; init; }
    public decimal? Duration { get; init; }
}

public sealed class CreateClipFromVodRequest
{
    public required string EditorId { get; init; }
    public required string BroadcasterId { get; init; }
    public required string VodId { get; init; }
    /// <summary>Offset where the new clip ends, unlike TwitchClip.VodOffset, which indicates where a clip starts.</summary>
    public required int VodOffset { get; init; }
    public decimal? Duration { get; init; }
    public required string Title { get; init; }
}

public sealed class CreatedClip
{
    public required string Id { get; init; }
    public required string EditUrl { get; init; }
}

public sealed record GetClipsRequest
{
    public string? BroadcasterId { get; init; }
    public string? GameId { get; init; }
    public IReadOnlyList<string> Ids { get; init => field = value ?? []; } = [];
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public int? First { get; init; }
    public string? Before { get; init; }
    public string? After { get; init; }
    public bool? IsFeatured { get; init; }
}

public sealed class TwitchClip
{
    public required string Id { get; init; }
    public required string Url { get; init; }
    public required string EmbedUrl { get; init; }
    public required string BroadcasterId { get; init; }
    public required string BroadcasterName { get; init; }
    public required string CreatorId { get; init; }
    public required string CreatorName { get; init; }
    public required string VideoId { get; init; }
    public required string GameId { get; init; }
    public required string Language { get; init; }
    public required string Title { get; init; }
    public long ViewCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public required string ThumbnailUrl { get; init; }
    public decimal Duration { get; init; }
    public int? VodOffset { get; init; }
    public bool IsFeatured { get; init; }
}

public sealed class GetClipsDownloadRequest
{
    public required string EditorId { get; init; }
    public required string BroadcasterId { get; init; }
    public required IReadOnlyList<string> ClipIds { get; init; }
}

public sealed class ClipDownload
{
    public required string ClipId { get; init; }
    public string? LandscapeDownloadUrl { get; init; }
    public string? PortraitDownloadUrl { get; init; }
}
