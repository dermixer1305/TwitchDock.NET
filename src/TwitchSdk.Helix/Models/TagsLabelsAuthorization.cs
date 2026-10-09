namespace TwitchSdk.Helix.Models;

public sealed record GetAllStreamTagsRequest
{
    /// <summary>Up to 100 tag IDs. Twitch ignores invalid IDs but not duplicates.</summary>
    public IReadOnlyList<string> TagIds { get; init; } = [];
    /// <summary>Page size between 1 and 100. Twitch defaults to 20.</summary>
    public int? First { get; init; }
    public string? After { get; init; }
}

/// <summary>A legacy Twitch-defined stream tag. Channel-defined tags are exposed as Tags on channels and streams.</summary>
public sealed class StreamTag
{
    public required string TagId { get; init; }
    public bool IsAuto { get; init; }
    /// <summary>Localized names keyed by locale, for example en-us.</summary>
    public IReadOnlyDictionary<string, string> LocalizationNames { get; init; } = new Dictionary<string, string>();
    /// <summary>Localized descriptions keyed by locale, for example en-us.</summary>
    public IReadOnlyDictionary<string, string> LocalizationDescriptions { get; init; } = new Dictionary<string, string>();
}

public sealed class ContentClassificationLabel
{
    public required string Id { get; init; }
    /// <summary>Localized description.</summary>
    public required string Description { get; init; }
    /// <summary>Localized name.</summary>
    public required string Name { get; init; }
}

public sealed class UserAuthorization
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string UserLogin { get; init; }
    /// <summary>All scopes the user granted to the client ID; empty when the user has not authorized it.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];
    public bool HasAuthorized { get; init; }
}

public sealed class GetCustomPowerUpsRequest
{
    /// <summary>Must match the token user.</summary>
    public required string BroadcasterId { get; init; }
    /// <summary>Up to 50 Power-up IDs. If none are found, Twitch returns 404.</summary>
    public IReadOnlyList<string> Ids { get; init; } = [];
}

/// <summary>A custom Bits Power-up. Image, limit and cooldown objects share the custom reward wire shapes.</summary>
public sealed class CustomPowerUp
{
    public required string BroadcasterId { get; init; }
    public required string BroadcasterLogin { get; init; }
    public required string BroadcasterName { get; init; }
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Prompt { get; init; } = "";
    public long Bits { get; init; }
    /// <summary>Null when the broadcaster did not upload images.</summary>
    public RewardImages? Image { get; init; }
    public required RewardImages DefaultImage { get; init; }
    public required string BackgroundColor { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsUserInputRequired { get; init; }
    public RewardMaxPerStreamSetting MaxPerStreamSetting { get; init; } = new();
    public RewardMaxPerUserPerStreamSetting MaxPerUserPerStreamSetting { get; init; } = new();
    public RewardGlobalCooldownSetting GlobalCooldownSetting { get; init; } = new();
    public bool IsPaused { get; init; }
    public bool IsInStock { get; init; }
    /// <summary>Null when the stream is offline or no per-stream limit is enabled.</summary>
    public int? RedemptionsRedeemedCurrentStream { get; init; }
    /// <summary>Null when the Power-up is not cooling down.</summary>
    public DateTimeOffset? CooldownExpiresAt { get; init; }
}
