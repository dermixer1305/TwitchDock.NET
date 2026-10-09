using TwitchDock.EventSub.Events;

namespace TwitchDock.EventSub;

// Bits, subscriptions, Channel Points, custom Power-ups, polls and predictions. Names match the EventSubSubscriptions factories.
public static partial class EventSubEvents
{
    public static EventSubEventDefinition<ChannelBitsUseEvent> ChannelBitsUseV1 { get; } =
        new("channel.bits.use", "1", EventSubEventsJsonContext.Default.ChannelBitsUseEvent);
    public static EventSubEventDefinition<ChannelSubscribeEvent> ChannelSubscribeV1 { get; } =
        new("channel.subscribe", "1", EventSubEventsJsonContext.Default.ChannelSubscribeEvent);
    public static EventSubEventDefinition<ChannelSubscriptionEndEvent> ChannelSubscriptionEndV1 { get; } =
        new("channel.subscription.end", "1", EventSubEventsJsonContext.Default.ChannelSubscriptionEndEvent);
    public static EventSubEventDefinition<ChannelSubscriptionGiftEvent> ChannelSubscriptionGiftV1 { get; } =
        new("channel.subscription.gift", "1", EventSubEventsJsonContext.Default.ChannelSubscriptionGiftEvent);
    public static EventSubEventDefinition<ChannelSubscriptionMessageEvent> ChannelSubscriptionMessageV1 { get; } =
        new("channel.subscription.message", "1", EventSubEventsJsonContext.Default.ChannelSubscriptionMessageEvent);
    public static EventSubEventDefinition<ChannelCheerEvent> ChannelCheerV1 { get; } =
        new("channel.cheer", "1", EventSubEventsJsonContext.Default.ChannelCheerEvent);

    public static EventSubEventDefinition<ChannelPointsAutomaticRewardRedemptionAddEvent> ChannelPointsAutomaticRewardRedemptionAddV1 { get; } =
        new("channel.channel_points_automatic_reward_redemption.add", "1", EventSubEventsJsonContext.Default.ChannelPointsAutomaticRewardRedemptionAddEvent);
    public static EventSubEventDefinition<ChannelPointsAutomaticRewardRedemptionAddEventV2> ChannelPointsAutomaticRewardRedemptionAddV2 { get; } =
        new("channel.channel_points_automatic_reward_redemption.add", "2", EventSubEventsJsonContext.Default.ChannelPointsAutomaticRewardRedemptionAddEventV2);
    public static EventSubEventDefinition<ChannelPointsCustomRewardAddEvent> ChannelPointsCustomRewardAddV1 { get; } =
        new("channel.channel_points_custom_reward.add", "1", EventSubEventsJsonContext.Default.ChannelPointsCustomRewardAddEvent);
    public static EventSubEventDefinition<ChannelPointsCustomRewardUpdateEvent> ChannelPointsCustomRewardUpdateV1 { get; } =
        new("channel.channel_points_custom_reward.update", "1", EventSubEventsJsonContext.Default.ChannelPointsCustomRewardUpdateEvent);
    public static EventSubEventDefinition<ChannelPointsCustomRewardRemoveEvent> ChannelPointsCustomRewardRemoveV1 { get; } =
        new("channel.channel_points_custom_reward.remove", "1", EventSubEventsJsonContext.Default.ChannelPointsCustomRewardRemoveEvent);
    public static EventSubEventDefinition<ChannelPointsCustomRewardRedemptionAddEvent> ChannelPointsCustomRewardRedemptionAddV1 { get; } =
        new("channel.channel_points_custom_reward_redemption.add", "1", EventSubEventsJsonContext.Default.ChannelPointsCustomRewardRedemptionAddEvent);
    public static EventSubEventDefinition<ChannelPointsCustomRewardRedemptionUpdateEvent> ChannelPointsCustomRewardRedemptionUpdateV1 { get; } =
        new("channel.channel_points_custom_reward_redemption.update", "1", EventSubEventsJsonContext.Default.ChannelPointsCustomRewardRedemptionUpdateEvent);
    public static EventSubEventDefinition<ChannelCustomPowerUpRedemptionAddEvent> ChannelCustomPowerUpRedemptionAddV1 { get; } =
        new("channel.custom_power_up_redemption.add", "1", EventSubEventsJsonContext.Default.ChannelCustomPowerUpRedemptionAddEvent);

    public static EventSubEventDefinition<ChannelPollBeginEvent> ChannelPollBeginV1 { get; } =
        new("channel.poll.begin", "1", EventSubEventsJsonContext.Default.ChannelPollBeginEvent);
    public static EventSubEventDefinition<ChannelPollProgressEvent> ChannelPollProgressV1 { get; } =
        new("channel.poll.progress", "1", EventSubEventsJsonContext.Default.ChannelPollProgressEvent);
    public static EventSubEventDefinition<ChannelPollEndEvent> ChannelPollEndV1 { get; } =
        new("channel.poll.end", "1", EventSubEventsJsonContext.Default.ChannelPollEndEvent);
    public static EventSubEventDefinition<ChannelPredictionBeginEvent> ChannelPredictionBeginV1 { get; } =
        new("channel.prediction.begin", "1", EventSubEventsJsonContext.Default.ChannelPredictionBeginEvent);
    public static EventSubEventDefinition<ChannelPredictionProgressEvent> ChannelPredictionProgressV1 { get; } =
        new("channel.prediction.progress", "1", EventSubEventsJsonContext.Default.ChannelPredictionProgressEvent);
    public static EventSubEventDefinition<ChannelPredictionLockEvent> ChannelPredictionLockV1 { get; } =
        new("channel.prediction.lock", "1", EventSubEventsJsonContext.Default.ChannelPredictionLockEvent);
    public static EventSubEventDefinition<ChannelPredictionEndEvent> ChannelPredictionEndV1 { get; } =
        new("channel.prediction.end", "1", EventSubEventsJsonContext.Default.ChannelPredictionEndEvent);
}
