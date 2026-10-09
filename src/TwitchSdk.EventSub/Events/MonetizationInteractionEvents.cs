using System.Text.Json.Serialization;
using TwitchSdk.Core;

namespace TwitchSdk.EventSub.Events;

// Discriminators such as type, tier, status and color stay strings so new Twitch values do not break deserialization.
// Lists coalesce in their init accessor: the source generator passes null for absent init-only properties (for example
// top_predictors in channel.prediction.begin), which would otherwise override the empty-list initializer.

/// <summary>channel.bits.use v1: Bits were used for a cheer, a Power-up or a custom Power-up.</summary>
public sealed class ChannelBitsUseEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public long Bits { get; init; }
    /// <summary>cheer, power_up or custom_power_up.</summary>
    public required string Type { get; init; }
    /// <summary>The chat message with fragments; null when the Bits use carries no message.</summary>
    public ChannelBitsUseMessage? Message { get; init; }
    /// <summary>The built-in Power-up; null unless <see cref="Type"/> is power_up.</summary>
    public ChannelBitsUsePowerUp? PowerUp { get; init; }
    /// <summary>The custom Power-up; null unless <see cref="Type"/> is custom_power_up.</summary>
    public ChannelBitsUseCustomPowerUp? CustomPowerUp { get; init; }
}

public sealed class ChannelBitsUseMessage
{
    public required string Text { get; init; }
    public IReadOnlyList<ChannelBitsUseMessageFragment> Fragments { get => _fragments; init => _fragments = value ?? []; }
    private readonly IReadOnlyList<ChannelBitsUseMessageFragment> _fragments = [];
}

public sealed class ChannelBitsUseMessageFragment
{
    public required string Text { get; init; }
    /// <summary>text, cheermote or emote.</summary>
    public required string Type { get; init; }
    public ChannelBitsUseEmote? Emote { get; init; }
    public ChannelBitsUseCheermote? Cheermote { get; init; }
}

public sealed class ChannelBitsUseEmote
{
    public required string Id { get; init; }
    public required string EmoteSetId { get; init; }
    public required string OwnerId { get; init; }
    /// <summary>animated and/or static.</summary>
    public IReadOnlyList<string> Format { get => _format; init => _format = value ?? []; }
    private readonly IReadOnlyList<string> _format = [];
}

public sealed class ChannelBitsUseCheermote
{
    public required string Prefix { get; init; }
    public long Bits { get; init; }
    public int Tier { get; init; }
}

public sealed class ChannelBitsUsePowerUp
{
    /// <summary>message_effect, celebration or gigantify_an_emote.</summary>
    public required string Type { get; init; }
    public ChannelBitsUsePowerUpEmote? Emote { get; init; }
    public string? MessageEffectId { get; init; }
}

public sealed class ChannelBitsUsePowerUpEmote
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}

public sealed class ChannelBitsUseCustomPowerUp
{
    public required string Title { get; init; }
    public required string RewardId { get; init; }
}

/// <summary>channel.subscribe v1: a new subscription (resubscriptions are not included).</summary>
public sealed class ChannelSubscribeEvent
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    /// <summary>1000, 2000 or 3000.</summary>
    public required string Tier { get; init; }
    public bool IsGift { get; init; }
}

/// <summary>channel.subscription.end v1: a subscription expired.</summary>
public sealed class ChannelSubscriptionEndEvent
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    /// <summary>1000, 2000 or 3000.</summary>
    public required string Tier { get; init; }
    public bool IsGift { get; init; }
}

/// <summary>channel.subscription.gift v1: a user gifted one or more subscriptions.</summary>
public sealed class ChannelSubscriptionGiftEvent
{
    /// <summary>The gifter; null for anonymous gifts.</summary>
    public string? UserId { get; init; }
    public string? UserLogin { get; init; }
    public string? UserName { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public int Total { get; init; }
    public required string Tier { get; init; }
    /// <summary>Subscriptions this user has gifted in the channel; null for anonymous gifts or when the gifter does not share it.</summary>
    public int? CumulativeTotal { get; init; }
    public bool IsAnonymous { get; init; }
}

/// <summary>channel.subscription.message v1: a user sent a resubscription chat message.</summary>
public sealed class ChannelSubscriptionMessageEvent
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string Tier { get; init; }
    public required MessageWithEmoteRanges Message { get; init; }
    public int CumulativeMonths { get; init; }
    /// <summary>Null when the user does not share their streak.</summary>
    public int? StreakMonths { get; init; }
    public int DurationMonths { get; init; }
}

