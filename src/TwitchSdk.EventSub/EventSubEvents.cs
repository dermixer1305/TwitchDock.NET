using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using TwitchSdk.EventSub.Events;

namespace TwitchSdk.EventSub;

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
        // </group:chat-automod>
        // <group:moderation-channel>
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
        // </group:community-system>
    }
}
