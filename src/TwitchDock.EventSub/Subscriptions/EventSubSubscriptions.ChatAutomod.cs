using TwitchDock.Core;
using static TwitchDock.EventSub.EventSubCondition;

namespace TwitchDock.EventSub;

public static partial class EventSubSubscriptions
{
    /// <summary>
    /// automod.message.hold v1. Authorization: moderator:manage:automod from the moderator in <paramref name="moderatorUserId"/>, who must be a
    /// moderator or the broadcaster. WebSocket: the user token must belong to that moderator. App tokens (webhook/conduit): the moderator must
    /// have granted moderator:manage:automod to the app.
    /// </summary>
    public static EventSubSubscriptionSpec AutomodMessageHoldV1(string broadcasterUserId, string moderatorUserId)
        => ChatAutomodModeratorSpec("automod.message.hold", "1", broadcasterUserId, moderatorUserId, TwitchScopes.ModeratorManageAutomod);

    /// <summary>
    /// automod.message.hold v2 (only public blocked terms notify). Authorization: moderator:manage:automod from the moderator in
    /// <paramref name="moderatorUserId"/>, who must be a moderator or the broadcaster. WebSocket: the user token must belong to that moderator.
    /// App tokens (webhook/conduit): the moderator must have granted moderator:manage:automod to the app.
    /// </summary>
    public static EventSubSubscriptionSpec AutomodMessageHoldV2(string broadcasterUserId, string moderatorUserId)
        => ChatAutomodModeratorSpec("automod.message.hold", "2", broadcasterUserId, moderatorUserId, TwitchScopes.ModeratorManageAutomod);

    /// <summary>
    /// automod.message.update v1. Authorization: moderator:manage:automod from the moderator in <paramref name="moderatorUserId"/>, who must be a
    /// moderator or the broadcaster. WebSocket: the user token must belong to that moderator. App tokens (webhook/conduit): the moderator must
    /// have granted moderator:manage:automod to the app.
    /// </summary>
    public static EventSubSubscriptionSpec AutomodMessageUpdateV1(string broadcasterUserId, string moderatorUserId)
        => ChatAutomodModeratorSpec("automod.message.update", "1", broadcasterUserId, moderatorUserId, TwitchScopes.ModeratorManageAutomod);

    /// <summary>
    /// automod.message.update v2 (only public blocked terms notify). Authorization: moderator:manage:automod from the moderator in
    /// <paramref name="moderatorUserId"/>, who must be a moderator or the broadcaster. WebSocket: the user token must belong to that moderator.
    /// App tokens (webhook/conduit): the moderator must have granted moderator:manage:automod to the app.
    /// </summary>
    public static EventSubSubscriptionSpec AutomodMessageUpdateV2(string broadcasterUserId, string moderatorUserId)
        => ChatAutomodModeratorSpec("automod.message.update", "2", broadcasterUserId, moderatorUserId, TwitchScopes.ModeratorManageAutomod);

    /// <summary>
    /// automod.settings.update v1. Authorization: moderator:read:automod_settings from the moderator in <paramref name="moderatorUserId"/>, who
    /// must be a moderator or the broadcaster. WebSocket: the user token must belong to that moderator. App tokens (webhook/conduit): the
    /// moderator must have granted moderator:read:automod_settings to the app.
    /// </summary>
    public static EventSubSubscriptionSpec AutomodSettingsUpdateV1(string broadcasterUserId, string moderatorUserId)
        => ChatAutomodModeratorSpec("automod.settings.update", "1", broadcasterUserId, moderatorUserId, TwitchScopes.ModeratorReadAutomodSettings);

    /// <summary>
    /// automod.terms.update v1 (private terms are not sent). Authorization: moderator:manage:automod from the moderator in
    /// <paramref name="moderatorUserId"/>, who must be a moderator or the broadcaster. WebSocket: the user token must belong to that moderator.
    /// App tokens (webhook/conduit): the moderator must have granted moderator:manage:automod to the app.
    /// </summary>
    public static EventSubSubscriptionSpec AutomodTermsUpdateV1(string broadcasterUserId, string moderatorUserId)
        => ChatAutomodModeratorSpec("automod.terms.update", "1", broadcasterUserId, moderatorUserId, TwitchScopes.ModeratorManageAutomod);

