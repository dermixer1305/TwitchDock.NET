using System.Text.Json.Serialization;

namespace TwitchDock.Helix.Models;

public sealed record GetExtensionAnalyticsRequest
{
    public string? ExtensionId { get; init; }
    public string? Type { get; init; }
    public DateOnly? StartedAt { get; init; }
    public DateOnly? EndedAt { get; init; }
    public int? First { get; init; }
    public string? After { get; init; }
}

public sealed record GetGameAnalyticsRequest
{
    public string? GameId { get; init; }
    public string? Type { get; init; }
    public DateOnly? StartedAt { get; init; }
    public DateOnly? EndedAt { get; init; }
    public int? First { get; init; }
    public string? After { get; init; }
}

public sealed class AnalyticsDateRange
{
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }
}

public sealed class ExtensionAnalyticsReport
{
    public required string ExtensionId { get; init; }
    [JsonPropertyName("URL")] public required string Url { get; init; }
    public required string Type { get; init; }
    public required AnalyticsDateRange DateRange { get; init; }
}

public sealed class GameAnalyticsReport
{
    public required string GameId { get; init; }
    [JsonPropertyName("URL")] public required string Url { get; init; }
    public required string Type { get; init; }
    public required AnalyticsDateRange DateRange { get; init; }
}
