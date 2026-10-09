using System.Text.Json.Serialization;

namespace TwitchDock.EventSub.Events;

// Discriminators and status values stay strings so new Twitch values never break deserialization.

/// <summary>automod.message.hold v1: a message was caught by AutoMod for review.</summary>
public sealed class AutomodMessageHoldEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public required string MessageId { get; init; }
    /// <summary>Also accepts the plain string shown in Twitch's v1 example; fragments are then empty.</summary>
    [JsonConverter(typeof(AutomodHeldMessageV1Converter))]
    public required AutomodHeldMessage Message { get; init; }
    public required string Category { get; init; }
    /// <summary>Severity level; documented as 1 to 4.</summary>
    public int Level { get; init; }
    public DateTimeOffset HeldAt { get; init; }
}

/// <summary>automod.message.hold v2: a message was caught by AutoMod or by a public blocked term or link.</summary>
public sealed class AutomodMessageHoldEventV2
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public required string MessageId { get; init; }
    public required AutomodHeldMessage Message { get; init; }
    /// <summary>automod, blocked_term or blocked_link. Selects which of <see cref="Automod"/> and <see cref="BlockedTerm"/> is populated.</summary>
    public required string Reason { get; init; }
    /// <summary>Populated when <see cref="Reason"/> is automod.</summary>
    public AutomodCaughtReason? Automod { get; init; }
    /// <summary>Populated when <see cref="Reason"/> is blocked_term or blocked_link.</summary>
    public AutomodBlockedTermReason? BlockedTerm { get; init; }
    public DateTimeOffset HeldAt { get; init; }
}

/// <summary>automod.message.update v1: a held message's status changed.</summary>
public sealed class AutomodMessageUpdateEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public required string ModeratorUserId { get; init; }
    public required string ModeratorUserLogin { get; init; }
    public required string ModeratorUserName { get; init; }
    public required string MessageId { get; init; }
    /// <summary>Also accepts the plain string shown in Twitch's v1 example; fragments are then empty.</summary>
    [JsonConverter(typeof(AutomodHeldMessageV1Converter))]
    public required AutomodHeldMessage Message { get; init; }
    public required string Category { get; init; }
    /// <summary>Severity level; documented as 1 to 4.</summary>
    public int Level { get; init; }
    /// <summary>Approved, Denied or Expired (casing varies in Twitch's documentation; compare case-insensitively).</summary>
    public required string Status { get; init; }
    public DateTimeOffset HeldAt { get; init; }
}

/// <summary>automod.message.update v2: a held message's status changed.</summary>
public sealed class AutomodMessageUpdateEventV2
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public required string ModeratorUserId { get; init; }
    public required string ModeratorUserLogin { get; init; }
    public required string ModeratorUserName { get; init; }
    public required string MessageId { get; init; }
    public required AutomodHeldMessage Message { get; init; }
    /// <summary>Approved, Denied or Expired (casing varies in Twitch's documentation; compare case-insensitively).</summary>
    public required string Status { get; init; }
    public DateTimeOffset HeldAt { get; init; }
    /// <summary>automod, blocked_term or blocked_link. Selects which of <see cref="Automod"/> and <see cref="BlockedTerm"/> is populated.</summary>
    public required string Reason { get; init; }
    public AutomodCaughtReason? Automod { get; init; }
    public AutomodBlockedTermReason? BlockedTerm { get; init; }
}

/// <summary>A message held by AutoMod (automod.message.*, channel.chat.user_message_*).</summary>
public sealed class AutomodHeldMessage
{
    public required string Text { get; init; }
    public IReadOnlyList<AutomodHeldMessageFragment> Fragments { get; init => field = value ?? []; } = [];
}

/// <summary>A fragment of a held or suspicious-user message.</summary>
public sealed class AutomodHeldMessageFragment
{
    /// <summary>text, emote or cheermote. Documented for v2 AutoMod and suspicious-user payloads; null when Twitch omits it.</summary>
    public string? Type { get; init; }
    public required string Text { get; init; }
    public AutomodHeldMessageEmote? Emote { get; init; }
    public AutomodHeldMessageCheermote? Cheermote { get; init; }
}

public sealed class AutomodHeldMessageEmote
{
    public required string Id { get; init; }
    public required string EmoteSetId { get; init; }
}

