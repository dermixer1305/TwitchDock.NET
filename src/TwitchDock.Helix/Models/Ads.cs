using System.Text.Json.Serialization;

namespace TwitchDock.Helix.Models;

public sealed class StartCommercialRequest
{
    public required string BroadcasterId { get; init; }
    public required int Length { get; init; }
}

public sealed class CommercialResult
{
    public int Length { get; init; }
    public string Message { get; init => field = value ?? ""; } = "";
    public int RetryAfter { get; init; }
}

public sealed class AdSchedule
{
    public int SnoozeCount { get; init; }
    /// <summary>When the broadcaster gains another snooze; null when they already have the maximum. Twitch sends Unix seconds (0 for none).</summary>
    [JsonConverter(typeof(UnixSecondsDateTimeOffsetConverter))]
    public DateTimeOffset? SnoozeRefreshAt { get; init; }
    /// <summary>The next scheduled ad; null when no ad is scheduled or the channel is not live. Twitch sends Unix seconds (0 for none).</summary>
    [JsonConverter(typeof(UnixSecondsDateTimeOffsetConverter))]
    public DateTimeOffset? NextAdAt { get; init; }
    public int Duration { get; init; }
    /// <summary>The last ad break; null when no ad has run or the channel is not live. Twitch sends Unix seconds (0 for none).</summary>
    [JsonConverter(typeof(UnixSecondsDateTimeOffsetConverter))]
    public DateTimeOffset? LastAdAt { get; init; }
    public int PrerollFreeTime { get; init; }
}

public sealed class AdSnoozeResult
{
    public int SnoozeCount { get; init; }
    /// <summary>When the broadcaster gains another snooze; null when they already have the maximum. Twitch sends Unix seconds (0 for none).</summary>
    [JsonConverter(typeof(UnixSecondsDateTimeOffsetConverter))]
    public DateTimeOffset? SnoozeRefreshAt { get; init; }
    /// <summary>The next scheduled ad after the snooze. Twitch sends Unix seconds (0 for none).</summary>
    [JsonConverter(typeof(UnixSecondsDateTimeOffsetConverter))]
    public DateTimeOffset? NextAdAt { get; init; }
}
