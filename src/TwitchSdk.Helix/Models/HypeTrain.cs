namespace TwitchSdk.Helix.Models;

public sealed class HypeTrainStatus
{
    public CurrentHypeTrain? Current { get; init; }
    public HypeTrainRecord? AllTimeHigh { get; init; }
    public HypeTrainRecord? SharedAllTimeHigh { get; init; }
}

public sealed class CurrentHypeTrain
{
    public required string Id { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public int Level { get; init; }
    public long Total { get; init; }
    public long Progress { get; init; }
    public long Goal { get; init; }
    public IReadOnlyList<HypeTrainContribution> TopContributions { get; init => field = value ?? []; } = [];
    public IReadOnlyList<HypeTrainParticipant>? SharedTrainParticipants { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public required string Type { get; init; }
    public bool IsSharedTrain { get; init; }
}

public sealed class HypeTrainContribution
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public required string Type { get; init; }
    public long Total { get; init; }
}

public sealed class HypeTrainParticipant
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
}

public sealed class HypeTrainRecord
{
    public int Level { get; init; }
    public long Total { get; init; }
    public DateTimeOffset AchievedAt { get; init; }
}