/// <summary>A cheermote fragment. Bits and tier are also accepted as numeric strings, which the suspicious-user field table documents.</summary>
public sealed class AutomodHeldMessageCheermote
{
    /// <summary>The lowercase name portion of the cheermote; the full cheermote is prefix plus bits.</summary>
    public required string Prefix { get; init; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Bits { get; init; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Tier { get; init; }
}

/// <summary>Details when AutoMod itself caught the message.</summary>
public sealed class AutomodCaughtReason
{
    public required string Category { get; init; }
    /// <summary>Severity level (1-4).</summary>
    public int Level { get; init; }
    public IReadOnlyList<AutomodTextBoundary> Boundaries { get; init => field = value ?? []; } = [];
}

/// <summary>Details when a blocked term or blocked link caught the message.</summary>
public sealed class AutomodBlockedTermReason
{
    public IReadOnlyList<AutomodFoundTerm> TermsFound { get; init => field = value ?? []; } = [];
}

public sealed class AutomodFoundTerm
{
    public required string TermId { get; init; }
    public required AutomodTextBoundary Boundary { get; init; }
    public required string OwnerBroadcasterUserId { get; init; }
    public required string OwnerBroadcasterUserLogin { get; init; }
    public required string OwnerBroadcasterUserName { get; init; }
}

/// <summary>Zero-based, inclusive character bounds of the problematic text.</summary>
public sealed class AutomodTextBoundary
{
    public int StartPos { get; init; }
    public int EndPos { get; init; }
}

/// <summary>automod.settings.update v1.</summary>
public sealed class AutomodSettingsUpdateEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string ModeratorUserId { get; init; }
    public required string ModeratorUserLogin { get; init; }
    public required string ModeratorUserName { get; init; }
    public int Bullying { get; init; }
    /// <summary>The default AutoMod level; null when the broadcaster set individual levels.</summary>
    public int? OverallLevel { get; init; }
    public int Disability { get; init; }
    public int RaceEthnicityOrReligion { get; init; }
    public int Misogyny { get; init; }
    public int SexualitySexOrGender { get; init; }
    public int Aggression { get; init; }
    public int SexBasedTerms { get; init; }
    public int Swearing { get; init; }
}

/// <summary>automod.terms.update v1. Changes to private terms are not sent.</summary>
public sealed class AutomodTermsUpdateEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string ModeratorUserId { get; init; }
    public required string ModeratorUserLogin { get; init; }
    public required string ModeratorUserName { get; init; }
    /// <summary>add_permitted, remove_permitted, add_blocked or remove_blocked.</summary>
    public required string Action { get; init; }
    /// <summary>Whether the term was added by an AutoMod approve or deny action.</summary>
    public bool FromAutomod { get; init; }
    public IReadOnlyList<string> Terms { get; init => field = value ?? []; } = [];
}

/// <summary>channel.chat.clear v1: all messages were cleared from the chat room.</summary>
public sealed class ChannelChatClearEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string BroadcasterUserLogin { get; init; }
}

/// <summary>channel.chat.clear_user_messages v1: all messages of a banned or timed-out user were cleared.</summary>
public sealed class ChannelChatClearUserMessagesEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string TargetUserId { get; init; }
    public required string TargetUserName { get; init; }
    public required string TargetUserLogin { get; init; }
}

/// <summary>channel.chat.message_delete v1: a moderator removed a specific message.</summary>
public sealed class ChannelChatMessageDeleteEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string TargetUserId { get; init; }
    public required string TargetUserName { get; init; }
    public required string TargetUserLogin { get; init; }
    public required string MessageId { get; init; }
}

/// <summary>channel.chat_settings.update v1.</summary>
public sealed class ChannelChatSettingsUpdateEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public bool EmoteMode { get; init; }
    public bool FollowerMode { get; init; }
    /// <summary>Null when <see cref="FollowerMode"/> is false.</summary>
    public int? FollowerModeDurationMinutes { get; init; }
    public bool SlowMode { get; init; }
    /// <summary>Null when <see cref="SlowMode"/> is false.</summary>
    public int? SlowModeWaitTimeSeconds { get; init; }
    public bool SubscriberMode { get; init; }
    public bool UniqueChatMode { get; init; }
}

