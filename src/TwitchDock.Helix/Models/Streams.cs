namespace TwitchDock.Helix.Models;

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
    public string Description { get; init => field = value ?? ""; } = "";
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
    public IReadOnlyList<VideoMarkers> Videos { get; init => field = value ?? []; } = [];
}

public sealed class VideoMarkers
{
    public required string VideoId { get; init; }
    public IReadOnlyList<StreamMarker> Markers { get; init => field = value ?? []; } = [];
}

public sealed class StreamMarker
{
    public required string Id { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string Description { get; init => field = value ?? ""; } = "";
    public int PositionSeconds { get; init; }
    public required string Url { get; init; }
}

public sealed record GetStreamsRequest
{
    public IReadOnlyList<string> UserIds { get; init => field = value ?? []; } = [];
    public IReadOnlyList<string> UserLogins { get; init => field = value ?? []; } = [];
    public IReadOnlyList<string> GameIds { get; init => field = value ?? []; } = [];
    public IReadOnlyList<string> Languages { get; init => field = value ?? []; } = [];
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
    public string GameId { get; init => field = value ?? ""; } = "";
    public string GameName { get; init => field = value ?? ""; } = "";
    public string Type { get; init => field = value ?? ""; } = "";
    public string Title { get; init => field = value ?? ""; } = "";
    public IReadOnlyList<string> Tags { get; init => field = value ?? []; } = [];
    public long ViewerCount { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public string Language { get; init => field = value ?? ""; } = "";
    public string ThumbnailUrl { get; init => field = value ?? ""; } = "";
    /// <summary>Deprecated by Twitch; use Tags.</summary>
    public IReadOnlyList<string> TagIds { get; init => field = value ?? []; } = [];
    /// <summary>Deprecated by Twitch; always false.</summary>
    public bool IsMature { get; init; }
}
