using TwitchDock.Core;
using static TwitchDock.EventSub.EventSubCondition;

namespace TwitchDock.EventSub;

public static partial class EventSubSubscriptions
{
    /// <summary>channel.charity_campaign.donate v1. Authorization: the broadcaster's channel:read:charity scope.</summary>
    public static EventSubSubscriptionSpec ChannelCharityCampaignDonateV1(string broadcasterUserId)
        => CommunitySystemSpecs.Broadcaster("channel.charity_campaign.donate", "1", broadcasterUserId, TwitchScopes.ChannelReadCharity);

    /// <summary>channel.charity_campaign.start v1. Authorization: the broadcaster's channel:read:charity scope.</summary>
    public static EventSubSubscriptionSpec ChannelCharityCampaignStartV1(string broadcasterUserId)
        => CommunitySystemSpecs.Broadcaster("channel.charity_campaign.start", "1", broadcasterUserId, TwitchScopes.ChannelReadCharity);

    /// <summary>channel.charity_campaign.progress v1. Authorization: the broadcaster's channel:read:charity scope.</summary>
    public static EventSubSubscriptionSpec ChannelCharityCampaignProgressV1(string broadcasterUserId)
        => CommunitySystemSpecs.Broadcaster("channel.charity_campaign.progress", "1", broadcasterUserId, TwitchScopes.ChannelReadCharity);

    /// <summary>channel.charity_campaign.stop v1. Authorization: the broadcaster's channel:read:charity scope.</summary>
    public static EventSubSubscriptionSpec ChannelCharityCampaignStopV1(string broadcasterUserId)
        => CommunitySystemSpecs.Broadcaster("channel.charity_campaign.stop", "1", broadcasterUserId, TwitchScopes.ChannelReadCharity);

    /// <summary>channel.goal.begin v1. Authorization: the broadcaster's channel:read:goals scope.</summary>
    public static EventSubSubscriptionSpec ChannelGoalBeginV1(string broadcasterUserId)
        => CommunitySystemSpecs.Broadcaster("channel.goal.begin", "1", broadcasterUserId, TwitchScopes.ChannelReadGoals);

    /// <summary>channel.goal.progress v1. Authorization: the broadcaster's channel:read:goals scope.</summary>
    public static EventSubSubscriptionSpec ChannelGoalProgressV1(string broadcasterUserId)
        => CommunitySystemSpecs.Broadcaster("channel.goal.progress", "1", broadcasterUserId, TwitchScopes.ChannelReadGoals);

    /// <summary>channel.goal.end v1. Authorization: the broadcaster's channel:read:goals scope.</summary>
    public static EventSubSubscriptionSpec ChannelGoalEndV1(string broadcasterUserId)
        => CommunitySystemSpecs.Broadcaster("channel.goal.end", "1", broadcasterUserId, TwitchScopes.ChannelReadGoals);

    /// <summary>channel.hype_train.begin v2. Authorization: the broadcaster's channel:read:hype_train scope.</summary>
    public static EventSubSubscriptionSpec ChannelHypeTrainBeginV2(string broadcasterUserId)
        => CommunitySystemSpecs.Broadcaster("channel.hype_train.begin", "2", broadcasterUserId, TwitchScopes.ChannelReadHypeTrain);

    /// <summary>channel.hype_train.progress v2. Authorization: the broadcaster's channel:read:hype_train scope.</summary>
    public static EventSubSubscriptionSpec ChannelHypeTrainProgressV2(string broadcasterUserId)
        => CommunitySystemSpecs.Broadcaster("channel.hype_train.progress", "2", broadcasterUserId, TwitchScopes.ChannelReadHypeTrain);

    /// <summary>channel.hype_train.end v2. Authorization: the broadcaster's channel:read:hype_train scope.</summary>
    public static EventSubSubscriptionSpec ChannelHypeTrainEndV2(string broadcasterUserId)
        => CommunitySystemSpecs.Broadcaster("channel.hype_train.end", "2", broadcasterUserId, TwitchScopes.ChannelReadHypeTrain);

    /// <summary>
    /// user.authorization.grant v1. Authorization: an app access token whose client ID matches <paramref name="clientId"/>.
    /// Webhook and conduit transports only.
    /// </summary>
    public static EventSubSubscriptionSpec UserAuthorizationGrantV1(string clientId) => new()
    {
        Type = "user.authorization.grant", Version = "1", Condition = Create(Required("client_id", clientId)), Transports = CommunitySystemSpecs.AppTokenTransports,
    };

    /// <summary>
    /// user.authorization.revoke v1. Authorization: an app access token whose client ID matches <paramref name="clientId"/>.
    /// Webhook and conduit transports only.
    /// </summary>
    public static EventSubSubscriptionSpec UserAuthorizationRevokeV1(string clientId) => new()
    {
        Type = "user.authorization.revoke", Version = "1", Condition = Create(Required("client_id", clientId)), Transports = CommunitySystemSpecs.AppTokenTransports,
    };

    /// <summary>
    /// user.update v1. Authorization: none required; the event's email is only populated when the user granted user:read:email to your client.
    /// </summary>
    public static EventSubSubscriptionSpec UserUpdateV1(string userId) => new()
    {
        Type = "user.update", Version = "1", Condition = Create(Required("user_id", userId)),
    };

    /// <summary>user.whisper.message v1. Authorization: the receiving user's user:read:whispers or user:manage:whispers scope.</summary>
    public static EventSubSubscriptionSpec UserWhisperMessageV1(string userId) => new()
    {
        Type = "user.whisper.message", Version = "1", Condition = Create(Required("user_id", userId)),
        AnyOfScopes = [TwitchScopes.UserReadWhispers, TwitchScopes.UserManageWhispers], AuthorizingUserId = userId,
    };