/// <summary>channel.chat.user_message_hold v1: the chatting user's own message was caught by AutoMod.</summary>
public sealed class ChannelChatUserMessageHoldEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public required string MessageId { get; init; }
    public required AutomodHeldMessage Message { get; init; }
}

/// <summary>channel.chat.user_message_update v1: the AutoMod status of the chatting user's own message changed.</summary>
public sealed class ChannelChatUserMessageUpdateEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    /// <summary>approved, denied or invalid.</summary>
    public required string Status { get; init; }
    public required string MessageId { get; init; }
    public required AutomodHeldMessage Message { get; init; }
}

/// <summary>channel.shared_chat.begin v1: the channel became active in a shared chat session.</summary>
public sealed class ChannelSharedChatBeginEvent
{
    public required string SessionId { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string HostBroadcasterUserId { get; init; }
    public required string HostBroadcasterUserName { get; init; }
    public required string HostBroadcasterUserLogin { get; init; }
    public IReadOnlyList<ChannelSharedChatParticipant> Participants { get; init => field = value ?? []; } = [];
}

/// <summary>channel.shared_chat.update v1: the active shared chat session changed.</summary>
public sealed class ChannelSharedChatUpdateEvent
{
    public required string SessionId { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string HostBroadcasterUserId { get; init; }
    public required string HostBroadcasterUserName { get; init; }
    public required string HostBroadcasterUserLogin { get; init; }
    public IReadOnlyList<ChannelSharedChatParticipant> Participants { get; init => field = value ?? []; } = [];
}

/// <summary>channel.shared_chat.end v1: the channel left the shared chat session or the session ended.</summary>
public sealed class ChannelSharedChatEndEvent
{
    public required string SessionId { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string HostBroadcasterUserId { get; init; }
    public required string HostBroadcasterUserName { get; init; }
    public required string HostBroadcasterUserLogin { get; init; }
}

public sealed class ChannelSharedChatParticipant
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string BroadcasterUserLogin { get; init; }
}

/// <summary>channel.suspicious_user.message v1: a suspicious user sent a chat message.</summary>
public sealed class ChannelSuspiciousUserMessageEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string UserLogin { get; init; }
    /// <summary>none, active_monitoring or restricted.</summary>
    public required string LowTrustStatus { get; init; }
    public IReadOnlyList<string> SharedBanChannelIds { get; init => field = value ?? []; } = [];
    /// <summary>manually_added, ban_evader or banned_in_shared_channel.</summary>
    public IReadOnlyList<string> Types { get; init => field = value ?? []; } = [];
    /// <summary>unknown, possible or likely.</summary>
    public required string BanEvasionEvaluation { get; init; }
    public required SuspiciousUserChatMessage Message { get; init; }
}

/// <summary>The message a suspicious user sent; its fragments have the same shape as AutoMod fragments.</summary>
public sealed class SuspiciousUserChatMessage
{
    public required string MessageId { get; init; }
    public required string Text { get; init; }
    public IReadOnlyList<AutomodHeldMessageFragment> Fragments { get; init => field = value ?? []; } = [];
}

/// <summary>channel.suspicious_user.update v1: a suspicious user's treatment was updated.</summary>
public sealed class ChannelSuspiciousUserUpdateEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string ModeratorUserId { get; init; }
    public required string ModeratorUserName { get; init; }
    public required string ModeratorUserLogin { get; init; }
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string UserLogin { get; init; }
    /// <summary>none, active_monitoring or restricted.</summary>
    public required string LowTrustStatus { get; init; }
}

/// <summary>channel.warning.acknowledge v1: a user acknowledged their warning.</summary>
public sealed class ChannelWarningAcknowledgeEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
}

/// <summary>channel.warning.send v1: a moderator warned a user.</summary>
public sealed class ChannelWarningSendEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string ModeratorUserId { get; init; }
    public required string ModeratorUserLogin { get; init; }
    public required string ModeratorUserName { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyList<string>? ChatRulesCited { get; init; }
}