    /// <summary>
    /// channel.chat.clear v1. Authorization: user:read:chat from the chatting user in <paramref name="userId"/>; WebSocket tokens must belong to
    /// that user. App tokens additionally require user:bot from the chatting user and either channel:bot from the broadcaster or moderator status.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelChatClearV1(string broadcasterUserId, string userId)
        => ChatAutomodChatterSpec("channel.chat.clear", broadcasterUserId, userId);

    /// <summary>
    /// channel.chat.clear_user_messages v1. Authorization: user:read:chat from the chatting user in <paramref name="userId"/>; WebSocket tokens
    /// must belong to that user. App tokens additionally require user:bot from the chatting user and either channel:bot from the broadcaster or
    /// moderator status.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelChatClearUserMessagesV1(string broadcasterUserId, string userId)
        => ChatAutomodChatterSpec("channel.chat.clear_user_messages", broadcasterUserId, userId);

    /// <summary>
    /// channel.chat.message v1. Authorization: user:read:chat from the chatting user in <paramref name="userId"/>; WebSocket tokens must belong to
    /// that user. App tokens additionally require user:bot from the chatting user and either channel:bot from the broadcaster or moderator status.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelChatMessageV1(string broadcasterUserId, string userId)
        => ChatAutomodChatterSpec("channel.chat.message", broadcasterUserId, userId);

    /// <summary>
    /// channel.chat.message_delete v1. Authorization: user:read:chat from the chatting user in <paramref name="userId"/>; WebSocket tokens must
    /// belong to that user. App tokens additionally require user:bot from the chatting user and either channel:bot from the broadcaster or
    /// moderator status.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelChatMessageDeleteV1(string broadcasterUserId, string userId)
        => ChatAutomodChatterSpec("channel.chat.message_delete", broadcasterUserId, userId);

    /// <summary>
    /// channel.chat.notification v1. Authorization: user:read:chat from the chatting user in <paramref name="userId"/>; WebSocket tokens must
    /// belong to that user. App tokens additionally require user:bot from the chatting user and either channel:bot from the broadcaster or
    /// moderator status.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelChatNotificationV1(string broadcasterUserId, string userId)
        => ChatAutomodChatterSpec("channel.chat.notification", broadcasterUserId, userId);

    /// <summary>
    /// channel.chat_settings.update v1. Authorization: user:read:chat from the chatting user in <paramref name="userId"/>; WebSocket tokens must
    /// belong to that user. App tokens additionally require user:bot from the chatting user and either channel:bot from the broadcaster or
    /// moderator status.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelChatSettingsUpdateV1(string broadcasterUserId, string userId)
        => ChatAutomodChatterSpec("channel.chat_settings.update", broadcasterUserId, userId);

    /// <summary>
    /// channel.chat.user_message_hold v1. Authorization: user:read:chat from the chatting user in <paramref name="userId"/>; WebSocket tokens must
    /// belong to that user. App tokens additionally require user:bot from the chatting user.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelChatUserMessageHoldV1(string broadcasterUserId, string userId)
        => ChatAutomodChatterSpec("channel.chat.user_message_hold", broadcasterUserId, userId);

    /// <summary>
    /// channel.chat.user_message_update v1. Authorization: user:read:chat from the chatting user in <paramref name="userId"/>; WebSocket tokens
    /// must belong to that user. App tokens additionally require user:bot from the chatting user.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelChatUserMessageUpdateV1(string broadcasterUserId, string userId)
        => ChatAutomodChatterSpec("channel.chat.user_message_update", broadcasterUserId, userId);

    /// <summary>channel.shared_chat.begin v1. No authorization required.</summary>
    public static EventSubSubscriptionSpec ChannelSharedChatBeginV1(string broadcasterUserId) => new()
    {
        Type = "channel.shared_chat.begin", Version = "1", Condition = Create(Required("broadcaster_user_id", broadcasterUserId)),
    };

