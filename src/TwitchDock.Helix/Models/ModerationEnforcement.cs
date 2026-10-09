using System.Text.Json.Serialization;
using TwitchDock.Core;

namespace TwitchDock.Helix.Models;

/// <summary>Body of Check AutoMod Status. The broadcaster ID is sent as a query parameter.</summary>
public sealed class CheckAutoModStatusRequest
{
    [JsonIgnore]
    public string BroadcasterId { get; init => field = value ?? ""; } = "";
    /// <summary>1–100 messages to check.</summary>
    public IReadOnlyList<AutoModCheckMessage> Data { get; init => field = value ?? []; } = [];
}

public sealed class AutoModCheckMessage
{
    /// <summary>Caller-defined ID that correlates the message with its result.</summary>
    public required string MsgId { get; init; }
    public required string MsgText { get; init; }
}

public sealed class AutoModCheckResult
{
    public required string MsgId { get; init; }
    public bool IsPermitted { get; init; }
}

public sealed class ManageHeldAutoModMessageRequest
{
    /// <summary>The moderator approving or denying the message; must match the token user.</summary>
    public required string UserId { get; init; }
    public required string MsgId { get; init; }
    /// <summary>ALLOW or DENY.</summary>
    public required string Action { get; init; }
}

public sealed class AutoModSettings
{
    public required string BroadcasterId { get; init; }
    public required string ModeratorId { get; init; }
    /// <summary>Null when the broadcaster configured individual levels.</summary>
    public int? OverallLevel { get; init; }
    public int Disability { get; init; }
    public int Aggression { get; init; }
    public int SexualitySexOrGender { get; init; }
    public int Misogyny { get; init; }
    public int Bullying { get; init; }
    public int Swearing { get; init; }
    public int RaceEthnicityOrReligion { get; init; }
    public int SexBasedTerms { get; init; }
}

/// <summary>
/// PUT overwrites the settings: set either <see cref="OverallLevel"/> or the individual levels you want after the update, not both.
/// Null omits a field; zero is serialized. Levels range from 0 (no filtering) to 4 (most aggressive).
/// </summary>
public sealed class UpdateAutoModSettingsRequest
{
    [JsonIgnore]
    public string BroadcasterId { get; init => field = value ?? ""; } = "";
    [JsonIgnore]
    public string ModeratorId { get; init => field = value ?? ""; } = "";
    public int? OverallLevel { get; init; }
    public int? Disability { get; init; }
    public int? Aggression { get; init; }
    public int? SexualitySexOrGender { get; init; }
    public int? Misogyny { get; init; }
    public int? Bullying { get; init; }
    public int? Swearing { get; init; }
    public int? RaceEthnicityOrReligion { get; init; }
    public int? SexBasedTerms { get; init; }
}

public sealed record GetBannedUsersRequest
{
    public required string BroadcasterId { get; init; }
    /// <summary>Up to 100 user IDs; results keep the requested order.</summary>
    public IReadOnlyList<string> UserIds { get; init => field = value ?? []; } = [];
    public int? First { get; init; }
    public string? After { get; init; }
    public string? Before { get; init; }
}

public sealed class BannedUser
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    /// <summary>Timeout end. Twitch sends an empty string for permanent bans, which maps to null.</summary>
    [JsonConverter(typeof(EmptyStringAsNullDateTimeOffsetConverter))]
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    /// <summary>Empty when the moderator gave no reason.</summary>
    public string Reason { get; init => field = value ?? ""; } = "";
    public required string ModeratorId { get; init; }
    public required string ModeratorLogin { get; init; }
    public required string ModeratorName { get; init; }
}

/// <summary>Body of Ban User, wrapped in <c>data</c> as documented. Broadcaster and moderator IDs are query parameters.</summary>
public sealed class BanUserRequest
{
    [JsonIgnore]
    public string BroadcasterId { get; init => field = value ?? ""; } = "";
    [JsonIgnore]
    public string ModeratorId { get; init => field = value ?? ""; } = "";
    public required BanUserData Data { get; init; }
}

public sealed class BanUserData
{
    public required string UserId { get; init; }
    /// <summary>Timeout in seconds (1–1209600). Null bans permanently; 1 ends an existing timeout.</summary>
    public int? Duration { get; init; }
    /// <summary>Optional reason with at most 500 characters.</summary>
    public string? Reason { get; init; }
}

public sealed class BanUserResult
{
    public required string BroadcasterId { get; init; }
    public required string ModeratorId { get; init; }
    public required string UserId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    /// <summary>Null for a permanent ban.</summary>
    [JsonConverter(typeof(EmptyStringAsNullDateTimeOffsetConverter))]
    public DateTimeOffset? EndTime { get; init; }
}

public sealed record GetUnbanRequestsRequest
{
    public required string BroadcasterId { get; init; }
    public required string ModeratorId { get; init; }
    /// <summary>pending, approved, denied, acknowledged or canceled.</summary>
    public required string Status { get; init; }
    public string? UserId { get; init; }
    public string? After { get; init; }
    public int? First { get; init; }
}

/// <summary>Query parameters of Resolve Unban Requests.</summary>
public sealed class UnbanRequestResolution
{
    public required string BroadcasterId { get; init; }
    public required string ModeratorId { get; init; }
    public required string UnbanRequestId { get; init; }
    /// <summary>approved or denied.</summary>
    public required string Status { get; init; }
    /// <summary>Optional message with at most 500 characters.</summary>
    public string? ResolutionText { get; init; }
}

public sealed class UnbanRequest
{
    public required string Id { get; init; }
    public required string BroadcasterId { get; init; }
    public required string BroadcasterName { get; init; }
    public required string BroadcasterLogin { get; init; }
    /// <summary>The resolving moderator; null or empty while unresolved.</summary>
    public string? ModeratorId { get; init; }
    public string? ModeratorLogin { get; init; }
    public string? ModeratorName { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public string Text { get; init => field = value ?? ""; } = "";
    public required string Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    [JsonConverter(typeof(EmptyStringAsNullDateTimeOffsetConverter))]
    public DateTimeOffset? ResolvedAt { get; init; }
    public string? ResolutionText { get; init; }
}

public sealed record GetBlockedTermsRequest
{
    public required string BroadcasterId { get; init; }
    public required string ModeratorId { get; init; }
    public int? First { get; init; }
    public string? After { get; init; }
}

public sealed class AddBlockedTermRequest
{
    [JsonIgnore]
    public string BroadcasterId { get; init => field = value ?? ""; } = "";
    [JsonIgnore]
    public string ModeratorId { get; init => field = value ?? ""; } = "";
    /// <summary>2–500 characters. A wildcard (*) may appear only at the beginning or end of a word.</summary>
    public required string Text { get; init; }
}

public sealed class BlockedTerm
{
    public required string BroadcasterId { get; init; }
    public required string ModeratorId { get; init; }
    public required string Id { get; init; }
    public required string Text { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    /// <summary>Null for manually added or permanently blocked terms.</summary>
    [JsonConverter(typeof(EmptyStringAsNullDateTimeOffsetConverter))]
    public DateTimeOffset? ExpiresAt { get; init; }
}
