namespace TwitchSdk.Helix.Models;

public sealed record GetPredictionsRequest
{
    public required string BroadcasterId { get; init; }
    public IReadOnlyList<string> Ids { get; init => field = value ?? []; } = [];
    public int? First { get; init; }
    public string? After { get; init; }
}

public sealed class CreatePredictionRequest
{
    public required string BroadcasterId { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyList<PredictionOutcomeRequest> Outcomes { get; init; }
    public required int PredictionWindow { get; init; }
}

public sealed class PredictionOutcomeRequest
{
    public required string Title { get; init; }
}

public sealed class EndPredictionRequest
{
    public required string BroadcasterId { get; init; }
    public required string Id { get; init; }
    public required string Status { get; init; }
    public string? WinningOutcomeId { get; init; }
}

public sealed class TwitchPrediction
{
    public required string Id { get; init; }
    public required string BroadcasterId { get; init; }
    public required string BroadcasterName { get; init; }
    public required string BroadcasterLogin { get; init; }
    public required string Title { get; init; }
    public string? WinningOutcomeId { get; init; }
    public IReadOnlyList<PredictionOutcome> Outcomes { get; init => field = value ?? []; } = [];
    public int PredictionWindow { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public DateTimeOffset? LockedAt { get; init; }
}

public sealed class PredictionOutcome
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public long Users { get; init; }
    public long ChannelPoints { get; init; }
    public IReadOnlyList<TopPredictor>? TopPredictors { get; init; }
    public required string Color { get; init; }
}

public sealed class TopPredictor
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string UserLogin { get; init; }
    public long ChannelPointsUsed { get; init; }
    public long? ChannelPointsWon { get; init; }
}