/// <summary>channel.chat.message v1: a user sent a message to the chat room.</summary>
public sealed class ChannelChatMessageEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string ChatterUserId { get; init; }
    public required string ChatterUserName { get; init; }
    public required string ChatterUserLogin { get; init; }
    /// <summary>A UUID that identifies the message.</summary>
    public required string MessageId { get; init; }
    public required ChatMessageBody Message { get; init; }
    /// <summary>
    /// text, channel_points_highlighted, channel_points_sub_only, user_intro, power_ups_message_effect or power_ups_gigantified_emote.
    /// </summary>
    public string MessageType { get; init => field = value ?? ""; } = "";
    public IReadOnlyList<ChatMessageBadge> Badges { get; init => field = value ?? []; } = [];
    /// <summary>Set when the message is a cheer.</summary>
    public ChatMessageCheer? Cheer { get; init; }
    /// <summary>The chatter's name color as #RRGGBB; empty when never set.</summary>
    public string Color { get; init => field = value ?? ""; } = "";
    /// <summary>Set when the message is a reply.</summary>
    public ChatMessageReply? Reply { get; init; }
    /// <summary>The ID of a redeemed channel points custom reward.</summary>
    public string? ChannelPointsCustomRewardId { get; init; }
    /// <summary>
    /// The animation selected for an "animate my message" power-up. Not listed in the local Twitch field table snapshot;
    /// null when Twitch omits it.
    /// </summary>
    public string? ChannelPointsAnimationId { get; init; }
    /// <summary>Shared chat: the channel the message was sent from; null when sent in the broadcaster's own channel.</summary>
    public string? SourceBroadcasterUserId { get; init; }
    public string? SourceBroadcasterUserName { get; init; }
    public string? SourceBroadcasterUserLogin { get; init; }
    /// <summary>Shared chat: the message ID in the source channel.</summary>
    public string? SourceMessageId { get; init; }
    /// <summary>Shared chat: the chatter's badges in the source channel.</summary>
    public IReadOnlyList<ChatMessageBadge>? SourceBadges { get; init; }
    /// <summary>Shared chat: whether the message is only delivered to the source channel.</summary>
    public bool? IsSourceOnly { get; init; }
}

/// <summary>The structured text of a chat message or notification.</summary>
public sealed class ChatMessageBody
{
    public required string Text { get; init; }
    public IReadOnlyList<ChatMessageFragment> Fragments { get; init => field = value ?? []; } = [];
}

/// <summary>A chat message fragment; the object matching <see cref="Type"/> is populated.</summary>
public sealed class ChatMessageFragment
{
    /// <summary>text, cheermote, emote, mention or gif.</summary>
    public required string Type { get; init; }
    public required string Text { get; init; }
    public ChatMessageCheermote? Cheermote { get; init; }
    public ChatMessageEmote? Emote { get; init; }
    public ChatMessageMention? Mention { get; init; }
    public ChatMessageGif? Gif { get; init; }
}

public sealed class ChatMessageCheermote
{
    /// <summary>The lowercase name portion of the cheermote; the full cheermote is prefix plus bits.</summary>
    public required string Prefix { get; init; }
    public int Bits { get; init; }
    public int Tier { get; init; }
}

public sealed class ChatMessageEmote
{
    public required string Id { get; init; }
    public required string EmoteSetId { get; init; }
    /// <summary>The ID of the broadcaster who owns the emote.</summary>
    public required string OwnerId { get; init; }
    /// <summary>static and/or animated.</summary>
    public IReadOnlyList<string> Format { get; init => field = value ?? []; } = [];
}

public sealed class ChatMessageMention
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string UserLogin { get; init; }
}

public sealed class ChatMessageGif
{
    public required string Id { get; init; }
    /// <summary>Render this URL unmodified.</summary>
    public required string Url { get; init; }
}

public sealed class ChatMessageBadge
{
    public required string SetId { get; init; }
    public required string Id { get; init; }
    /// <summary>Subscriber badges carry the number of subscribed months; empty for other badges.</summary>
    public string Info { get; init => field = value ?? ""; } = "";
}

public sealed class ChatMessageCheer
{
    public int Bits { get; init; }
}

public sealed class ChatMessageReply
{
    public required string ParentMessageId { get; init; }
    public required string ParentMessageBody { get; init; }
    public required string ParentUserId { get; init; }
    public required string ParentUserName { get; init; }
    public required string ParentUserLogin { get; init; }
    public required string ThreadMessageId { get; init; }
    public required string ThreadUserId { get; init; }
    public required string ThreadUserName { get; init; }
    public required string ThreadUserLogin { get; init; }
}