/// <summary>A message with emote positions, as sent by channel.subscription.message and automatic reward redemptions v1.</summary>
public sealed class MessageWithEmoteRanges
{
    public required string Text { get; init; }
    public IReadOnlyList<MessageEmoteRange> Emotes { get => _emotes; init => _emotes = value ?? []; }
    private readonly IReadOnlyList<MessageEmoteRange> _emotes = [];
}

/// <summary>An emote ID and the inclusive character positions where it appears in the text.</summary>
public sealed class MessageEmoteRange
{
    public required string Id { get; init; }
    public int Begin { get; init; }
    public int End { get; init; }
}

/// <summary>channel.cheer v1: a user cheered Bits.</summary>
public sealed class ChannelCheerEvent
{
    public bool IsAnonymous { get; init; }
    /// <summary>The cheering user; null when <see cref="IsAnonymous"/> is true.</summary>
    public string? UserId { get; init; }
    public string? UserLogin { get; init; }
    public string? UserName { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string Message { get; init; }
    public long Bits { get; init; }
}

/// <summary>channel.channel_points_automatic_reward_redemption.add v1.</summary>
public sealed class ChannelPointsAutomaticRewardRedemptionAddEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public required string Id { get; init; }
    public required ChannelPointsAutomaticReward Reward { get; init; }
    /// <summary>The user message; nullable because rewards such as emote unlocks may not carry one.</summary>
    public MessageWithEmoteRanges? Message { get; init; }
    /// <summary>The text the user entered, when the reward requires input.</summary>
    public string? UserInput { get; init; }
    public DateTimeOffset RedeemedAt { get; init; }
}

/// <summary>The automatic reward in a v1 redemption.</summary>
public sealed class ChannelPointsAutomaticReward
{
    /// <summary>For example send_highlighted_message, single_message_bypass_sub_mode, random_sub_emote_unlock, chosen_sub_emote_unlock, chosen_modified_sub_emote_unlock, message_effect, gigantify_an_emote or celebration.</summary>
    public required string Type { get; init; }
    public long Cost { get; init; }
    public ChannelPointsRewardEmote? UnlockedEmote { get; init; }
}

/// <summary>channel.channel_points_automatic_reward_redemption.add v2: the reward reports its Channel Points and the message uses fragments.</summary>
public sealed class ChannelPointsAutomaticRewardRedemptionAddEventV2
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public required string Id { get; init; }
    public required ChannelPointsAutomaticRewardV2 Reward { get; init; }
    public ChannelPointsAutomaticRewardMessageV2? Message { get; init; }
    public DateTimeOffset RedeemedAt { get; init; }
}

/// <summary>The automatic reward in a v2 redemption.</summary>
public sealed class ChannelPointsAutomaticRewardV2
{
    /// <summary>For example send_highlighted_message, single_message_bypass_sub_mode, random_sub_emote_unlock, chosen_sub_emote_unlock or chosen_modified_sub_emote_unlock.</summary>
    public required string Type { get; init; }
    public long ChannelPoints { get; init; }
    public ChannelPointsRewardEmote? Emote { get; init; }
}

public sealed class ChannelPointsRewardEmote
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}

public sealed class ChannelPointsAutomaticRewardMessageV2
{
    public required string Text { get; init; }
    public IReadOnlyList<ChannelPointsAutomaticRewardMessageFragment> Fragments { get => _fragments; init => _fragments = value ?? []; }
    private readonly IReadOnlyList<ChannelPointsAutomaticRewardMessageFragment> _fragments = [];
}

public sealed class ChannelPointsAutomaticRewardMessageFragment
{
    public required string Text { get; init; }
    /// <summary>text or emote.</summary>
    public required string Type { get; init; }
    public ChannelPointsFragmentEmote? Emote { get; init; }
}

public sealed class ChannelPointsFragmentEmote
{
    public required string Id { get; init; }
}

