using TwitchDock.Core;
using static TwitchDock.EventSub.EventSubCondition;

namespace TwitchDock.EventSub;

public static partial class EventSubSubscriptions
{
    /// <summary>channel.update v2. Authorization: No authorization required.</summary>
    public static EventSubSubscriptionSpec ChannelUpdateV2(string broadcasterUserId) => new()
    {
        Type = "channel.update", Version = "2", Condition = Create(Required("broadcaster_user_id", broadcasterUserId)),
    };

    /// <summary>
    /// channel.follow v2. Authorization: Must have moderator:read:followers scope.
    /// <paramref name="moderatorUserId"/> is the broadcaster or one of their moderators and must authorize the subscription.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelFollowV2(string broadcasterUserId, string moderatorUserId) => new()
    {
        Type = "channel.follow", Version = "2",
        Condition = Create(Required("broadcaster_user_id", broadcasterUserId), Required("moderator_user_id", moderatorUserId)),
        RequiredScopes = [TwitchScopes.ModeratorReadFollowers], AuthorizingUserId = moderatorUserId,
    };

    /// <summary>channel.ad_break.begin v1. Authorization: Must have channel:read:ads scope.</summary>
    public static EventSubSubscriptionSpec ChannelAdBreakBeginV1(string broadcasterUserId) => new()
    {
        Type = "channel.ad_break.begin", Version = "1", Condition = Create(Required("broadcaster_user_id", broadcasterUserId)),
        RequiredScopes = [TwitchScopes.ChannelReadAds], AuthorizingUserId = broadcasterUserId,
    };

    /// <summary>
    /// channel.raid v1. Authorization: No authorization required.
    /// Set exactly one of <paramref name="fromBroadcasterUserId"/> (raids the broadcaster starts) or
    /// <paramref name="toBroadcasterUserId"/> (raids the broadcaster receives); Twitch rejects both together.
    /// </summary>
    /// <exception cref="ArgumentException">Neither or both IDs are set, or the set ID is blank.</exception>
    public static EventSubSubscriptionSpec ChannelRaidV1(string? fromBroadcasterUserId = null, string? toBroadcasterUserId = null)
    {
        if ((fromBroadcasterUserId is null) == (toBroadcasterUserId is null))
            throw new ArgumentException("channel.raid requires exactly one of from_broadcaster_user_id or to_broadcaster_user_id.",
                fromBroadcasterUserId is null ? nameof(fromBroadcasterUserId) : nameof(toBroadcasterUserId));
        return new()
        {
            Type = "channel.raid", Version = "1",
            Condition = Create(Optional("from_broadcaster_user_id", fromBroadcasterUserId), Optional("to_broadcaster_user_id", toBroadcasterUserId)),
        };
    }

    /// <summary>channel.raid v1 for raids that <paramref name="fromBroadcasterUserId"/> starts. Authorization: No authorization required.</summary>
    public static EventSubSubscriptionSpec ChannelRaidFromBroadcasterV1(string fromBroadcasterUserId)
    {
        ArgumentNullException.ThrowIfNull(fromBroadcasterUserId);
        return ChannelRaidV1(fromBroadcasterUserId: fromBroadcasterUserId);
    }

    /// <summary>channel.raid v1 for raids that <paramref name="toBroadcasterUserId"/> receives. Authorization: No authorization required.</summary>
    public static EventSubSubscriptionSpec ChannelRaidToBroadcasterV1(string toBroadcasterUserId)
    {
        ArgumentNullException.ThrowIfNull(toBroadcasterUserId);
        return ChannelRaidV1(toBroadcasterUserId: toBroadcasterUserId);
    }

    /// <summary>channel.ban v1 (bans and timeouts). Authorization: Must have channel:moderate scope.</summary>
    public static EventSubSubscriptionSpec ChannelBanV1(string broadcasterUserId) => new()
    {
        Type = "channel.ban", Version = "1", Condition = Create(Required("broadcaster_user_id", broadcasterUserId)),
        RequiredScopes = [TwitchScopes.ChannelModerate], AuthorizingUserId = broadcasterUserId,
    };

    /// <summary>channel.unban v1. Authorization: Must have channel:moderate scope.</summary>
    public static EventSubSubscriptionSpec ChannelUnbanV1(string broadcasterUserId) => new()
    {
        Type = "channel.unban", Version = "1", Condition = Create(Required("broadcaster_user_id", broadcasterUserId)),
        RequiredScopes = [TwitchScopes.ChannelModerate], AuthorizingUserId = broadcasterUserId,
    };

