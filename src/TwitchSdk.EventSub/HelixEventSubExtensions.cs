using TwitchSdk.Core;
using TwitchSdk.Helix;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.EventSub;

public static class HelixEventSubExtensions
{
    /// <summary>
    /// Creates a typed subscription. WebSocket subscriptions are preflighted against the user token's scopes and user ID;
    /// webhook and conduit subscriptions require an app token, and Twitch checks the user's grant to the app server side.
    /// </summary>
    public static Task<EventSubSubscriptionsResponse> CreateEventSubSubscriptionAsync(this HelixClient helix, EventSubSubscriptionSpec subscription,
        EventSubTransportRequest transport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(helix);
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(transport);
        var method = transport.Method switch
        {
            "websocket" => EventSubTransports.WebSocket,
            "webhook" => EventSubTransports.Webhook,
            "conduit" => EventSubTransports.Conduit,
            _ => throw new ArgumentException("Unknown EventSub transport.", nameof(transport)),
        };
        if (!subscription.Transports.HasFlag(method))
            throw new ArgumentException($"{subscription} does not support the {transport.Method} transport.", nameof(transport));
        var request = new CreateEventSubSubscriptionRequest
        {
            Type = subscription.Type, Version = subscription.Version, Condition = subscription.Condition, Transport = transport,
            IsBatchingEnabled = subscription.IsBatchingEnabled ? true : null,
        };
        var userRequirement = new TwitchAuthorizationRequirement(subscription.RequiredScopes, requiredUserId: subscription.AuthorizingUserId,
            anyUserScopes: subscription.AnyOfScopes);
        return helix.CreateEventSubSubscriptionAsync(request, userRequirement, cancellationToken);
    }

    /// <summary>Subscribes a WebSocket session after its welcome message.</summary>
    public static Task<EventSubSubscriptionsResponse> SubscribeWebSocketAsync(this HelixClient helix, EventSubSubscriptionSpec subscription,
        string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return helix.CreateEventSubSubscriptionAsync(subscription, new EventSubTransportRequest { Method = "websocket", SessionId = sessionId }, cancellationToken);
    }
}
