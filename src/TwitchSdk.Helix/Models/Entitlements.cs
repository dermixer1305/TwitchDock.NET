namespace TwitchSdk.Helix.Models;

/// <summary>Case-sensitive entitlement fulfillment statuses.</summary>
public static class DropsFulfillmentStatuses
{
    /// <summary>The user claimed the benefit.</summary>
    public const string Claimed = "CLAIMED";
    /// <summary>The developer granted the claimed benefit.</summary>
    public const string Fulfilled = "FULFILLED";
}

/// <summary>Per-group results of Update Drops Entitlements.</summary>
public static class DropsEntitlementUpdateStatuses
{
    public const string InvalidId = "INVALID_ID";
    public const string NotFound = "NOT_FOUND";
    public const string Success = "SUCCESS";
    public const string Unauthorized = "UNAUTHORIZED";
    /// <summary>Transient failure; retry these IDs later.</summary>
    public const string UpdateFailed = "UPDATE_FAILED";
}

/// <summary>
/// App tokens may filter by user, game or both (none returns every entitlement of the organization).
/// User tokens are implicitly limited to the token user and may filter only by game; UserId is invalid with them.
/// </summary>
public sealed record GetDropsEntitlementsRequest
{
    /// <summary>Up to 100 entitlement IDs.</summary>
    public IReadOnlyList<string> Ids { get; init; } = [];
    public string? UserId { get; init; }
    public string? GameId { get; init; }
    /// <summary>CLAIMED or FULFILLED.</summary>
    public string? FulfillmentStatus { get; init; }
    public string? After { get; init; }
    /// <summary>1–1000; Twitch defaults to 20.</summary>
    public int? First { get; init; }
}

/// <summary>Entitlements are not sorted by any returned field.</summary>
public sealed class DropsEntitlement
{
    public required string Id { get; init; }
    public required string BenefitId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public required string UserId { get; init; }
    public required string GameId { get; init; }
    /// <summary>CLAIMED or FULFILLED.</summary>
    public required string FulfillmentStatus { get; init; }
    public DateTimeOffset LastUpdated { get; init; }
}

/// <summary>Both fields are optional per the reference; null fields are omitted from the body.</summary>
public sealed class UpdateDropsEntitlementsRequest
{
    /// <summary>Up to 100 entitlement IDs.</summary>
    public IReadOnlyList<string>? EntitlementIds { get; init; }
    /// <summary>CLAIMED or FULFILLED.</summary>
    public string? FulfillmentStatus { get; init; }
}

/// <summary>One status group. HTTP 200 does not mean every entitlement was updated; inspect each group.</summary>
public sealed class DropsEntitlementUpdate
{
    /// <summary>See <see cref="DropsEntitlementUpdateStatuses"/>.</summary>
    public required string Status { get; init; }
    public IReadOnlyList<string> Ids { get; init; } = [];
}