/// <summary>
/// channel.chat.notification v1: a chat notice such as a sub, raid or announcement. <see cref="NoticeType"/> selects the populated
/// variant object; shared_chat_* notice types populate the matching SharedChat* property for notices from another shared chat channel.
/// </summary>
public sealed class ChannelChatNotificationEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string ChatterUserId { get; init; }
    public required string ChatterUserName { get; init; }
    /// <summary>Present in Twitch's examples although missing from the field table; empty when omitted.</summary>
    public string ChatterUserLogin { get; init => field = value ?? ""; } = "";
    public bool ChatterIsAnonymous { get; init; }
    public string Color { get; init => field = value ?? ""; } = "";
    public IReadOnlyList<ChatMessageBadge> Badges { get; init => field = value ?? []; } = [];
    /// <summary>The message Twitch shows in the chat room for this notice.</summary>
    public required string SystemMessage { get; init; }
    public required string MessageId { get; init; }
    /// <summary>The chatter's message; text and fragments may be empty.</summary>
    public required ChatMessageBody Message { get; init; }
    /// <summary>
    /// sub, resub, sub_gift, community_sub_gift, gift_paid_upgrade, prime_paid_upgrade, raid, unraid, pay_it_forward, announcement,
    /// bits_badge_tier, charity_donation, watch_streak, modiversary, gifted_drops_summary, their shared_chat_* counterparts, or unknown.
    /// </summary>
    public required string NoticeType { get; init; }
    public ChatNotificationSub? Sub { get; init; }
    public ChatNotificationResub? Resub { get; init; }
    public ChatNotificationSubGift? SubGift { get; init; }
    public ChatNotificationCommunitySubGift? CommunitySubGift { get; init; }
    public ChatNotificationGiftPaidUpgrade? GiftPaidUpgrade { get; init; }
    public ChatNotificationPrimePaidUpgrade? PrimePaidUpgrade { get; init; }
    public ChatNotificationPayItForward? PayItForward { get; init; }
    public ChatNotificationRaid? Raid { get; init; }
    /// <summary>An empty object when <see cref="NoticeType"/> is unraid; otherwise null.</summary>
    public ChatNotificationUnraid? Unraid { get; init; }
    public ChatNotificationAnnouncement? Announcement { get; init; }
    public ChatNotificationBitsBadgeTier? BitsBadgeTier { get; init; }
    public ChatNotificationCharityDonation? CharityDonation { get; init; }
    public ChatNotificationWatchStreak? WatchStreak { get; init; }
    public ChatNotificationModiversary? Modiversary { get; init; }
    public ChatNotificationGiftedDropsSummary? GiftedDropsSummary { get; init; }
    public ChatNotificationSub? SharedChatSub { get; init; }
    public ChatNotificationResub? SharedChatResub { get; init; }
    public ChatNotificationSubGift? SharedChatSubGift { get; init; }
    public ChatNotificationCommunitySubGift? SharedChatCommunitySubGift { get; init; }
    public ChatNotificationGiftPaidUpgrade? SharedChatGiftPaidUpgrade { get; init; }
    public ChatNotificationPrimePaidUpgrade? SharedChatPrimePaidUpgrade { get; init; }
    public ChatNotificationPayItForward? SharedChatPayItForward { get; init; }
    public ChatNotificationRaid? SharedChatRaid { get; init; }
    public ChatNotificationAnnouncement? SharedChatAnnouncement { get; init; }
    public ChatNotificationModiversary? SharedChatModiversary { get; init; }
    public ChatNotificationGiftedDropsSummary? SharedChatGiftedDropsSummary { get; init; }
    /// <summary>Appears only in Twitch's shared chat example payload; no matching notice type is documented.</summary>
    public ChatNotificationUnraid? SharedChatUnraid { get; init; }
    /// <summary>Appears only in Twitch's shared chat example payload; no matching notice type is documented.</summary>
    public ChatNotificationBitsBadgeTier? SharedChatBitsBadgeTier { get; init; }
    /// <summary>Appears only in Twitch's shared chat example payload; no matching notice type is documented.</summary>
    public ChatNotificationCharityDonation? SharedChatCharityDonation { get; init; }
    /// <summary>Shared chat: the channel the notice happened in; null when it happened in the broadcaster's own channel.</summary>
    public string? SourceBroadcasterUserId { get; init; }
    public string? SourceBroadcasterUserName { get; init; }
    public string? SourceBroadcasterUserLogin { get; init; }
    public string? SourceMessageId { get; init; }
    public IReadOnlyList<ChatMessageBadge>? SourceBadges { get; init; }
    /// <summary>Whether the notice is only sent to the source channel; null outside shared chat.</summary>
    public bool? IsSourceOnly { get; init; }
}

