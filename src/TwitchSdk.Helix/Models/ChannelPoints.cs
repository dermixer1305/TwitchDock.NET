using System.Text.Json.Serialization;

namespace TwitchSdk.Helix.Models;

/// <summary>Optional reward settings. Null omits a field; false and zero are serialized explicitly.</summary>
public abstract class CustomRewardOptions
{
    public string? Prompt { get; init; }
    public bool? IsEnabled { get; init; }
    public string? BackgroundColor { get; init; }
    public bool? IsUserInputRequired { get; init; }
    public bool? IsMaxPerStreamEnabled { get; init; }
    public long? MaxPerStream { get; init; }
    public bool? IsMaxPerUserPerStreamEnabled { get; init; }
    public long? MaxPerUserPerStream { get; init; }
    public bool? IsGlobalCooldownEnabled { get; init; }
    public long? GlobalCooldownSeconds { get; init; }
    public bool? ShouldRedemptionsSkipRequestQueue { get; init; }
}

public sealed class CreateCustomRewardRequest : CustomRewardOptions
{
    public required string Title { get; init; }
    public required long Cost { get; init; }
}

public sealed class UpdateCustomRewardRequest : CustomRewardOptions
{
    public string? Title { get; init; }
    public long? Cost { get; init; }
    public bool? IsPaused { get; init; }
}

public sealed class GetCustomRewardsRequest
{
    public required string BroadcasterId { get; init; }
    public IReadOnlyList<string> Ids { get; init => field = value ?? []; } = [];
    public bool? OnlyManageableRewards { get; init; }
}

public sealed class CustomReward
{
    public required string BroadcasterId { get; init; }
    public required string BroadcasterLogin { get; init; }
    public required string BroadcasterName { get; init; }
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Prompt { get; init => field = value ?? ""; } = "";
    public long Cost { get; init; }
    public RewardImages? Image { get; init; }
    public required RewardImages DefaultImage { get; init; }
    public required string BackgroundColor { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsUserInputRequired { get; init; }
    public RewardMaxPerStreamSetting MaxPerStreamSetting { get; init => field = value ?? new(); } = new();
    public RewardMaxPerUserPerStreamSetting MaxPerUserPerStreamSetting { get; init => field = value ?? new(); } = new();
    public RewardGlobalCooldownSetting GlobalCooldownSetting { get; init => field = value ?? new(); } = new();
    public bool IsPaused { get; init; }
    public bool IsInStock { get; init; }
    public bool ShouldRedemptionsSkipRequestQueue { get; init; }
    public int? RedemptionsRedeemedCurrentStream { get; init; }
    public DateTimeOffset? CooldownExpiresAt { get; init; }
}

public sealed class RewardImages
{
    [JsonPropertyName("url_1x")]
    public required string Url1x { get; init; }
    [JsonPropertyName("url_2x")]
    public required string Url2x { get; init; }
    [JsonPropertyName("url_4x")]
    public required string Url4x { get; init; }
}

public sealed class RewardMaxPerStreamSetting
{
    public bool IsEnabled { get; init; }
    public long MaxPerStream { get; init; }
}

public sealed class RewardMaxPerUserPerStreamSetting
{
    public bool IsEnabled { get; init; }
    public long MaxPerUserPerStream { get; init; }
}

public sealed class RewardGlobalCooldownSetting
{
    public bool IsEnabled { get; init; }
    public long GlobalCooldownSeconds { get; init; }
}

public sealed record GetCustomRewardRedemptionsRequest
{
    public required string BroadcasterId { get; init; }
    public required string RewardId { get; init; }
    public string? Status { get; init; }
    public IReadOnlyList<string> Ids { get; init => field = value ?? []; } = [];
    public string? Sort { get; init; }
    public string? After { get; init; }
    public int? First { get; init; }
}

public sealed class UpdateRedemptionStatusRequest
{
    [JsonIgnore]
    public string BroadcasterId { get; init => field = value ?? ""; } = "";
    [JsonIgnore]
    public string RewardId { get; init => field = value ?? ""; } = "";
    [JsonIgnore]
    public IReadOnlyList<string> Ids { get; init => field = value ?? []; } = [];
    public required string Status { get; init; }
}

public sealed class CustomRewardRedemption
{
    public required string BroadcasterId { get; init; }
    public required string BroadcasterLogin { get; init; }
    public required string BroadcasterName { get; init; }
    public required string Id { get; init; }
    public required string UserLogin { get; init; }
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public string UserInput { get; init => field = value ?? ""; } = "";
    public required string Status { get; init; }
    public DateTimeOffset RedeemedAt { get; init; }
    public required RedeemedReward Reward { get; init; }
}

public sealed class RedeemedReward
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Prompt { get; init => field = value ?? ""; } = "";
    public long Cost { get; init; }
}