    /// <summary>
    /// conduit.shard.disabled v1. Authorization: an app access token whose client ID matches <paramref name="clientId"/>; when
    /// <paramref name="conduitId"/> is set, the client must own that conduit. App tokens restrict it to webhook and conduit transports.
    /// </summary>
    /// <param name="clientId">Your application's client ID.</param>
    /// <param name="conduitId">Optional conduit to watch; when omitted, events for all of the client's conduits are sent.</param>
    public static EventSubSubscriptionSpec ConduitShardDisabledV1(string clientId, string? conduitId = null) => new()
    {
        Type = "conduit.shard.disabled", Version = "1", Condition = Create(Required("client_id", clientId), Optional("conduit_id", conduitId)),
        Transports = CommunitySystemSpecs.AppTokenTransports,
    };

    /// <summary>
    /// drop.entitlement.grant v1. Authorization: an app access token whose client is owned by a member of the organization.
    /// Webhook and conduit transports only; notifications are batched (see <see cref="EventSubEvents.DropEntitlementGrantV1"/>).
    /// </summary>
    /// <param name="organizationId">The organization that owns the game on the developer portal.</param>
    /// <param name="categoryId">Optional category (game) filter.</param>
    /// <param name="campaignId">Optional Drops campaign filter.</param>
    public static EventSubSubscriptionSpec DropEntitlementGrantV1(string organizationId, string? categoryId = null, string? campaignId = null) => new()
    {
        Type = "drop.entitlement.grant", Version = "1",
        Condition = Create(Required("organization_id", organizationId), Optional("category_id", categoryId), Optional("campaign_id", campaignId)),
        Transports = CommunitySystemSpecs.AppTokenTransports, IsBatchingEnabled = true,
    };

    /// <summary>
    /// extension.bits_transaction.create v1. Authorization: an app access token whose client ID matches <paramref name="extensionClientId"/>.
    /// Webhook and conduit transports only.
    /// </summary>
    public static EventSubSubscriptionSpec ExtensionBitsTransactionCreateV1(string extensionClientId) => new()
    {
        Type = "extension.bits_transaction.create", Version = "1", Condition = Create(Required("extension_client_id", extensionClientId)),
        Transports = CommunitySystemSpecs.AppTokenTransports,
    };

    /// <summary>
    /// channel.guest_star_session.begin beta (public beta). Authorization: the moderator's or broadcaster's channel:read:guest_star,
    /// channel:manage:guest_star, moderator:read:guest_star or moderator:manage:guest_star scope.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelGuestStarSessionBeginBeta(string broadcasterUserId, string moderatorUserId)
        => CommunitySystemSpecs.GuestStar("channel.guest_star_session.begin", broadcasterUserId, moderatorUserId);

    /// <summary>
    /// channel.guest_star_session.end beta (public beta). Authorization: the moderator's or broadcaster's channel:read:guest_star,
    /// channel:manage:guest_star, moderator:read:guest_star or moderator:manage:guest_star scope.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelGuestStarSessionEndBeta(string broadcasterUserId, string moderatorUserId)
        => CommunitySystemSpecs.GuestStar("channel.guest_star_session.end", broadcasterUserId, moderatorUserId);

    /// <summary>
    /// channel.guest_star_guest.update beta (public beta). Authorization: the moderator's or broadcaster's channel:read:guest_star,
    /// channel:manage:guest_star, moderator:read:guest_star or moderator:manage:guest_star scope.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelGuestStarGuestUpdateBeta(string broadcasterUserId, string moderatorUserId)
        => CommunitySystemSpecs.GuestStar("channel.guest_star_guest.update", broadcasterUserId, moderatorUserId);

    /// <summary>
    /// channel.guest_star_settings.update beta (public beta). Authorization: the moderator's or broadcaster's channel:read:guest_star,
    /// channel:manage:guest_star, moderator:read:guest_star or moderator:manage:guest_star scope.
    /// </summary>
    public static EventSubSubscriptionSpec ChannelGuestStarSettingsUpdateBeta(string broadcasterUserId, string moderatorUserId)
        => CommunitySystemSpecs.GuestStar("channel.guest_star_settings.update", broadcasterUserId, moderatorUserId);
}

// File-local so the helper names cannot collide with other partial EventSubSubscriptions files.
file static class CommunitySystemSpecs
{
    public const EventSubTransports AppTokenTransports = EventSubTransports.Webhook | EventSubTransports.Conduit;

    public static EventSubSubscriptionSpec Broadcaster(string type, string version, string broadcasterUserId, string scope) => new()
    {
        Type = type, Version = version, Condition = Create(Required("broadcaster_user_id", broadcasterUserId)),
        RequiredScopes = [scope], AuthorizingUserId = broadcasterUserId,
    };

    public static EventSubSubscriptionSpec GuestStar(string type, string broadcasterUserId, string moderatorUserId) => new()
    {
        Type = type, Version = "beta",
        Condition = Create(Required("broadcaster_user_id", broadcasterUserId), Required("moderator_user_id", moderatorUserId)),
        AnyOfScopes = [TwitchScopes.ChannelReadGuestStar, TwitchScopes.ChannelManageGuestStar, TwitchScopes.ModeratorReadGuestStar, TwitchScopes.ModeratorManageGuestStar],
        AuthorizingUserId = moderatorUserId,
    };
}
