using System.Text.Json.Serialization;
using TwitchSdk.EventSub.Events;

namespace TwitchSdk.EventSub;

// Each group keeps its own region so parallel additions merge without conflicts.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(StreamOnlineEvent))]
[JsonSerializable(typeof(StreamOfflineEvent))]
// <group:chat-automod>
// </group:chat-automod>
// <group:moderation-channel>
// </group:moderation-channel>
// <group:monetization-interaction>
[JsonSerializable(typeof(ChannelBitsUseEvent))]
[JsonSerializable(typeof(ChannelSubscribeEvent))]
[JsonSerializable(typeof(ChannelSubscriptionEndEvent))]
[JsonSerializable(typeof(ChannelSubscriptionGiftEvent))]
[JsonSerializable(typeof(ChannelSubscriptionMessageEvent))]
[JsonSerializable(typeof(ChannelCheerEvent))]
[JsonSerializable(typeof(ChannelPointsAutomaticRewardRedemptionAddEvent))]
[JsonSerializable(typeof(ChannelPointsAutomaticRewardRedemptionAddEventV2))]
[JsonSerializable(typeof(ChannelPointsCustomRewardAddEvent))]
[JsonSerializable(typeof(ChannelPointsCustomRewardUpdateEvent))]
[JsonSerializable(typeof(ChannelPointsCustomRewardRemoveEvent))]
[JsonSerializable(typeof(ChannelPointsCustomRewardRedemptionAddEvent))]
[JsonSerializable(typeof(ChannelPointsCustomRewardRedemptionUpdateEvent))]
[JsonSerializable(typeof(ChannelCustomPowerUpRedemptionAddEvent))]
[JsonSerializable(typeof(ChannelPollBeginEvent))]
[JsonSerializable(typeof(ChannelPollProgressEvent))]
[JsonSerializable(typeof(ChannelPollEndEvent))]
[JsonSerializable(typeof(ChannelPredictionBeginEvent))]
[JsonSerializable(typeof(ChannelPredictionProgressEvent))]
[JsonSerializable(typeof(ChannelPredictionLockEvent))]
[JsonSerializable(typeof(ChannelPredictionEndEvent))]
// </group:monetization-interaction>
// <group:community-system>
// </group:community-system>
public partial class EventSubEventsJsonContext : JsonSerializerContext;
