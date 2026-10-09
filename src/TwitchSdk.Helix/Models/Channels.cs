using System.Text.Json.Serialization;

namespace TwitchSdk.Helix.Models;

public sealed class ModifyChannelInformationRequest
{
    [JsonIgnore]
    public string BroadcasterId { get; init => field = value ?? ""; } = "";
    public string? GameId { get; init; }
    public string? BroadcasterLanguage { get; init; }
    public string? Title { get; init; }
    public int? Delay { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public IReadOnlyList<ChannelContentClassificationLabel>? ContentClassificationLabels { get; init; }
    public bool? IsBrandedContent { get; init; }
}

public sealed class ChannelContentClassificationLabel
{
    public required string Id { get; init; }
    public required bool IsEnabled { get; init; }
}

public sealed class ChannelEditor
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record GetFollowedChannelsRequest
{
    public required string UserId { get; init; }
    public string? BroadcasterId { get; init; }
    public int? First { get; init; }
    public string? After { get; init; }
}

public sealed class FollowedChannel
{
    public required string BroadcasterId { get; init; }
    public required string BroadcasterLogin { get; init; }
    public required string BroadcasterName { get; init; }
    public DateTimeOffset FollowedAt { get; init; }
}

public sealed record GetChannelFollowersRequest
{
    public required string BroadcasterId { get; init; }
    public string? UserId { get; init; }
    public int? First { get; init; }
    public string? After { get; init; }
}

public sealed class ChannelFollower
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public DateTimeOffset FollowedAt { get; init; }
}

public sealed class ChannelInformation
{
    public required string BroadcasterId { get; init; }
    public required string BroadcasterLogin { get; init; }
    public required string BroadcasterName { get; init; }
    public string BroadcasterLanguage { get; init => field = value ?? ""; } = "";
    public string GameId { get; init => field = value ?? ""; } = "";
    public string GameName { get; init => field = value ?? ""; } = "";
    public string Title { get; init => field = value ?? ""; } = "";
    public uint Delay { get; init; }
    public IReadOnlyList<string> Tags { get; init => field = value ?? []; } = [];
    public IReadOnlyList<string> ContentClassificationLabels { get; init => field = value ?? []; } = [];
    public bool IsBrandedContent { get; init; }
}
