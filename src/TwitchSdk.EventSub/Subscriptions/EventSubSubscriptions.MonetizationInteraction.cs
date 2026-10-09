using TwitchSdk.Core;
using static TwitchSdk.EventSub.EventSubCondition;

namespace TwitchSdk.EventSub;

// Bits, subscriptions, Channel Points, custom Power-ups, polls and predictions. Every type is authorized by the
// broadcaster in the condition and accepts all transports.
public static partial class EventSubSubscriptions
{
    /// <summary>channel.bits.use v1. Authorization: bits:read.</summary>
    public static EventSubSubscriptionSpec ChannelBitsUseV1(string broadcasterUserId)
        => MonetizationInteractionSpecs.Broadcaster("channel.bits.use", "1", broadcasterUserId, required: [TwitchScopes.BitsRead]);

    /// <summary>channel.subscribe v1. Authorization: channel:read:subscriptions.</summary>
    public static EventSubSubscriptionSpec ChannelSubscribeV1(string broadcasterUserId)
        => MonetizationInteractionSpecs.Broadcaster("channel.subscribe", "1", broadcasterUserId, required: [TwitchScopes.ChannelReadSubscriptions]);

    /// <summary>channel.subscription.end v1. Authorization: channel:read:subscriptions.</summary>
    public static EventSubSubscriptionSpec ChannelSubscriptionEndV1(string broadcasterUserId)
        => MonetizationInteractionSpecs.Broadcaster("channel.subscription.end", "1", broadcasterUserId, required: [TwitchScopes.ChannelReadSubscriptions]);

    /// <summary>channel.subscription.gift v1. Authorization: channel:read:subscriptions.</summary>
    public static EventSubSubscriptionSpec ChannelSubscriptionGiftV1(string broadcasterUserId)
        => MonetizationInteractionSpecs.Broadcaster("channel.subscription.gift", "1", broadcasterUserId, required: [TwitchScopes.ChannelReadSubscriptions]);

    /// <summary>channel.subscription.message v1. Authorization: channel:read:subscriptions.</summary>
    public static EventSubSubscriptionSpec ChannelSubscriptionMessageV1(string broadcasterUserId)
        => MonetizationInteractionSpecs.Broadcaster("channel.subscription.message", "1", broadcasterUserId, required: [TwitchScopes.ChannelReadSubscriptions]);

    /// <summary>channel.cheer v1. Authorization: bits:read.</summary>
    public static EventSubSubscriptionSpec ChannelCheerV1(string broadcasterUserId)
        => MonetizationInteractionSpecs.Broadcaster("channel.cheer", "1", broadcasterUserId, required: [TwitchScopes.BitsRead]);

    /// <summary>channel.channel_points_automatic_reward_redemption.add v1. Authorization: channel:read:redemptions or channel:manage:redemptions.</summary>
    public static EventSubSubscriptionSpec ChannelPointsAutomaticRewardRedemptionAddV1(string broadcasterUserId)
        => MonetizationInteractionSpecs.Broadcaster("channel.channel_points_automatic_reward_redemption.add", "1", broadcasterUserId, anyOf: MonetizationInteractionSpecs.Redemptions());

    /// <summary>channel.channel_points_automatic_reward_redemption.add v2. Authorization: channel:read:redemptions or channel:manage:redemptions.</summary>
    public static EventSubSubscriptionSpec ChannelPointsAutomaticRewardRedemptionAddV2(string broadcasterUserId)
        => MonetizationInteractionSpecs.Broadcaster("channel.channel_points_automatic_reward_redemption.add", "2", broadcasterUserId, anyOf: MonetizationInteractionSpecs.Redemptions());

    /// <summary>channel.channel_points_custom_reward.add v1. Authorization: channel:read:redemptions or channel:manage:redemptions.</summary>
    public static EventSubSubscriptionSpec ChannelPointsCustomRewardAddV1(string broadcasterUserId)
        => MonetizationInteractionSpecs.Broadcaster("channel.channel_points_custom_reward.add", "1", broadcasterUserId, anyOf: MonetizationInteractionSpecs.Redemptions());

