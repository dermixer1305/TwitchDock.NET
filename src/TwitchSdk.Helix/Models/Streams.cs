namespace TwitchSdk.Helix.Models;

/// <summary>A streaming credential; do not log or publish StreamKey.</summary>
public sealed class StreamKeyResult
{
    public required string StreamKey { get; init; }
    public override string ToString() => "StreamKeyResult [redacted]";
}

public sealed record GetFollowedStreamsRequest
{
    public required string UserId { get; init; }
    public int? First { get; init; }
    public string? After { get; init; }
}

public sealed class CreateStreamMarkerRequest
{
    public required string UserId { get; init; }
    public string? Description { get; init; }
}

public sealed class CreatedStreamMarker
{
    public required string Id { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public int PositionSeconds { get; init; }
    public string Description { get; init; } = "";
}

public sealed record GetStreamMarkersRequest
{
    public string? UserId { get; init; }
    public string? VideoId { get; init; }
    public int? First { get; init; }
    public string? Before { get; init; }
    public string? After { get; init; }
}

public sealed class StreamMarkerGroup
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string UserLogin { get; init; }
    public IReadOnlyList<VideoMarkers> Videos { get; init; } = [];
}

public sealed class VideoMarkers
{
    public required string VideoId { get; init; }
    public IReadOnlyList<StreamMarker> Markers { get; init; } = [];
}

public sealed class StreamMarker
{
    public required string Id { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string Description { get; init; } = "";
    public int PositionSeconds { get; init; }
    public required string Url { get; init; }
}

public sealed record GetStreamsRequest
{
    public IReadOnlyList<string> UserIds { get; init; } = [];
    public IReadOnlyList<string> UserLogins { get; init; } = [];
    public IReadOnlyList<string> GameIds { get; init; } = [];
    public IReadOnlyList<string> Languages { get; init; } = [];
    public string? Type { get; init; }
    public int? First { get; init; }
    public string? Before { get; init; }
    public string? After { get; init; }
}

public sealed class TwitchStream
{
    public required string Id { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public string GameId { get; init; } = "";
    public string GameName { get; init; } = "";
    public string Type { get; init; } = "";
    public string Title { get; init; } = "";
    public IReadOnlyList<string> Tags { get; init; } = [];
    public long ViewerCount { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public string Language { get; init; } = "";
    public string ThumbnailUrl { get; init; } = "";
    /// <summary>Deprecated by Twitch; use Tags.</summary>
    public IReadOnlyList<string> TagIds { get; init; } = [];
    /// <summary>Deprecated by Twitch; always false.</summary>
    public bool IsMature { get; init; }
}
