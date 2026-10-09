using System.Text.Json.Serialization;

namespace TwitchSdk.Helix.Models;

public sealed class GetBitsLeaderboardRequest
{
    public int? Count { get; init; }
    public string? Period { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public string? UserId { get; init; }
}

public sealed class BitsLeaderboardResponse
{
    public IReadOnlyList<BitsLeaderboardEntry> Data { get; init => field = value ?? []; } = [];
    public BitsLeaderboardDateRange DateRange { get; init => field = value ?? new(); } = new();
    public int Total { get; init; }
}

public sealed class BitsLeaderboardEntry
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public int Rank { get; init; }
    public long Score { get; init; }
}

/// <summary>Dates may be empty strings when started_at was not supplied.</summary>
public sealed class BitsLeaderboardDateRange
{
    public string StartedAt { get; init => field = value ?? ""; } = "";
    public string EndedAt { get; init => field = value ?? ""; } = "";
}

public sealed class Cheermote
{
    public required string Prefix { get; init; }
    public IReadOnlyList<CheermoteTier> Tiers { get; init => field = value ?? []; } = [];
    public required string Type { get; init; }
    public int Order { get; init; }
    public DateTimeOffset LastUpdated { get; init; }
    public bool IsCharitable { get; init; }
}

public sealed class CheermoteTier
{
    public int MinBits { get; init; }
    public required string Id { get; init; }
    public required string Color { get; init; }
    public CheermoteImages Images { get; init => field = value ?? new(); } = new();
    public bool CanCheer { get; init; }
    public bool ShowInBitsCard { get; init; }
}

public sealed class CheermoteImages
{
    public CheermoteImageFormats Dark { get; init => field = value ?? new(); } = new();
    public CheermoteImageFormats Light { get; init => field = value ?? new(); } = new();
}

public sealed class CheermoteImageFormats
{
    public IReadOnlyDictionary<string, string> Animated { get; init => field = value ?? new Dictionary<string, string>(); } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Static { get; init => field = value ?? new Dictionary<string, string>(); } = new Dictionary<string, string>();
}

public sealed record GetExtensionTransactionsRequest
{
    public required string ExtensionId { get; init; }
    public IReadOnlyList<string> Ids { get; init => field = value ?? []; } = [];
    public int? First { get; init; }
    public string? After { get; init; }
}

public sealed class ExtensionTransaction
{
    public required string Id { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public required string BroadcasterId { get; init; }
    public required string BroadcasterLogin { get; init; }
    public required string BroadcasterName { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public required string ProductType { get; init; }
    public required ExtensionTransactionProduct ProductData { get; init; }
}

public sealed class ExtensionTransactionProduct
{
    public required string Sku { get; init; }
    public required string Domain { get; init; }
    public required ExtensionProductCost Cost { get; init; }
    [JsonPropertyName("inDevelopment")]
    public bool InDevelopment { get; init; }
    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }
    public string Expiration { get; init => field = value ?? ""; } = "";
    public bool Broadcast { get; init; }
}

public sealed class ExtensionProductCost
{
    public int Amount { get; init; }
    public required string Type { get; init; }
}
