namespace TwitchDock.Helix.Models;

/// <summary>A broadcaster's chat settings. Durations are null while their mode is off.</summary>
public sealed class ChatSettings
{
    public required string BroadcasterId { get; init; }
    public bool EmoteMode { get; init; }
    public bool FollowerMode { get; init; }
    /// <summary>Minutes a user must follow before chatting.</summary>
    public int? FollowerModeDuration { get; init; }
    /// <summary>Returned only for user tokens with moderator:read:chat_settings.</summary>
    public string? ModeratorId { get; init; }
    /// <summary>Get Chat Settings returns this only when a moderator ID of a moderator was supplied with moderator:read:chat_settings.</summary>
    public bool? NonModeratorChatDelay { get; init; }
    /// <summary>Delay in seconds; returned under the same conditions as <see cref="NonModeratorChatDelay"/>.</summary>
    public int? NonModeratorChatDelayDuration { get; init; }
    public bool SlowMode { get; init; }
    /// <summary>Seconds a user must wait between messages.</summary>
    public int? SlowModeWaitTime { get; init; }
    public bool SubscriberMode { get; init; }
    public bool UniqueChatMode { get; init; }
}

/// <summary>Chat settings to change. Null omits a field; false and zero are serialized explicitly.</summary>
public sealed class UpdateChatSettingsRequest
{
    public bool? EmoteMode { get; init; }
    public bool? FollowerMode { get; init; }
    /// <summary>0 (no restriction) through 129600 minutes. Requires <see cref="FollowerMode"/> = true; omit it to use the default 0.</summary>
    public int? FollowerModeDuration { get; init; }
    public bool? NonModeratorChatDelay { get; init; }
    /// <summary>2, 4 or 6 seconds. Required when <see cref="NonModeratorChatDelay"/> is true.</summary>
    public int? NonModeratorChatDelayDuration { get; init; }
    public bool? SlowMode { get; init; }
    /// <summary>3 through 120 seconds. Requires <see cref="SlowMode"/> = true; omit it to use the default 30.</summary>
    public int? SlowModeWaitTime { get; init; }
    public bool? SubscriberMode { get; init; }
    public bool? UniqueChatMode { get; init; }
}

public sealed class SendChatAnnouncementRequest
{
    /// <summary>1 to 500 Unicode code points.</summary>
    public required string Message { get; init; }
    /// <summary>Case-sensitive: blue, green, orange, purple or primary (default, the channel accent color).</summary>
    public string? Color { get; init; }
    /// <summary>App tokens only. Twitch defaults to true (source channel only) during shared chat.</summary>
    public bool? ForSourceOnly { get; init; }
}

public sealed class PinnedChatMessage
{
    public required string MessageId { get; init; }
    public required string BroadcasterId { get; init; }
    public required string SenderUserId { get; init; }
    public required string SenderUserLogin { get; init; }
    public required string SenderUserName { get; init; }
    public required string PinnedByUserId { get; init; }
    public required string PinnedByUserLogin { get; init; }
    public required string PinnedByUserName { get; init; }
    public required PinnedChatMessageContent Message { get; init; }
    public DateTimeOffset StartsAt { get; init; }
    /// <summary>Null when the message stays pinned until the stream ends.</summary>
    public DateTimeOffset? EndsAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class PinnedChatMessageContent
{
    public required string Text { get; init; }
    public IReadOnlyList<PinnedChatFragment> Fragments { get; init => field = value ?? []; } = [];
}

public sealed class PinnedChatFragment
{
    /// <summary>text, emote, cheermote or mention.</summary>
    public required string Type { get; init; }
    public required string Text { get; init; }
    public PinnedChatCheermote? Cheermote { get; init; }
    public PinnedChatEmoticon? Emote { get; init; }
    public PinnedChatMention? Mention { get; init; }
}

public sealed class PinnedChatCheermote
{
    public required string Prefix { get; init; }
    public int Bits { get; init; }
    public int Tier { get; init; }
}

/// <summary>Emote metadata of an emote fragment.</summary>
public sealed class PinnedChatEmoticon
{
    public required string Id { get; init; }
    public required string EmoteSetId { get; init; }
    public required string OwnerId { get; init; }
    public IReadOnlyList<string> Format { get; init => field = value ?? []; } = [];
}

public sealed class PinnedChatMention
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
}

public sealed class UserChatColor
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    /// <summary>Hex color code such as #9146FF; empty when the user has not chosen a color.</summary>
    public string Color { get; init => field = value ?? ""; } = "";
}

public sealed class SharedChatSession
{
    public required string SessionId { get; init; }
    public required string HostBroadcasterId { get; init; }
    public IReadOnlyList<SharedChatParticipant> Participants { get; init => field = value ?? []; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class SharedChatParticipant
{
    public required string BroadcasterId { get; init; }
}