    /// <summary>channel.unban_request.create v1. Authorization: Must have moderator:read:unban_requests or moderator:manage:unban_requests scope.</summary>
    public static EventSubSubscriptionSpec ChannelUnbanRequestCreateV1(string broadcasterUserId, string moderatorUserId) =>
        ModerationChannelSpecs.UnbanRequest("channel.unban_request.create", broadcasterUserId, moderatorUserId);

    /// <summary>
    /// channel.unban_request.resolve v1. Authorization: Must have moderator:read:unban_requests or moderator:manage:unban_requests scope;
    /// for WebSockets the moderator must match the user access token.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelUnbanRequestResolveV1(string broadcasterUserId, string moderatorUserId) =>
        ModerationChannelSpecs.UnbanRequest("channel.unban_request.resolve", broadcasterUserId, moderatorUserId);

    /// <summary>
    /// channel.moderate v1. Authorization: Must have moderator:read:moderators, moderator:read:vips and one scope of each pair
    /// moderator:read|manage:blocked_terms, :chat_settings, :unban_requests, :banned_users and :chat_messages.
    /// The local preflight checks the two fixed scopes and at least one pair scope; Twitch checks every pair.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelModerateV1(string broadcasterUserId, string moderatorUserId) =>
        ModerationChannelSpecs.Moderate("1", broadcasterUserId, moderatorUserId, ModerationChannelSpecs.ModerateV1PairScopes);

    /// <summary>
    /// channel.moderate v2 (adds warnings). Authorization: Must have moderator:read:moderators, moderator:read:vips and one scope of each pair
    /// moderator:read|manage:blocked_terms, :chat_settings, :unban_requests, :banned_users, :chat_messages and :warnings.
    /// The local preflight checks the two fixed scopes and at least one pair scope; Twitch checks every pair.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelModerateV2(string broadcasterUserId, string moderatorUserId) =>
        ModerationChannelSpecs.Moderate("2", broadcasterUserId, moderatorUserId, ModerationChannelSpecs.ModerateV2PairScopes);

    /// <summary>channel.moderator.add v1. Authorization: Must have moderation:read scope.</summary>
    public static EventSubSubscriptionSpec ChannelModeratorAddV1(string broadcasterUserId) => new()
    {
        Type = "channel.moderator.add", Version = "1", Condition = Create(Required("broadcaster_user_id", broadcasterUserId)),
        RequiredScopes = [TwitchScopes.ModerationRead], AuthorizingUserId = broadcasterUserId,
    };

    /// <summary>channel.moderator.remove v1. Authorization: Must have moderation:read scope.</summary>
    public static EventSubSubscriptionSpec ChannelModeratorRemoveV1(string broadcasterUserId) => new()
    {
        Type = "channel.moderator.remove", Version = "1", Condition = Create(Required("broadcaster_user_id", broadcasterUserId)),
        RequiredScopes = [TwitchScopes.ModerationRead], AuthorizingUserId = broadcasterUserId,
    };

    /// <summary>channel.vip.add v1. Authorization: Must have channel:read:vips or channel:manage:vips scope.</summary>
    public static EventSubSubscriptionSpec ChannelVipAddV1(string broadcasterUserId) => ModerationChannelSpecs.Vip("channel.vip.add", broadcasterUserId);

    /// <summary>channel.vip.remove v1. Authorization: Must have channel:read:vips or channel:manage:vips scope.</summary>
    public static EventSubSubscriptionSpec ChannelVipRemoveV1(string broadcasterUserId) => ModerationChannelSpecs.Vip("channel.vip.remove", broadcasterUserId);

    /// <summary>
    /// channel.shield_mode.begin v1. Authorization: Requires the moderator:read:shield_mode or moderator:manage:shield_mode scope;
    /// for WebSockets the moderator must match the user access token.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelShieldModeBeginV1(string broadcasterUserId, string moderatorUserId) =>
        ModerationChannelSpecs.ModeratorAnyOf("channel.shield_mode.begin", broadcasterUserId, moderatorUserId, TwitchScopes.ModeratorReadShieldMode, TwitchScopes.ModeratorManageShieldMode);

    /// <summary>
    /// channel.shield_mode.end v1. Authorization: Requires the moderator:read:shield_mode or moderator:manage:shield_mode scope;
    /// for WebSockets the moderator must match the user access token.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelShieldModeEndV1(string broadcasterUserId, string moderatorUserId) =>
        ModerationChannelSpecs.ModeratorAnyOf("channel.shield_mode.end", broadcasterUserId, moderatorUserId, TwitchScopes.ModeratorReadShieldMode, TwitchScopes.ModeratorManageShieldMode);

