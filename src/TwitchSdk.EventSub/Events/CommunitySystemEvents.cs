using System.Text.Json.Serialization;
using TwitchSdk.Core;

namespace TwitchSdk.EventSub.Events;

/// <summary>A charity amount in the currency's minor unit; the major-unit amount is <c>Value / 10^DecimalPlaces</c>.</summary>
public sealed class CharityCampaignAmount
{
    /// <summary>Amount in minor units (for example cents). Use <see cref="DecimalPlaces"/> to convert without floating-point rounding.</summary>
    public long Value { get; init; }
    public int DecimalPlaces { get; init; }
    /// <summary>ISO-4217 three-letter currency code.</summary>
    public required string Currency { get; init; }
}

/// <summary>channel.charity_campaign.donate v1.</summary>
public sealed class ChannelCharityCampaignDonateEvent
{
    /// <summary>Donation ID, unique across campaigns.</summary>
    public required string Id { get; init; }
    public required string CampaignId { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public required string CharityName { get; init; }
    public required string CharityDescription { get; init; }
    /// <summary>URL of the charity's 100x100 PNG logo.</summary>
    public required string CharityLogo { get; init; }
    public required string CharityWebsite { get; init; }
    public required CharityCampaignAmount Amount { get; init; }
}

/// <summary>channel.charity_campaign.start v1. May arrive after a progress event.</summary>
public sealed class ChannelCharityCampaignStartEvent
{
    /// <summary>Campaign ID.</summary>
    public required string Id { get; init; }
    public required string BroadcasterId { get; init; }
    public required string BroadcasterLogin { get; init; }
    public required string BroadcasterName { get; init; }
    public required string CharityName { get; init; }
    public required string CharityDescription { get; init; }
    public required string CharityLogo { get; init; }
    public required string CharityWebsite { get; init; }
    public required CharityCampaignAmount CurrentAmount { get; init; }
    public required CharityCampaignAmount TargetAmount { get; init; }
    public DateTimeOffset StartedAt { get; init; }
}

/// <summary>channel.charity_campaign.progress v1. Sent for donations and target changes; may arrive before the start event.</summary>
public sealed class ChannelCharityCampaignProgressEvent
{
    /// <summary>Campaign ID.</summary>
    public required string Id { get; init; }
    public required string BroadcasterId { get; init; }
    public required string BroadcasterLogin { get; init; }
    public required string BroadcasterName { get; init; }
    public required string CharityName { get; init; }
    public required string CharityDescription { get; init; }
    public required string CharityLogo { get; init; }
    public required string CharityWebsite { get; init; }
    public required CharityCampaignAmount CurrentAmount { get; init; }
    public required CharityCampaignAmount TargetAmount { get; init; }
}

/// <summary>channel.charity_campaign.stop v1.</summary>
public sealed class ChannelCharityCampaignStopEvent
{
    /// <summary>Campaign ID.</summary>
    public required string Id { get; init; }
    public required string BroadcasterId { get; init; }
    public required string BroadcasterLogin { get; init; }
    public required string BroadcasterName { get; init; }
    public required string CharityName { get; init; }
    public required string CharityDescription { get; init; }
    public required string CharityLogo { get; init; }
    public required string CharityWebsite { get; init; }
    /// <summary>Final amount raised.</summary>
    public required CharityCampaignAmount CurrentAmount { get; init; }
    public required CharityCampaignAmount TargetAmount { get; init; }
    public DateTimeOffset StoppedAt { get; init; }
}

/// <summary>channel.goal.begin v1. May arrive after progress events.</summary>
public sealed class ChannelGoalBeginEvent
{
    public required string Id { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    /// <summary>follow, subscription, subscription_count, new_subscription, new_subscription_count, new_bit or new_cheerer; kept as a string to tolerate new values.</summary>
    public required string Type { get; init; }
    /// <summary>Goal description (at most 40 characters); empty when none was specified.</summary>
    public required string Description { get; init; }
    public long CurrentAmount { get; init; }
    public long TargetAmount { get; init; }
    public DateTimeOffset StartedAt { get; init; }
}

/// <summary>channel.goal.progress v1. Progress can be negative, for example when users unfollow.</summary>
public sealed class ChannelGoalProgressEvent
{
    public required string Id { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    /// <summary>follow, subscription, subscription_count, new_subscription, new_subscription_count, new_bit or new_cheerer; kept as a string to tolerate new values.</summary>
    public required string Type { get; init; }
    public required string Description { get; init; }
    public long CurrentAmount { get; init; }
    public long TargetAmount { get; init; }
    public DateTimeOffset StartedAt { get; init; }
}

/// <summary>channel.goal.end v1. Only this goal event carries <see cref="IsAchieved"/> and <see cref="EndedAt"/>.</summary>
public sealed class ChannelGoalEndEvent
{
    public required string Id { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    /// <summary>follow, subscription, subscription_count, new_subscription, new_subscription_count, new_bit or new_cheerer; kept as a string to tolerate new values.</summary>
    public required string Type { get; init; }
    public required string Description { get; init; }
    public bool IsAchieved { get; init; }
    public long CurrentAmount { get; init; }
    public long TargetAmount { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }
}

/// <summary>A top Hype Train contributor in a channel.hype_train.* v2 event.</summary>
public sealed class HypeTrainEventContribution
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    /// <summary>bits, subscription or other; kept as a string to tolerate new values.</summary>
    public required string Type { get; init; }
    /// <summary>Bits used, or 500/1000/2500 for tier 1/2/3 subscriptions.</summary>
    public long Total { get; init; }
}

/// <summary>A broadcaster participating in a shared Hype Train.</summary>
public sealed class HypeTrainEventParticipant
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
}

/// <summary>channel.hype_train.begin v2.</summary>
public sealed class ChannelHypeTrainBeginEvent
{
    public required string Id { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public long Total { get; init; }
    /// <summary>Points contributed at the current level.</summary>
    public long Progress { get; init; }
    /// <summary>Points required to reach the next level.</summary>
    public long Goal { get; init; }
    public IReadOnlyList<HypeTrainEventContribution> TopContributions { get; init; } = [];
    public int Level { get; init; }
    /// <summary>All-time high level for this type of Hype Train on this channel.</summary>
    public int AllTimeHighLevel { get; init; }
    /// <summary>All-time high total for this type of Hype Train on this channel.</summary>
    public long AllTimeHighTotal { get; init; }
    /// <summary>Broadcasters in a shared Hype Train; null when the train is not shared.</summary>
    public IReadOnlyList<HypeTrainEventParticipant>? SharedTrainParticipants { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    /// <summary>treasure, golden_kappa or regular; kept as a string to tolerate new values.</summary>
    public required string Type { get; init; }
    public bool IsSharedTrain { get; init; }
}

/// <summary>channel.hype_train.progress v2. May arrive before the begin event.</summary>
public sealed class ChannelHypeTrainProgressEvent
{
    public required string Id { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public long Total { get; init; }
    /// <summary>Points contributed at the current level.</summary>
    public long Progress { get; init; }
    /// <summary>Points required to reach the next level.</summary>
    public long Goal { get; init; }
    public IReadOnlyList<HypeTrainEventContribution> TopContributions { get; init; } = [];
    public int Level { get; init; }
    /// <summary>Broadcasters in a shared Hype Train; null when the train is not shared.</summary>
    public IReadOnlyList<HypeTrainEventParticipant>? SharedTrainParticipants { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    /// <summary>treasure, golden_kappa or regular; kept as a string to tolerate new values.</summary>
    public required string Type { get; init; }
    public bool IsSharedTrain { get; init; }
}

/// <summary>channel.hype_train.end v2.</summary>
public sealed class ChannelHypeTrainEndEvent
{
    public required string Id { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public long Total { get; init; }
    public IReadOnlyList<HypeTrainEventContribution> TopContributions { get; init; } = [];
    public int Level { get; init; }
    /// <summary>Broadcasters in a shared Hype Train; null when the train is not shared.</summary>
    public IReadOnlyList<HypeTrainEventParticipant>? SharedTrainParticipants { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }
    /// <summary>When the next Hype Train can start.</summary>
    public DateTimeOffset CooldownEndsAt { get; init; }
    /// <summary>treasure, golden_kappa or regular; kept as a string to tolerate new values.</summary>
    public required string Type { get; init; }
    public bool IsSharedTrain { get; init; }
}

/// <summary>user.authorization.grant v1.</summary>
public sealed class UserAuthorizationGrantEvent
{
    public required string ClientId { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
}

/// <summary>user.authorization.revoke v1. Use it to honor data-deletion requirements such as GDPR, LGPD or CCPA.</summary>
public sealed class UserAuthorizationRevokeEvent
{
    public required string ClientId { get; init; }
    public required string UserId { get; init; }
    /// <summary>Null when the user no longer exists.</summary>
    public string? UserLogin { get; init; }
    /// <summary>Null when the user no longer exists.</summary>
    public string? UserName { get; init; }
}

/// <summary>user.update v1.</summary>
public sealed class UserUpdateEvent
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    /// <summary>Only populated when the user granted user:read:email to your client; otherwise empty (or absent).</summary>
    public string? Email { get; init; }
    /// <summary>Whether Twitch verified the email address. Ignore it when <see cref="Email"/> is empty.</summary>
    public bool EmailVerified { get; init; }
    public required string Description { get; init; }
}

/// <summary>The whisper body of a user.whisper.message event.</summary>
public sealed class UserWhisperMessageContent
{
    public required string Text { get; init; }
}

/// <summary>user.whisper.message v1.</summary>
public sealed class UserWhisperMessageEvent
{
    public required string FromUserId { get; init; }
    public required string FromUserName { get; init; }
    public required string FromUserLogin { get; init; }
    public required string ToUserId { get; init; }
    public required string ToUserName { get; init; }
    public required string ToUserLogin { get; init; }
    public required string WhisperId { get; init; }
    public required UserWhisperMessageContent Whisper { get; init; }
}

/// <summary>The disabled transport of a conduit.shard.disabled event.</summary>
public sealed class ConduitShardDisabledTransport
{
    /// <summary>websocket or webhook.</summary>
    public required string Method { get; init; }
    /// <summary>Webhook callback URL; null for WebSocket shards.</summary>
    public string? Callback { get; init; }
    /// <summary>WebSocket session ID; null for webhook shards.</summary>
    public string? SessionId { get; init; }
    /// <summary>When the WebSocket session connected; null for webhook shards.</summary>
    [JsonConverter(typeof(EmptyStringAsNullDateTimeOffsetConverter))]
    public DateTimeOffset? ConnectedAt { get; init; }
    /// <summary>When the WebSocket session disconnected; null for webhook shards.</summary>
    [JsonConverter(typeof(EmptyStringAsNullDateTimeOffsetConverter))]
    public DateTimeOffset? DisconnectedAt { get; init; }
}

/// <summary>conduit.shard.disabled v1.</summary>
public sealed class ConduitShardDisabledEvent
{
    public required string ConduitId { get; init; }
    public required string ShardId { get; init; }
    /// <summary>The transport's new status, for example websocket_disconnected; kept as a string to tolerate new values.</summary>
    public required string Status { get; init; }
    public required ConduitShardDisabledTransport Transport { get; init; }
}

/// <summary>One entitlement in a batched drop.entitlement.grant v1 notification.</summary>
public sealed class DropEntitlementGrantEvent
{
    /// <summary>Individual event ID assigned by EventSub; use it to de-duplicate events.</summary>
    public required string Id { get; init; }
    public required DropEntitlementGrantData Data { get; init; }
}

/// <summary>The entitlement granted by a Drop.</summary>
public sealed class DropEntitlementGrantData
{
    public required string OrganizationId { get; init; }
    /// <summary>Category (game) being played when the benefit was entitled.</summary>
    public required string CategoryId { get; init; }
    public required string CategoryName { get; init; }
    public required string CampaignId { get; init; }
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string UserLogin { get; init; }
    /// <summary>Unique entitlement ID; use it to de-duplicate entitlements.</summary>
    public required string EntitlementId { get; init; }
    public required string BenefitId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>The product of an Extension Bits transaction.</summary>
public sealed class ExtensionBitsTransactionProduct
{
    public required string Name { get; init; }
    /// <summary>Bits involved in the transaction; 0 when <see cref="InDevelopment"/> is true.</summary>
    public int Bits { get; init; }
    public required string Sku { get; init; }
    public bool InDevelopment { get; init; }
}

/// <summary>extension.bits_transaction.create v1.</summary>
public sealed class ExtensionBitsTransactionCreateEvent
{
    /// <summary>Transaction ID.</summary>
    public required string Id { get; init; }
    public required string ExtensionClientId { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public required ExtensionBitsTransactionProduct Product { get; init; }
}

/// <summary>channel.guest_star_session.begin beta.</summary>
public sealed class ChannelGuestStarSessionBeginEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    /// <summary>Shown in Twitch's notification example but not in the field table; null when absent.</summary>
    public string? ModeratorUserId { get; init; }
    public string? ModeratorUserName { get; init; }
    public string? ModeratorUserLogin { get; init; }
    public required string SessionId { get; init; }
    public DateTimeOffset StartedAt { get; init; }
}

/// <summary>channel.guest_star_session.end beta.</summary>
public sealed class ChannelGuestStarSessionEndEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    /// <summary>Shown in Twitch's notification example but not in the field table; null when absent.</summary>
    public string? ModeratorUserId { get; init; }
    public string? ModeratorUserName { get; init; }
    public string? ModeratorUserLogin { get; init; }
    public required string SessionId { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }
    /// <summary>Host channel; documented in the field table but missing from Twitch's example, so null when absent.</summary>
    public string? HostUserId { get; init; }
    public string? HostUserName { get; init; }
    public string? HostUserLogin { get; init; }
}

/// <summary>channel.guest_star_guest.update beta.</summary>
public sealed class ChannelGuestStarGuestUpdateEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string SessionId { get; init; }
    /// <summary>Moderator (possibly the host) who updated the guest; null when the guest made the update.</summary>
    public string? ModeratorUserId { get; init; }
    public string? ModeratorUserName { get; init; }
    public string? ModeratorUserLogin { get; init; }
    /// <summary>Null when the slot is now empty.</summary>
    public string? GuestUserId { get; init; }
    public string? GuestUserName { get; init; }
    public string? GuestUserLogin { get; init; }
    /// <summary>Null while the guest is invited, removed, ready or accepted.</summary>
    public string? SlotId { get; init; }
    /// <summary>invited, accepted, ready, backstage, live or removed; null when the slot is now empty.</summary>
    public string? State { get; init; }
    /// <summary>Host channel; documented in the field table but missing from Twitch's example, so null when absent.</summary>
    public string? HostUserId { get; init; }
    public string? HostUserName { get; init; }
    public string? HostUserLogin { get; init; }
    /// <summary>Null when the guest is not slotted.</summary>
    public bool? HostVideoEnabled { get; init; }
    /// <summary>Null when the guest is not slotted.</summary>
    public bool? HostAudioEnabled { get; init; }
    /// <summary>Slot audio level from 0 to 100; null when the guest is not slotted.</summary>
    public int? HostVolume { get; init; }
}

/// <summary>channel.guest_star_settings.update beta.</summary>
public sealed class ChannelGuestStarSettingsUpdateEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public bool IsModeratorSendLiveEnabled { get; init; }
    public int SlotCount { get; init; }
    public bool IsBrowserSourceAudioEnabled { get; init; }
    /// <summary>tiled, screenshare, horizontal_top, horizontal_bottom, vertical_left or vertical_right; kept as a string to tolerate new values.</summary>
    public required string GroupLayout { get; init; }
}