    /// <summary>channel.channel_points_custom_reward.update v1. Authorization: channel:read:redemptions or channel:manage:redemptions.</summary>
    /// <param name="broadcasterUserId">The broadcaster whose channel is monitored; their token authorizes WebSocket subscriptions.</param>
    /// <param name="rewardId">Optional reward ID that limits notifications to one reward.</param>
    public static EventSubSubscriptionSpec ChannelPointsCustomRewardUpdateV1(string broadcasterUserId, string? rewardId = null)
        => MonetizationInteractionSpecs.Broadcaster("channel.channel_points_custom_reward.update", "1", broadcasterUserId, rewardId, anyOf: MonetizationInteractionSpecs.Redemptions());

    /// <summary>channel.channel_points_custom_reward.remove v1. Authorization: channel:read:redemptions or channel:manage:redemptions.</summary>
    /// <param name="broadcasterUserId">The broadcaster whose channel is monitored; their token authorizes WebSocket subscriptions.</param>
    /// <param name="rewardId">Optional reward ID that limits notifications to one reward.</param>
    public static EventSubSubscriptionSpec ChannelPointsCustomRewardRemoveV1(string broadcasterUserId, string? rewardId = null)
        => MonetizationInteractionSpecs.Broadcaster("channel.channel_points_custom_reward.remove", "1", broadcasterUserId, rewardId, anyOf: MonetizationInteractionSpecs.Redemptions());

    /// <summary>channel.channel_points_custom_reward_redemption.add v1. Authorization: channel:read:redemptions or channel:manage:redemptions.</summary>
    /// <param name="broadcasterUserId">The broadcaster whose channel is monitored; their token authorizes WebSocket subscriptions.</param>
    /// <param name="rewardId">Optional reward ID that limits notifications to one reward.</param>
    public static EventSubSubscriptionSpec ChannelPointsCustomRewardRedemptionAddV1(string broadcasterUserId, string? rewardId = null)
        => MonetizationInteractionSpecs.Broadcaster("channel.channel_points_custom_reward_redemption.add", "1", broadcasterUserId, rewardId, anyOf: MonetizationInteractionSpecs.Redemptions());

    /// <summary>channel.channel_points_custom_reward_redemption.update v1. Authorization: channel:read:redemptions or channel:manage:redemptions.</summary>
    /// <param name="broadcasterUserId">The broadcaster whose channel is monitored; their token authorizes WebSocket subscriptions.</param>
    /// <param name="rewardId">Optional reward ID that limits notifications to one reward.</param>
    public static EventSubSubscriptionSpec ChannelPointsCustomRewardRedemptionUpdateV1(string broadcasterUserId, string? rewardId = null)
        => MonetizationInteractionSpecs.Broadcaster("channel.channel_points_custom_reward_redemption.update", "1", broadcasterUserId, rewardId, anyOf: MonetizationInteractionSpecs.Redemptions());

    /// <summary>channel.custom_power_up_redemption.add v1. Authorization: bits:read.</summary>
    /// <param name="broadcasterUserId">The broadcaster whose channel is monitored; their token authorizes WebSocket subscriptions.</param>
    /// <param name="rewardId">Optional custom Power-up ID that limits notifications to one Power-up.</param>
    public static EventSubSubscriptionSpec ChannelCustomPowerUpRedemptionAddV1(string broadcasterUserId, string? rewardId = null)
        => MonetizationInteractionSpecs.Broadcaster("channel.custom_power_up_redemption.add", "1", broadcasterUserId, rewardId, required: [TwitchScopes.BitsRead]);

