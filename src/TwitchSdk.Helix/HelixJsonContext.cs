using System.Text.Json.Serialization;
using TwitchSdk.Core;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Helix;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HelixPage<TwitchUser>))]
[JsonSerializable(typeof(HelixPage<BlockedUser>))]
[JsonSerializable(typeof(HelixPage<InstalledUserExtension>))]
[JsonSerializable(typeof(UserActiveExtensionsResponse))]
[JsonSerializable(typeof(UpdateUserExtensionsRequest))]
[JsonSerializable(typeof(SendWhisperRequest))]
[JsonSerializable(typeof(ChannelStreamScheduleResponse))]
[JsonSerializable(typeof(CreateScheduleSegmentRequest))]
[JsonSerializable(typeof(UpdateScheduleSegmentRequest))]
[JsonSerializable(typeof(HelixPage<Conduit>))]
[JsonSerializable(typeof(CreateConduitRequest))]
[JsonSerializable(typeof(UpdateConduitRequest))]
[JsonSerializable(typeof(HelixPage<ConduitShard>))]
[JsonSerializable(typeof(UpdateConduitShardsRequest))]
[JsonSerializable(typeof(UpdateConduitShardsResponse))]
[JsonSerializable(typeof(HelixPage<HypeTrainStatus>))]
[JsonSerializable(typeof(HelixPage<TwitchStream>))]
[JsonSerializable(typeof(HelixPage<StreamKeyResult>))]
[JsonSerializable(typeof(CreateStreamMarkerRequest))]
[JsonSerializable(typeof(HelixPage<CreatedStreamMarker>))]
[JsonSerializable(typeof(HelixPage<StreamMarkerGroup>))]
[JsonSerializable(typeof(BroadcasterSubscriptionsResponse))]
[JsonSerializable(typeof(HelixPage<UserSubscription>))]
[JsonSerializable(typeof(BitsLeaderboardResponse))]
[JsonSerializable(typeof(HelixPage<Cheermote>))]
[JsonSerializable(typeof(HelixPage<ExtensionTransaction>))]
[JsonSerializable(typeof(CreateCustomRewardRequest))]
[JsonSerializable(typeof(UpdateCustomRewardRequest))]
[JsonSerializable(typeof(UpdateRedemptionStatusRequest))]
[JsonSerializable(typeof(HelixPage<CustomReward>))]
[JsonSerializable(typeof(HelixPage<CustomRewardRedemption>))]
[JsonSerializable(typeof(CreatePollRequest))]
[JsonSerializable(typeof(EndPollRequest))]
[JsonSerializable(typeof(HelixPage<TwitchPoll>))]
[JsonSerializable(typeof(CreatePredictionRequest))]
[JsonSerializable(typeof(EndPredictionRequest))]
[JsonSerializable(typeof(HelixPage<TwitchPrediction>))]
[JsonSerializable(typeof(HelixPage<ChannelInformation>))]
[JsonSerializable(typeof(ModifyChannelInformationRequest))]
[JsonSerializable(typeof(HelixPage<ChannelEditor>))]
[JsonSerializable(typeof(HelixPage<FollowedChannel>))]
[JsonSerializable(typeof(HelixPage<ChannelFollower>))]
[JsonSerializable(typeof(HelixPage<SendChatMessageResult>))]
[JsonSerializable(typeof(SendChatMessageRequest))]
[JsonSerializable(typeof(CreateEventSubSubscriptionRequest))]
[JsonSerializable(typeof(EventSubSubscriptionsResponse))]
[JsonSerializable(typeof(StartCommercialRequest))]
[JsonSerializable(typeof(HelixPage<CommercialResult>))]
[JsonSerializable(typeof(HelixPage<AdSchedule>))]
[JsonSerializable(typeof(HelixPage<AdSnoozeResult>))]
[JsonSerializable(typeof(HelixPage<ExtensionAnalyticsReport>))]
[JsonSerializable(typeof(HelixPage<GameAnalyticsReport>))]
[JsonSerializable(typeof(HelixPage<TwitchGame>))]
[JsonSerializable(typeof(HelixPage<CategorySearchResult>))]
[JsonSerializable(typeof(HelixPage<ChannelSearchResult>))]
[JsonSerializable(typeof(HelixPage<CreatorGoal>))]
[JsonSerializable(typeof(HelixPage<RaidResult>))]
[JsonSerializable(typeof(HelixPage<CreatedClip>))]
[JsonSerializable(typeof(HelixPage<TwitchClip>))]
[JsonSerializable(typeof(HelixPage<ClipDownload>))]
[JsonSerializable(typeof(HelixPage<TwitchVideo>))]
[JsonSerializable(typeof(HelixPage<string>))]
[JsonSerializable(typeof(HelixPage<CharityCampaign>))]
[JsonSerializable(typeof(HelixPage<CharityDonation>))]
[JsonSerializable(typeof(HelixPage<ChannelTeam>))]
[JsonSerializable(typeof(HelixPage<TwitchTeam>))]
// Each group keeps its own region so parallel additions merge without conflicts.
// <group:moderation-a>
// </group:moderation-a>
// <group:moderation-b>
// </group:moderation-b>
// <group:chat-a>
// </group:chat-a>
// <group:chat-b>
// </group:chat-b>
// <group:extensions>
// </group:extensions>
// <group:guest-star>
[JsonSerializable(typeof(HelixPage<GuestStarChannelSettings>))]
[JsonSerializable(typeof(GuestStarUpdateChannelSettingsRequest))]
[JsonSerializable(typeof(HelixPage<GuestStarSession>))]
[JsonSerializable(typeof(HelixPage<GuestStarInvite>))]
// </group:guest-star>
public partial class HelixJsonContext : JsonSerializerContext;
