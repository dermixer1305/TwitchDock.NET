using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using TwitchDock.EventSub.Events;

namespace TwitchDock.EventSub;

/// <summary>Typed event definitions for every supported subscription type and version.</summary>
public static partial class EventSubEvents
{
    private static readonly Lazy<FrozenDictionary<(string Type, string Version), IEventSubEventDefinition>> Registry =
        new(() => Definitions().ToFrozenDictionary(d => (d.Type, d.Version)));

    public static EventSubEventDefinition<StreamOnlineEvent> StreamOnlineV1 { get; } = new("stream.online", "1", EventSubEventsJsonContext.Default.StreamOnlineEvent);
    public static EventSubEventDefinition<StreamOfflineEvent> StreamOfflineV1 { get; } = new("stream.offline", "1", EventSubEventsJsonContext.Default.StreamOfflineEvent);

    /// <summary>All registered definitions.</summary>
    public static IReadOnlyCollection<IEventSubEventDefinition> All => Registry.Value.Values;

    public static bool TryGetDefinition(string type, string version, [NotNullWhen(true)] out IEventSubEventDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(version);
        return Registry.Value.TryGetValue((type, version), out definition);
    }

    // Each group keeps its own region so parallel additions merge without conflicts.
    private static IEnumerable<IEventSubEventDefinition> Definitions()
    {
        yield return StreamOnlineV1;
        yield return StreamOfflineV1;
        // <group:chat-automod>
        yield return AutomodMessageHoldV1;
        yield return AutomodMessageHoldV2;
        yield return AutomodMessageUpdateV1;
        yield return AutomodMessageUpdateV2;
        yield return AutomodSettingsUpdateV1;
        yield return AutomodTermsUpdateV1;
        yield return ChannelChatClearV1;
        yield return ChannelChatClearUserMessagesV1;
        yield return ChannelChatMessageV1;
        yield return ChannelChatMessageDeleteV1;
        yield return ChannelChatNotificationV1;
        yield return ChannelChatSettingsUpdateV1;
        yield return ChannelChatUserMessageHoldV1;
        yield return ChannelChatUserMessageUpdateV1;
        yield return ChannelSharedChatBeginV1;
        yield return ChannelSharedChatUpdateV1;
        yield return ChannelSharedChatEndV1;
        yield return ChannelSuspiciousUserMessageV1;
        yield return ChannelSuspiciousUserUpdateV1;
        yield return ChannelWarningAcknowledgeV1;
        yield return ChannelWarningSendV1;
        // </group:chat-automod>
        // <group:moderation-channel>
        yield return ChannelUpdateV2;
        yield return ChannelFollowV2;
        yield return ChannelAdBreakBeginV1;
        yield return ChannelRaidV1;
        yield return ChannelBanV1;
        yield return ChannelUnbanV1;
        yield return ChannelUnbanRequestCreateV1;
        yield return ChannelUnbanRequestResolveV1;
        yield return ChannelModerateV1;
        yield return ChannelModerateV2;
        yield return ChannelModeratorAddV1;
        yield return ChannelModeratorRemoveV1;
        yield return ChannelVipAddV1;
        yield return ChannelVipRemoveV1;
        yield return ChannelShieldModeBeginV1;
        yield return ChannelShieldModeEndV1;
        yield return ChannelShoutoutCreateV1;
        yield return ChannelShoutoutReceiveV1;
        // </group:moderation-channel>
        // <group:monetization-interaction>
        yield return ChannelBitsUseV1;
        yield return ChannelSubscribeV1;
        yield return ChannelSubscriptionEndV1;
        yield return ChannelSubscriptionGiftV1;
        yield return ChannelSubscriptionMessageV1;
        yield return ChannelCheerV1;
        yield return ChannelPointsAutomaticRewardRedemptionAddV1;
        yield return ChannelPointsAutomaticRewardRedemptionAddV2;
        yield return ChannelPointsCustomRewardAddV1;
        yield return ChannelPointsCustomRewardUpdateV1;
        yield return ChannelPointsCustomRewardRemoveV1;
        yield return ChannelPointsCustomRewardRedemptionAddV1;
        yield return ChannelPointsCustomRewardRedemptionUpdateV1;
        yield return ChannelCustomPowerUpRedemptionAddV1;
        yield return ChannelPollBeginV1;
        yield return ChannelPollProgressV1;
        yield return ChannelPollEndV1;
        yield return ChannelPredictionBeginV1;
        yield return ChannelPredictionProgressV1;
        yield return ChannelPredictionLockV1;
        yield return ChannelPredictionEndV1;
        // </group:monetization-interaction>
        // <group:community-system>
        yield return ChannelCharityCampaignDonateV1;
        yield return ChannelCharityCampaignStartV1;
        yield return ChannelCharityCampaignProgressV1;
        yield return ChannelCharityCampaignStopV1;
        yield return ChannelGoalBeginV1;
        yield return ChannelGoalProgressV1;
        yield return ChannelGoalEndV1;
        yield return ChannelHypeTrainBeginV2;
        yield return ChannelHypeTrainProgressV2;
        yield return ChannelHypeTrainEndV2;
        yield return UserAuthorizationGrantV1;
        yield return UserAuthorizationRevokeV1;
        yield return UserUpdateV1;
        yield return UserWhisperMessageV1;
        yield return ConduitShardDisabledV1;
        yield return DropEntitlementGrantV1;
        yield return ExtensionBitsTransactionCreateV1;
        yield return ChannelGuestStarSessionBeginBeta;
        yield return ChannelGuestStarSessionEndBeta;
        yield return ChannelGuestStarGuestUpdateBeta;
        yield return ChannelGuestStarSettingsUpdateBeta;
        // </group:community-system>
    }
}