    /// <summary>channel.poll.begin v1. Authorization: channel:read:polls or channel:manage:polls.</summary>
    public static EventSubSubscriptionSpec ChannelPollBeginV1(string broadcasterUserId)
        => MonetizationInteractionSpecs.Broadcaster("channel.poll.begin", "1", broadcasterUserId, anyOf: MonetizationInteractionSpecs.Polls());

    /// <summary>channel.poll.progress v1. Authorization: channel:read:polls or channel:manage:polls.</summary>
    public static EventSubSubscriptionSpec ChannelPollProgressV1(string broadcasterUserId)
        => MonetizationInteractionSpecs.Broadcaster("channel.poll.progress", "1", broadcasterUserId, anyOf: MonetizationInteractionSpecs.Polls());

    /// <summary>channel.poll.end v1. Authorization: channel:read:polls or channel:manage:polls.</summary>
    public static EventSubSubscriptionSpec ChannelPollEndV1(string broadcasterUserId)
        => MonetizationInteractionSpecs.Broadcaster("channel.poll.end", "1", broadcasterUserId, anyOf: MonetizationInteractionSpecs.Polls());

    /// <summary>channel.prediction.begin v1. Authorization: channel:read:predictions or channel:manage:predictions.</summary>
    public static EventSubSubscriptionSpec ChannelPredictionBeginV1(string broadcasterUserId)
        => MonetizationInteractionSpecs.Broadcaster("channel.prediction.begin", "1", broadcasterUserId, anyOf: MonetizationInteractionSpecs.Predictions());

    /// <summary>channel.prediction.progress v1. Authorization: channel:read:predictions or channel:manage:predictions.</summary>
    public static EventSubSubscriptionSpec ChannelPredictionProgressV1(string broadcasterUserId)
        => MonetizationInteractionSpecs.Broadcaster("channel.prediction.progress", "1", broadcasterUserId, anyOf: MonetizationInteractionSpecs.Predictions());

    /// <summary>channel.prediction.lock v1. Authorization: channel:read:predictions or channel:manage:predictions.</summary>
    public static EventSubSubscriptionSpec ChannelPredictionLockV1(string broadcasterUserId)
        => MonetizationInteractionSpecs.Broadcaster("channel.prediction.lock", "1", broadcasterUserId, anyOf: MonetizationInteractionSpecs.Predictions());

    /// <summary>channel.prediction.end v1. Authorization: channel:read:predictions or channel:manage:predictions.</summary>
    public static EventSubSubscriptionSpec ChannelPredictionEndV1(string broadcasterUserId)
        => MonetizationInteractionSpecs.Broadcaster("channel.prediction.end", "1", broadcasterUserId, anyOf: MonetizationInteractionSpecs.Predictions());

    // Nested so the helper names cannot collide with other groups' parts of this partial class.
    private static class MonetizationInteractionSpecs
    {
        // Fresh arrays per spec: the lists are exposed as IReadOnlyList and must not be shared mutable state.
        public static string[] Redemptions() => [TwitchScopes.ChannelReadRedemptions, TwitchScopes.ChannelManageRedemptions];
        public static string[] Polls() => [TwitchScopes.ChannelReadPolls, TwitchScopes.ChannelManagePolls];
        public static string[] Predictions() => [TwitchScopes.ChannelReadPredictions, TwitchScopes.ChannelManagePredictions];

        /// <summary>Builds a spec whose condition is broadcaster_user_id plus an optional reward_id, authorized by that broadcaster.</summary>
        public static EventSubSubscriptionSpec Broadcaster(string type, string version, string broadcasterUserId, string? rewardId = null,
            string[]? required = null, string[]? anyOf = null) => new()
        {
            Type = type, Version = version,
            Condition = Create(Required("broadcaster_user_id", broadcasterUserId), Optional("reward_id", rewardId)),
            RequiredScopes = required ?? [], AnyOfScopes = anyOf ?? [], AuthorizingUserId = broadcasterUserId,
        };
    }
}
