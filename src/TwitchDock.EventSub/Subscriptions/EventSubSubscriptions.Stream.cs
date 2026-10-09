using static TwitchDock.EventSub.EventSubCondition;

namespace TwitchDock.EventSub;

public static partial class EventSubSubscriptions
{
    /// <summary>stream.online v1. No authorization required.</summary>
    public static EventSubSubscriptionSpec StreamOnlineV1(string broadcasterUserId) => new()
    {
        Type = "stream.online", Version = "1", Condition = Create(Required("broadcaster_user_id", broadcasterUserId)),
    };

    /// <summary>stream.offline v1. No authorization required.</summary>
    public static EventSubSubscriptionSpec StreamOfflineV1(string broadcasterUserId) => new()
    {
        Type = "stream.offline", Version = "1", Condition = Create(Required("broadcaster_user_id", broadcasterUserId)),
    };
}
