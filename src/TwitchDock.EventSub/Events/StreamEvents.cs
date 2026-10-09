namespace TwitchDock.EventSub.Events;

/// <summary>stream.online v1.</summary>
public sealed class StreamOnlineEvent
{
    public required string Id { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    /// <summary>live, playlist, watch_party, premiere or rerun; kept as a string to tolerate new values.</summary>
    public required string Type { get; init; }
    public DateTimeOffset StartedAt { get; init; }
}

/// <summary>stream.offline v1.</summary>
public sealed class StreamOfflineEvent
{
    /// <summary>Documented by Twitch, but historically absent from delivered payloads; null when omitted.</summary>
    public string? Id { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
}
