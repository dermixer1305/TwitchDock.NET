namespace TwitchDock.Helix.Models;

public sealed class Conduit
{
    public required string Id { get; init; }
    public int ShardCount { get; init; }
}

public sealed class CreateConduitRequest
{
    public required int ShardCount { get; init; }
}

public sealed class UpdateConduitRequest
{
    public required string Id { get; init; }
    public required int ShardCount { get; init; }
}

public sealed record GetConduitShardsRequest
{
    public required string ConduitId { get; init; }
    public string? Status { get; init; }
    public string? After { get; init; }
}

public sealed class ConduitShard
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public required ConduitShardTransport Transport { get; init; }
}

public sealed class ConduitShardTransport
{
    public required string Method { get; init; }
    public string? Callback { get; init; }
    public string? SessionId { get; init; }
    public DateTimeOffset? ConnectedAt { get; init; }
    public DateTimeOffset? DisconnectedAt { get; init; }
}

public sealed class UpdateConduitShardsRequest
{
    public required string ConduitId { get; init; }
    public required IReadOnlyList<ConduitShardUpdate> Shards { get; init; }
}

public sealed class ConduitShardUpdate
{
    /// <summary>Zero-based shard identifier. Twitch validates it against the current shard count.</summary>
    public required string Id { get; init; }
    public required ConduitShardTransportRequest Transport { get; init; }
}

public sealed class ConduitShardTransportRequest
{
    /// <summary>webhook or websocket; optional according to the shard-update contract.</summary>
    public string? Method { get; init; }
    public string? Callback { get; init; }
    public string? Secret { get; init; }
    public string? SessionId { get; init; }
    public override string ToString() => $"ConduitShardTransportRequest {{ Method = {Method}, Secret = [REDACTED] }}";
}

/// <summary>HTTP 202 may contain both successful updates and individual shard errors. Always inspect Errors.</summary>
public sealed class UpdateConduitShardsResponse
{
    public IReadOnlyList<ConduitShard> Data { get; init => field = value ?? []; } = [];
    public IReadOnlyList<ConduitShardError> Errors { get; init => field = value ?? []; } = [];
}

public sealed class ConduitShardError
{
    public required string Id { get; init; }
    public required string Message { get; init; }
    public required string Code { get; init; }
}