public sealed class ChatNotificationSub
{
    /// <summary>1000, 2000 or 3000 (Prime subscriptions use 1000).</summary>
    public required string SubTier { get; init; }
    public bool IsPrime { get; init; }
    public int DurationMonths { get; init; }
}

public sealed class ChatNotificationResub
{
    public int CumulativeMonths { get; init; }
    public int DurationMonths { get; init; }
    /// <summary>Null when the user does not share their streak.</summary>
    public int? StreakMonths { get; init; }
    /// <summary>1000, 2000 or 3000.</summary>
    public required string SubTier { get; init; }
    public bool? IsPrime { get; init; }
    public bool IsGift { get; init; }
    public bool? GifterIsAnonymous { get; init; }
    public string? GifterUserId { get; init; }
    public string? GifterUserName { get; init; }
    public string? GifterUserLogin { get; init; }
}

public sealed class ChatNotificationSubGift
{
    public int DurationMonths { get; init; }
    /// <summary>The gifter's total gifts in this channel; null when anonymous.</summary>
    public int? CumulativeTotal { get; init; }
    public required string RecipientUserId { get; init; }
    public required string RecipientUserName { get; init; }
    public required string RecipientUserLogin { get; init; }
    /// <summary>1000, 2000 or 3000.</summary>
    public required string SubTier { get; init; }
    /// <summary>The associated community gift; null when not part of one.</summary>
    public string? CommunityGiftId { get; init; }
}

public sealed class ChatNotificationCommunitySubGift
{
    public required string Id { get; init; }
    public int Total { get; init; }
    /// <summary>1000, 2000 or 3000.</summary>
    public required string SubTier { get; init; }
    /// <summary>The gifter's total gifts in this channel; null when anonymous.</summary>
    public int? CumulativeTotal { get; init; }
}

public sealed class ChatNotificationGiftPaidUpgrade
{
    public bool GifterIsAnonymous { get; init; }
    public string? GifterUserId { get; init; }
    public string? GifterUserName { get; init; }
    /// <summary>Not in the field table (other gifter objects carry it); null when Twitch omits it.</summary>
    public string? GifterUserLogin { get; init; }
}

public sealed class ChatNotificationPrimePaidUpgrade
{
    /// <summary>1000, 2000 or 3000.</summary>
    public required string SubTier { get; init; }
}

public sealed class ChatNotificationPayItForward
{
    public bool GifterIsAnonymous { get; init; }
    public string? GifterUserId { get; init; }
    public string? GifterUserName { get; init; }
    public string? GifterUserLogin { get; init; }
}

public sealed class ChatNotificationRaid
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string UserLogin { get; init; }
    public int ViewerCount { get; init; }
    public required string ProfileImageUrl { get; init; }
}

/// <summary>Twitch sends an empty object for unraid notices.</summary>
public sealed class ChatNotificationUnraid;

public sealed class ChatNotificationAnnouncement
{
    public required string Color { get; init; }
}

public sealed class ChatNotificationBitsBadgeTier
{
    /// <summary>The Bits badge tier earned, for example 100, 1000 or 10000.</summary>
    public int Tier { get; init; }
}

public sealed class ChatNotificationCharityDonation
{
    public required string CharityName { get; init; }
    public required ChatNotificationCharityAmount Amount { get; init; }
}

/// <summary>An amount in the currency's minor unit: value 550 with decimal_place 2 is 5.50.</summary>
public sealed class ChatNotificationCharityAmount
{
    public int Value { get; init; }
    public int DecimalPlace { get; init; }
    /// <summary>ISO 4217 currency code.</summary>
    public required string Currency { get; init; }
}

public sealed class ChatNotificationWatchStreak
{
    public int StreakCount { get; init; }
    public int ChannelPointsAwarded { get; init; }
}

public sealed class ChatNotificationModiversary
{
    public int Months { get; init; }
}

public sealed class ChatNotificationGiftedDropsSummary
{
    public int RecipientCount { get; init; }
}
