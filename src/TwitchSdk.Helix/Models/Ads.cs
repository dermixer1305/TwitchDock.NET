namespace TwitchSdk.Helix.Models;

public sealed class StartCommercialRequest
{
    public required string BroadcasterId { get; init; }
    public required int Length { get; init; }
}

public sealed class CommercialResult
{
    public int Length { get; init; }
    public string Message { get; init; } = "";
    public int RetryAfter { get; init; }
}

public sealed class AdSchedule
{
    public int SnoozeCount { get; init; }
    public required string SnoozeRefreshAt { get; init; }
    /// <summary>RFC3339 timestamp, or an empty string when no ad is scheduled or the channel is offline.</summary>
    public required string NextAdAt { get; init; }
    public int Duration { get; init; }
    /// <summary>RFC3339 timestamp, or an empty string when no ad has run or the channel is offline.</summary>
    public required string LastAdAt { get; init; }
    public int PrerollFreeTime { get; init; }
}

public sealed class AdSnoozeResult
{
    public int SnoozeCount { get; init; }
    public required string SnoozeRefreshAt { get; init; }
    public required string NextAdAt { get; init; }
}
