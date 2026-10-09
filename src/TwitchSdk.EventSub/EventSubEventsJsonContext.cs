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
// </group:monetization-interaction>
// <group:community-system>
[JsonSerializable(typeof(ChannelCharityCampaignDonateEvent))]
[JsonSerializable(typeof(ChannelCharityCampaignStartEvent))]
[JsonSerializable(typeof(ChannelCharityCampaignProgressEvent))]
[JsonSerializable(typeof(ChannelCharityCampaignStopEvent))]
[JsonSerializable(typeof(ChannelGoalBeginEvent))]
[JsonSerializable(typeof(ChannelGoalProgressEvent))]
[JsonSerializable(typeof(ChannelGoalEndEvent))]
[JsonSerializable(typeof(ChannelHypeTrainBeginEvent))]
[JsonSerializable(typeof(ChannelHypeTrainProgressEvent))]
[JsonSerializable(typeof(ChannelHypeTrainEndEvent))]
[JsonSerializable(typeof(UserAuthorizationGrantEvent))]
[JsonSerializable(typeof(UserAuthorizationRevokeEvent))]
[JsonSerializable(typeof(UserUpdateEvent))]
[JsonSerializable(typeof(UserWhisperMessageEvent))]
[JsonSerializable(typeof(ConduitShardDisabledEvent))]
[JsonSerializable(typeof(DropEntitlementGrantEvent))]
[JsonSerializable(typeof(IReadOnlyList<DropEntitlementGrantEvent>))]
[JsonSerializable(typeof(ExtensionBitsTransactionCreateEvent))]
[JsonSerializable(typeof(ChannelGuestStarSessionBeginEvent))]
[JsonSerializable(typeof(ChannelGuestStarSessionEndEvent))]
[JsonSerializable(typeof(ChannelGuestStarGuestUpdateEvent))]
[JsonSerializable(typeof(ChannelGuestStarSettingsUpdateEvent))]
// </group:community-system>
public partial class EventSubEventsJsonContext : JsonSerializerContext;
