using TwitchDock.EventSub.Events;

namespace TwitchDock.EventSub;

public static partial class EventSubEvents
{
    public static EventSubEventDefinition<ChannelCharityCampaignDonateEvent> ChannelCharityCampaignDonateV1 { get; } =
        new("channel.charity_campaign.donate", "1", EventSubEventsJsonContext.Default.ChannelCharityCampaignDonateEvent);
    public static EventSubEventDefinition<ChannelCharityCampaignStartEvent> ChannelCharityCampaignStartV1 { get; } =
        new("channel.charity_campaign.start", "1", EventSubEventsJsonContext.Default.ChannelCharityCampaignStartEvent);
    public static EventSubEventDefinition<ChannelCharityCampaignProgressEvent> ChannelCharityCampaignProgressV1 { get; } =
        new("channel.charity_campaign.progress", "1", EventSubEventsJsonContext.Default.ChannelCharityCampaignProgressEvent);
    public static EventSubEventDefinition<ChannelCharityCampaignStopEvent> ChannelCharityCampaignStopV1 { get; } =
        new("channel.charity_campaign.stop", "1", EventSubEventsJsonContext.Default.ChannelCharityCampaignStopEvent);

    public static EventSubEventDefinition<ChannelGoalBeginEvent> ChannelGoalBeginV1 { get; } =
        new("channel.goal.begin", "1", EventSubEventsJsonContext.Default.ChannelGoalBeginEvent);
    public static EventSubEventDefinition<ChannelGoalProgressEvent> ChannelGoalProgressV1 { get; } =
        new("channel.goal.progress", "1", EventSubEventsJsonContext.Default.ChannelGoalProgressEvent);
    public static EventSubEventDefinition<ChannelGoalEndEvent> ChannelGoalEndV1 { get; } =
        new("channel.goal.end", "1", EventSubEventsJsonContext.Default.ChannelGoalEndEvent);

    public static EventSubEventDefinition<ChannelHypeTrainBeginEvent> ChannelHypeTrainBeginV2 { get; } =
        new("channel.hype_train.begin", "2", EventSubEventsJsonContext.Default.ChannelHypeTrainBeginEvent);
    public static EventSubEventDefinition<ChannelHypeTrainProgressEvent> ChannelHypeTrainProgressV2 { get; } =
        new("channel.hype_train.progress", "2", EventSubEventsJsonContext.Default.ChannelHypeTrainProgressEvent);
    public static EventSubEventDefinition<ChannelHypeTrainEndEvent> ChannelHypeTrainEndV2 { get; } =
        new("channel.hype_train.end", "2", EventSubEventsJsonContext.Default.ChannelHypeTrainEndEvent);

    public static EventSubEventDefinition<UserAuthorizationGrantEvent> UserAuthorizationGrantV1 { get; } =
        new("user.authorization.grant", "1", EventSubEventsJsonContext.Default.UserAuthorizationGrantEvent);
    public static EventSubEventDefinition<UserAuthorizationRevokeEvent> UserAuthorizationRevokeV1 { get; } =
        new("user.authorization.revoke", "1", EventSubEventsJsonContext.Default.UserAuthorizationRevokeEvent);
    public static EventSubEventDefinition<UserUpdateEvent> UserUpdateV1 { get; } =
        new("user.update", "1", EventSubEventsJsonContext.Default.UserUpdateEvent);
    public static EventSubEventDefinition<UserWhisperMessageEvent> UserWhisperMessageV1 { get; } =
        new("user.whisper.message", "1", EventSubEventsJsonContext.Default.UserWhisperMessageEvent);

    public static EventSubEventDefinition<ConduitShardDisabledEvent> ConduitShardDisabledV1 { get; } =
        new("conduit.shard.disabled", "1", EventSubEventsJsonContext.Default.ConduitShardDisabledEvent);

    /// <summary>drop.entitlement.grant v1. Notifications are batched: each one carries a JSON array of entitlement events.</summary>
    public static EventSubEventDefinition<IReadOnlyList<DropEntitlementGrantEvent>> DropEntitlementGrantV1 { get; } =
        new("drop.entitlement.grant", "1", EventSubEventsJsonContext.Default.IReadOnlyListDropEntitlementGrantEvent);

    public static EventSubEventDefinition<ExtensionBitsTransactionCreateEvent> ExtensionBitsTransactionCreateV1 { get; } =
        new("extension.bits_transaction.create", "1", EventSubEventsJsonContext.Default.ExtensionBitsTransactionCreateEvent);

    public static EventSubEventDefinition<ChannelGuestStarSessionBeginEvent> ChannelGuestStarSessionBeginBeta { get; } =
        new("channel.guest_star_session.begin", "beta", EventSubEventsJsonContext.Default.ChannelGuestStarSessionBeginEvent);
    public static EventSubEventDefinition<ChannelGuestStarSessionEndEvent> ChannelGuestStarSessionEndBeta { get; } =
        new("channel.guest_star_session.end", "beta", EventSubEventsJsonContext.Default.ChannelGuestStarSessionEndEvent);
    public static EventSubEventDefinition<ChannelGuestStarGuestUpdateEvent> ChannelGuestStarGuestUpdateBeta { get; } =
        new("channel.guest_star_guest.update", "beta", EventSubEventsJsonContext.Default.ChannelGuestStarGuestUpdateEvent);
    public static EventSubEventDefinition<ChannelGuestStarSettingsUpdateEvent> ChannelGuestStarSettingsUpdateBeta { get; } =
        new("channel.guest_star_settings.update", "beta", EventSubEventsJsonContext.Default.ChannelGuestStarSettingsUpdateEvent);
}