    /// <summary>channel.shared_chat.update v1. No authorization required.</summary>
    public static EventSubSubscriptionSpec ChannelSharedChatUpdateV1(string broadcasterUserId) => new()
    {
        Type = "channel.shared_chat.update", Version = "1", Condition = Create(Required("broadcaster_user_id", broadcasterUserId)),
    };

    /// <summary>channel.shared_chat.end v1. No authorization required.</summary>
    public static EventSubSubscriptionSpec ChannelSharedChatEndV1(string broadcasterUserId) => new()
    {
        Type = "channel.shared_chat.end", Version = "1", Condition = Create(Required("broadcaster_user_id", broadcasterUserId)),
    };

    /// <summary>
    /// channel.suspicious_user.message v1. Authorization: moderator:read:suspicious_users from the moderator in <paramref name="moderatorUserId"/>.
    /// WebSocket: the user token must belong to that moderator. Webhooks/conduits: the moderator must have granted the scope to the app.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelSuspiciousUserMessageV1(string broadcasterUserId, string moderatorUserId)
        => ChatAutomodModeratorSpec("channel.suspicious_user.message", "1", broadcasterUserId, moderatorUserId, TwitchScopes.ModeratorReadSuspiciousUsers);

    /// <summary>
    /// channel.suspicious_user.update v1. Authorization: moderator:read:suspicious_users from the moderator in <paramref name="moderatorUserId"/>.
    /// WebSocket: the user token must belong to that moderator. Webhooks/conduits: the moderator must have granted the scope to the app.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelSuspiciousUserUpdateV1(string broadcasterUserId, string moderatorUserId)
        => ChatAutomodModeratorSpec("channel.suspicious_user.update", "1", broadcasterUserId, moderatorUserId, TwitchScopes.ModeratorReadSuspiciousUsers);

    /// <summary>
    /// channel.warning.acknowledge v1. Authorization: moderator:read:warnings or moderator:manage:warnings from the moderator in
    /// <paramref name="moderatorUserId"/>; WebSocket tokens must belong to that moderator.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelWarningAcknowledgeV1(string broadcasterUserId, string moderatorUserId)
        => ChatAutomodWarningSpec("channel.warning.acknowledge", broadcasterUserId, moderatorUserId);

    /// <summary>
    /// channel.warning.send v1. Authorization: moderator:read:warnings or moderator:manage:warnings from the moderator in
    /// <paramref name="moderatorUserId"/>; WebSocket tokens must belong to that moderator.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelWarningSendV1(string broadcasterUserId, string moderatorUserId)
        => ChatAutomodWarningSpec("channel.warning.send", broadcasterUserId, moderatorUserId);

    private static EventSubSubscriptionSpec ChatAutomodModeratorSpec(string type, string version, string broadcasterUserId, string moderatorUserId, string scope) => new()
    {
        Type = type, Version = version,
        Condition = Create(Required("broadcaster_user_id", broadcasterUserId), Required("moderator_user_id", moderatorUserId)),
        RequiredScopes = [scope], AuthorizingUserId = moderatorUserId,
    };

    private static EventSubSubscriptionSpec ChatAutomodChatterSpec(string type, string broadcasterUserId, string userId) => new()
    {
        Type = type, Version = "1",
        Condition = Create(Required("broadcaster_user_id", broadcasterUserId), Required("user_id", userId)),
        RequiredScopes = [TwitchScopes.UserReadChat], AuthorizingUserId = userId,
    };

    private static EventSubSubscriptionSpec ChatAutomodWarningSpec(string type, string broadcasterUserId, string moderatorUserId) => new()
    {
        Type = type, Version = "1",
        Condition = Create(Required("broadcaster_user_id", broadcasterUserId), Required("moderator_user_id", moderatorUserId)),
        AnyOfScopes = [TwitchScopes.ModeratorReadWarnings, TwitchScopes.ModeratorManageWarnings], AuthorizingUserId = moderatorUserId,
    };
}