    /// <summary>
    /// channel.shoutout.create v1. Authorization: Requires the moderator:read:shoutouts or moderator:manage:shoutouts scope;
    /// for WebSockets the moderator must match the user access token.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelShoutoutCreateV1(string broadcasterUserId, string moderatorUserId) =>
        ModerationChannelSpecs.ModeratorAnyOf("channel.shoutout.create", broadcasterUserId, moderatorUserId, TwitchScopes.ModeratorReadShoutouts, TwitchScopes.ModeratorManageShoutouts);

    /// <summary>
    /// channel.shoutout.receive v1. Authorization: Requires the moderator:read:shoutouts or moderator:manage:shoutouts scope;
    /// for WebSockets the moderator must match the user access token.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelShoutoutReceiveV1(string broadcasterUserId, string moderatorUserId) =>
        ModerationChannelSpecs.ModeratorAnyOf("channel.shoutout.receive", broadcasterUserId, moderatorUserId, TwitchScopes.ModeratorReadShoutouts, TwitchScopes.ModeratorManageShoutouts);

    /// <summary>Helpers kept in a nested class so their names cannot collide with other partial files of EventSubSubscriptions.</summary>
    private static class ModerationChannelSpecs
    {
        // channel.moderate requires one scope of every read/manage pair plus moderator:read:moderators and moderator:read:vips.
        // The spec expresses only one alternative group, so the preflight checks the fixed scopes and at least one pair member;
        // Twitch validates the remaining pairs.
        private static readonly IReadOnlyList<string> ModerateFixedScopes = Array.AsReadOnly([TwitchScopes.ModeratorReadModerators, TwitchScopes.ModeratorReadVips]);

        private static readonly string[] ModerateV1Pairs =
        [
            TwitchScopes.ModeratorReadBlockedTerms, TwitchScopes.ModeratorManageBlockedTerms,
            TwitchScopes.ModeratorReadChatSettings, TwitchScopes.ModeratorManageChatSettings,
            TwitchScopes.ModeratorReadUnbanRequests, TwitchScopes.ModeratorManageUnbanRequests,
            TwitchScopes.ModeratorReadBannedUsers, TwitchScopes.ModeratorManageBannedUsers,
            TwitchScopes.ModeratorReadChatMessages, TwitchScopes.ModeratorManageChatMessages,
        ];

        public static readonly IReadOnlyList<string> ModerateV1PairScopes = Array.AsReadOnly(ModerateV1Pairs);
        public static readonly IReadOnlyList<string> ModerateV2PairScopes =
            Array.AsReadOnly([.. ModerateV1Pairs, TwitchScopes.ModeratorReadWarnings, TwitchScopes.ModeratorManageWarnings]);

        public static EventSubSubscriptionSpec UnbanRequest(string type, string broadcasterUserId, string moderatorUserId) =>
            ModeratorAnyOf(type, broadcasterUserId, moderatorUserId, TwitchScopes.ModeratorReadUnbanRequests, TwitchScopes.ModeratorManageUnbanRequests);

        public static EventSubSubscriptionSpec ModeratorAnyOf(string type, string broadcasterUserId, string moderatorUserId, string readScope, string manageScope) => new()
        {
            Type = type, Version = "1",
            Condition = Create(Required("broadcaster_user_id", broadcasterUserId), Required("moderator_user_id", moderatorUserId)),
            AnyOfScopes = [readScope, manageScope], AuthorizingUserId = moderatorUserId,
        };

        public static EventSubSubscriptionSpec Moderate(string version, string broadcasterUserId, string moderatorUserId, IReadOnlyList<string> pairScopes) => new()
        {
            Type = "channel.moderate", Version = version,
            Condition = Create(Required("broadcaster_user_id", broadcasterUserId), Required("moderator_user_id", moderatorUserId)),
            RequiredScopes = ModerateFixedScopes, AnyOfScopes = pairScopes, AuthorizingUserId = moderatorUserId,
        };

        public static EventSubSubscriptionSpec Vip(string type, string broadcasterUserId) => new()
        {
            Type = type, Version = "1", Condition = Create(Required("broadcaster_user_id", broadcasterUserId)),
            AnyOfScopes = [TwitchScopes.ChannelReadVips, TwitchScopes.ChannelManageVips], AuthorizingUserId = broadcasterUserId,
        };
    }
}
