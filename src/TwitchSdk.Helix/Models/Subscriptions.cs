using TwitchSdk.Core;

namespace TwitchSdk.Helix.Models;

public sealed record GetBroadcasterSubscriptionsRequest
{
    public required string BroadcasterId { get; init; }
    public IReadOnlyList<string> UserIds { get; init; } = [];
    public int? First { get; init; }
    public string? After { get; init; }
    public string? Before { get; init; }
}

public sealed class BroadcasterSubscriptionsResponse
{
    public IReadOnlyList<BroadcasterSubscription> Data { get; init; } = [];
    public Pagination? Pagination { get; init; }
    public int? Points { get; init; }
    public int? Total { get; init; }
}

public sealed class BroadcasterSubscription
{
    public required string BroadcasterId { get; init; }
    public required string BroadcasterLogin { get; init; }
    public required string BroadcasterName { get; init; }
    public string GifterId { get; init; } = "";
    public string GifterLogin { get; init; } = "";
    public string GifterName { get; init; } = "";
    public bool IsGift { get; init; }
    public required string PlanName { get; init; }
    public required string Tier { get; init; }
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string UserLogin { get; init; }
}

public sealed class CheckUserSubscriptionRequest
{
    public required string BroadcasterId { get; init; }
    public required string UserId { get; init; }
}

public sealed class UserSubscription
{
    public required string BroadcasterId { get; init; }
    public required string BroadcasterLogin { get; init; }
    public required string BroadcasterName { get; init; }
    public string? GifterId { get; init; }
    public string? GifterLogin { get; init; }
    public string? GifterName { get; init; }
    public bool IsGift { get; init; }
    public required string Tier { get; init; }
}
