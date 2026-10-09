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
[JsonSerializable(typeof(ChannelUpdateEvent))]
[JsonSerializable(typeof(ChannelFollowEvent))]
[JsonSerializable(typeof(ChannelAdBreakBeginEvent))]
[JsonSerializable(typeof(ChannelRaidEvent))]
[JsonSerializable(typeof(ChannelBanEvent))]
[JsonSerializable(typeof(ChannelUnbanEvent))]
[JsonSerializable(typeof(ChannelUnbanRequestCreateEvent))]
[JsonSerializable(typeof(ChannelUnbanRequestResolveEvent))]
[JsonSerializable(typeof(ChannelModerateEvent))]
[JsonSerializable(typeof(ChannelModerateEventV2))]
[JsonSerializable(typeof(ChannelModeratorAddEvent))]
[JsonSerializable(typeof(ChannelModeratorRemoveEvent))]
[JsonSerializable(typeof(ChannelVipAddEvent))]
[JsonSerializable(typeof(ChannelVipRemoveEvent))]
[JsonSerializable(typeof(ChannelShieldModeBeginEvent))]
[JsonSerializable(typeof(ChannelShieldModeEndEvent))]
[JsonSerializable(typeof(ChannelShoutoutCreateEvent))]
[JsonSerializable(typeof(ChannelShoutoutReceiveEvent))]
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
