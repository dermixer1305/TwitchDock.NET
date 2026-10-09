using TwitchDock.Core;

namespace TwitchDock.Helix.Models;

/// <summary>Extensible condition map; typed conditions for each subscription are tracked separately in coverage.</summary>
public sealed class CreateEventSubSubscriptionRequest
{
    public required string Type { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyDictionary<string, string> Condition { get; init; }
    public required EventSubTransportRequest Transport { get; init; }
    /// <summary>Required (true) for batched subscription types such as drop.entitlement.grant; omitted when null.</summary>
    public bool? IsBatchingEnabled { get; init; }
}

/// <summary>Only the fields belonging to the selected method may be supplied.</summary>
public sealed class EventSubTransportRequest
{
    public required string Method { get; init; }
    public string? Callback { get; init; }
    public string? Secret { get; init; }
    public string? SessionId { get; init; }
    public string? ConduitId { get; init; }
    public override string ToString() => $"EventSubTransportRequest ({Method}) [secret redacted]";
}

public sealed class EventSubTransport
{
    public required string Method { get; init; }
    public string? Callback { get; init; }
    public string? SessionId { get; init; }
    public string? ConduitId { get; init; }
    public DateTimeOffset? ConnectedAt { get; init; }
    public DateTimeOffset? DisconnectedAt { get; init; }
}

public sealed class EventSubSubscription
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public required string Type { get; init; }
    public required string Version { get; init; }
    public IReadOnlyDictionary<string, string> Condition { get; init => field = value ?? new Dictionary<string, string>(); } = new Dictionary<string, string>();
    public DateTimeOffset CreatedAt { get; init; }
    public required EventSubTransport Transport { get; init; }
    public int Cost { get; init; }
}

public sealed class EventSubSubscriptionsResponse
{
    public IReadOnlyList<EventSubSubscription> Data { get; init => field = value ?? []; } = [];
    public int Total { get; init; }
    public int TotalCost { get; init; }
    public int MaxTotalCost { get; init; }
    public Pagination? Pagination { get; init; }
}

public sealed record GetEventSubSubscriptionsRequest
{
    public string? Status { get; init; }
    public string? Type { get; init; }
    public string? UserId { get; init; }
    public string? SubscriptionId { get; init; }
    public string? ConduitId { get; init; }
    public string? After { get; init; }
}
