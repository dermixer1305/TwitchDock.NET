using System.Text.Json.Serialization;
using TwitchSdk.Core;

namespace TwitchSdk.Helix.Models;

public sealed record GetModeratedChannelsRequest
{
    /// <summary>Must match the user in the token (or the user an app token was authorized for).</summary>
    public required string UserId { get; init; }
    public int? First { get; init; }
    public string? After { get; init; }
}

public sealed class ModeratedChannel
{
    public required string BroadcasterId { get; init; }
    public required string BroadcasterLogin { get; init; }
    public required string BroadcasterName { get; init; }
}

public sealed record GetModeratorsRequest
{
    public required string BroadcasterId { get; init; }
    /// <summary>Up to 100 user IDs. Only users who are moderators are returned, in the requested order.</summary>
    public IReadOnlyList<string> UserIds { get; init; } = [];
    public int? First { get; init; }
    public string? After { get; init; }
}

public sealed class ChannelModerator
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
}

public sealed record GetVipsRequest
{
    public required string BroadcasterId { get; init; }
    /// <summary>Up to 100 user IDs. Users that are not VIPs are ignored.</summary>
    public IReadOnlyList<string> UserIds { get; init; } = [];
    public int? First { get; init; }
    public string? After { get; init; }
}

public sealed class ChannelVip
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string UserLogin { get; init; }
}

public sealed class UpdateShieldModeStatusRequest
{
    public required bool IsActive { get; init; }
}

public sealed class ShieldModeStatus
{
    public bool IsActive { get; init; }
    /// <summary>The moderator that last activated Shield Mode; empty if it was never activated.</summary>
    public string ModeratorId { get; init; } = "";
    public string ModeratorLogin { get; init; } = "";
    public string ModeratorName { get; init; } = "";
    /// <summary>Null when Twitch sends an empty string because Shield Mode was never activated.</summary>
    [JsonConverter(typeof(EmptyStringAsNullDateTimeOffsetConverter))]
    public DateTimeOffset? LastActivatedAt { get; init; }
}

public sealed class WarnChatUserRequest
{
    public required string UserId { get; init; }
    /// <summary>Required; at most 500 characters.</summary>
    public required string Reason { get; init; }
}

/// <summary>Wire envelope: Twitch expects the warning as a single object under <c>data</c>.</summary>
public sealed class WarnChatUserBody
{
    public required WarnChatUserRequest Data { get; init; }
}

public sealed class ChatUserWarning
{
    public required string BroadcasterId { get; init; }
    public required string UserId { get; init; }
    public required string ModeratorId { get; init; }
    public required string Reason { get; init; }
}

public sealed class AddSuspiciousStatusRequest
{
    public required string UserId { get; init; }
    /// <summary>ACTIVE_MONITORING or RESTRICTED.</summary>
    public required string Status { get; init; }
}

public sealed class SuspiciousChatUserStatus
{
    public required string UserId { get; init; }
    public required string BroadcasterId { get; init; }
    public required string ModeratorId { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    /// <summary>ACTIVE_MONITORING or RESTRICTED after adding; NO_TREATMENT after removal. Kept as a string for new values.</summary>
    public required string Status { get; init; }
    /// <summary>For example MANUALLY_ADDED, DETECTED_BAN_EVADER, DETECTED_SUS_CHATTER or BANNED_IN_SHARED_CHANNEL.</summary>
    public IReadOnlyList<string> Types { get; init; } = [];
}
