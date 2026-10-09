using System.Text.Json.Serialization;
using TwitchSdk.Core;

namespace TwitchSdk.Helix.Models;

public sealed record GetChannelStreamScheduleRequest
{
    public required string BroadcasterId { get; init; }
    public IReadOnlyList<string> Ids { get; init; } = [];
    public DateTimeOffset? StartTime { get; init; }
    public int? First { get; init; }
    public string? After { get; init; }
}

public sealed class ChannelStreamScheduleResponse
{
    public required ChannelStreamSchedule Data { get; init; }
    public Pagination? Pagination { get; init; }
}

public sealed class ChannelStreamSchedule
{
    public IReadOnlyList<ScheduleSegment> Segments { get; init; } = [];
    public required string BroadcasterId { get; init; }
    public required string BroadcasterName { get; init; }
    public required string BroadcasterLogin { get; init; }
    public ScheduleVacation? Vacation { get; init; }
}

public sealed class ScheduleSegment
{
    public required string Id { get; init; }
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset EndTime { get; init; }
    public string Title { get; init; } = "";
    public DateTimeOffset? CanceledUntil { get; init; }
    public ScheduleCategory? Category { get; init; }
    public bool IsRecurring { get; init; }
}

public sealed class ScheduleCategory
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}

public sealed class ScheduleVacation
{
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset EndTime { get; init; }
}

public sealed class UpdateChannelStreamScheduleRequest
{
    public required string BroadcasterId { get; init; }
    public bool? IsVacationEnabled { get; init; }
    public DateTimeOffset? VacationStartTime { get; init; }
    public DateTimeOffset? VacationEndTime { get; init; }
    public string? Timezone { get; init; }
}

public sealed class CreateScheduleSegmentRequest
{
    public required DateTimeOffset StartTime { get; init; }
    public required string Timezone { get; init; }
    /// <summary>Minutes, serialized as a JSON string as documented by Twitch.</summary>
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public required int Duration { get; init; }
    public bool? IsRecurring { get; init; }
    public string? CategoryId { get; init; }
    public string? Title { get; init; }
}

public sealed class UpdateScheduleSegmentRequest
{
    public DateTimeOffset? StartTime { get; init; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public int? Duration { get; init; }
    public string? CategoryId { get; init; }
    public string? Title { get; init; }
    public bool? IsCanceled { get; init; }
    public string? Timezone { get; init; }
}
