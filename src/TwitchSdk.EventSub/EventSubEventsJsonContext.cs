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
// </group:community-system>
public partial class EventSubEventsJsonContext : JsonSerializerContext;