/// <summary>Shared payload of the channel.channel_points_custom_reward add, update and remove events.</summary>
public abstract class ChannelPointsCustomRewardEventBase
{
    public required string Id { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsPaused { get; init; }
    public bool IsInStock { get; init; }
    public required string Title { get; init; }
    public long Cost { get; init; }
    public required string Prompt { get; init; }
    public bool IsUserInputRequired { get; init; }
    public bool ShouldRedemptionsSkipRequestQueue { get; init; }
    public required ChannelPointsRewardLimit MaxPerStream { get; init; }
    public required ChannelPointsRewardLimit MaxPerUserPerStream { get; init; }
    /// <summary>Hex color with a # prefix, for example #FA1ED2.</summary>
    public required string BackgroundColor { get; init; }
    /// <summary>Custom images; null when none were uploaded.</summary>
    public ChannelPointsRewardImage? Image { get; init; }
    public required ChannelPointsRewardImage DefaultImage { get; init; }
    public required ChannelPointsRewardCooldown GlobalCooldown { get; init; }
    /// <summary>Null when the reward is not on cooldown.</summary>
    [JsonConverter(typeof(EmptyStringAsNullDateTimeOffsetConverter))]
    public DateTimeOffset? CooldownExpiresAt { get; init; }
    /// <summary>Null when the stream is offline or max_per_stream is disabled.</summary>
    public long? RedemptionsRedeemedCurrentStream { get; init; }
}

/// <summary>channel.channel_points_custom_reward.add v1.</summary>
public sealed class ChannelPointsCustomRewardAddEvent : ChannelPointsCustomRewardEventBase;

/// <summary>channel.channel_points_custom_reward.update v1.</summary>
public sealed class ChannelPointsCustomRewardUpdateEvent : ChannelPointsCustomRewardEventBase;

/// <summary>channel.channel_points_custom_reward.remove v1.</summary>
public sealed class ChannelPointsCustomRewardRemoveEvent : ChannelPointsCustomRewardEventBase;

/// <summary>max_per_stream or max_per_user_per_stream.</summary>
public sealed class ChannelPointsRewardLimit
{
    public bool IsEnabled { get; init; }
    public long Value { get; init; }
}

public sealed class ChannelPointsRewardCooldown
{
    public bool IsEnabled { get; init; }
    public long Seconds { get; init; }
}

public sealed class ChannelPointsRewardImage
{
    [JsonPropertyName("url_1x")]
    public required string Url1x { get; init; }
    [JsonPropertyName("url_2x")]
    public required string Url2x { get; init; }
    [JsonPropertyName("url_4x")]
    public required string Url4x { get; init; }
}

/// <summary>Shared payload of the channel.channel_points_custom_reward_redemption add and update events.</summary>
public abstract class ChannelPointsCustomRewardRedemptionEventBase
{
    public required string Id { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    /// <summary>Empty when the user provided no input.</summary>
    public required string UserInput { get; init; }
    /// <summary>unknown, unfulfilled, fulfilled or canceled.</summary>
    public required string Status { get; init; }
    /// <summary>The reward as it was when redeemed.</summary>
    public required ChannelPointsRewardInfo Reward { get; init; }
    public DateTimeOffset RedeemedAt { get; init; }
}

/// <summary>channel.channel_points_custom_reward_redemption.add v1.</summary>
public sealed class ChannelPointsCustomRewardRedemptionAddEvent : ChannelPointsCustomRewardRedemptionEventBase;

/// <summary>channel.channel_points_custom_reward_redemption.update v1; the status is fulfilled or canceled.</summary>
public sealed class ChannelPointsCustomRewardRedemptionUpdateEvent : ChannelPointsCustomRewardRedemptionEventBase;

public sealed class ChannelPointsRewardInfo
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public long Cost { get; init; }
    public required string Prompt { get; init; }
}

/// <summary>channel.custom_power_up_redemption.add v1: a viewer redeemed a custom Power-up.</summary>
public sealed class ChannelCustomPowerUpRedemptionAddEvent
{
    public required string Id { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    /// <summary>Empty when the user provided no input.</summary>
    public required string UserInput { get; init; }
    /// <summary>unknown, unfulfilled, fulfilled or canceled.</summary>
    public required string Status { get; init; }
    /// <summary>The custom Power-up as it was when redeemed.</summary>
    public required ChannelCustomPowerUpInfo CustomPowerUp { get; init; }
    public DateTimeOffset RedeemedAt { get; init; }
}

public sealed class ChannelCustomPowerUpInfo
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public long Bits { get; init; }
    public required string Prompt { get; init; }
}

/// <summary>Shared payload of the channel.poll begin, progress and end events.</summary>
public abstract class ChannelPollEventBase
{
    public required string Id { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string Title { get; init; }
    /// <summary>Vote counts are zero in channel.poll.begin, which does not send them.</summary>
    public IReadOnlyList<ChannelPollChoice> Choices { get => _choices; init => _choices = value ?? []; }
    private readonly IReadOnlyList<ChannelPollChoice> _choices = [];
    /// <summary>Bits voting is no longer supported; Twitch sends false and 0.</summary>
    public required ChannelPollVoting BitsVoting { get; init; }
    public required ChannelPollVoting ChannelPointsVoting { get; init; }
    public DateTimeOffset StartedAt { get; init; }
}

/// <summary>channel.poll.begin v1.</summary>
public sealed class ChannelPollBeginEvent : ChannelPollEventBase
{
    public DateTimeOffset EndsAt { get; init; }
}

/// <summary>channel.poll.progress v1.</summary>
public sealed class ChannelPollProgressEvent : ChannelPollEventBase
{
    public DateTimeOffset EndsAt { get; init; }
}

/// <summary>channel.poll.end v1.</summary>
public sealed class ChannelPollEndEvent : ChannelPollEventBase
{
    /// <summary>completed, archived or terminated.</summary>
    public required string Status { get; init; }
    public DateTimeOffset EndedAt { get; init; }
}

public sealed class ChannelPollChoice
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    /// <summary>Not used; always 0.</summary>
    public long BitsVotes { get; init; }
    public long ChannelPointsVotes { get; init; }
    public long Votes { get; init; }
}

/// <summary>bits_voting or channel_points_voting.</summary>
public sealed class ChannelPollVoting
{
    public bool IsEnabled { get; init; }
    public long AmountPerVote { get; init; }
}

/// <summary>Shared payload of the channel.prediction begin, progress, lock and end events.</summary>
public abstract class ChannelPredictionEventBase
{
    public required string Id { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string Title { get; init; }
    /// <summary>channel.prediction.begin sends only id, title and color for each outcome.</summary>
    public IReadOnlyList<ChannelPredictionOutcome> Outcomes { get => _outcomes; init => _outcomes = value ?? []; }
    private readonly IReadOnlyList<ChannelPredictionOutcome> _outcomes = [];
    public DateTimeOffset StartedAt { get; init; }
}

/// <summary>channel.prediction.begin v1.</summary>
public sealed class ChannelPredictionBeginEvent : ChannelPredictionEventBase
{
    public DateTimeOffset LocksAt { get; init; }
}

/// <summary>channel.prediction.progress v1.</summary>
public sealed class ChannelPredictionProgressEvent : ChannelPredictionEventBase
{
    public DateTimeOffset LocksAt { get; init; }
}

/// <summary>channel.prediction.lock v1.</summary>
public sealed class ChannelPredictionLockEvent : ChannelPredictionEventBase
{
    public DateTimeOffset LockedAt { get; init; }
}

/// <summary>channel.prediction.end v1.</summary>
public sealed class ChannelPredictionEndEvent : ChannelPredictionEventBase
{
    /// <summary>Null when the prediction was canceled.</summary>
    public string? WinningOutcomeId { get; init; }
    /// <summary>resolved or canceled.</summary>
    public required string Status { get; init; }
    public DateTimeOffset EndedAt { get; init; }
}

public sealed class ChannelPredictionOutcome
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    /// <summary>blue or pink.</summary>
    public required string Color { get; init; }
    public long Users { get; init; }
    public long ChannelPoints { get; init; }
    /// <summary>Up to 10 users who used the most Channel Points on this outcome.</summary>
    public IReadOnlyList<ChannelPredictionTopPredictor> TopPredictors { get => _topPredictors; init => _topPredictors = value ?? []; }
    private readonly IReadOnlyList<ChannelPredictionTopPredictor> _topPredictors = [];
}

public sealed class ChannelPredictionTopPredictor
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    /// <summary>Always null in progress and lock events. For losing outcomes and refunds Twitch documents 0, while its end example sends null.</summary>
    public long? ChannelPointsWon { get; init; }
    public long ChannelPointsUsed { get; init; }
}
