using TwitchSdk.Helix.Models;

namespace TwitchSdk.EventSub;

/// <summary>
/// Routes notifications to typed handlers for WebSocket messages and verified webhook payloads.
/// Register all handlers before dispatching; dispatching is then safe to call concurrently.
/// </summary>
public sealed class EventSubEventRouter
{
    private readonly Dictionary<(string Type, string Version), Func<EventSubPayload, CancellationToken, Task>> _handlers = [];
    private Func<EventSubSubscription, CancellationToken, Task>? _revocation;

    public EventSubEventRouter On<TEvent>(EventSubEventDefinition<TEvent> definition, Func<TEvent, EventSubSubscription, CancellationToken, Task> handler)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(handler);
        if (!_handlers.TryAdd((definition.Type, definition.Version), (payload, ct) => handler(definition.Deserialize(payload.Event), payload.Subscription!, ct)))
            throw new InvalidOperationException($"A handler for {definition} is already registered.");
        return this;
    }

    /// <summary>Handles revoked subscriptions; the subscription status carries the reason.</summary>
    public EventSubEventRouter OnRevocation(Func<EventSubSubscription, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (_revocation is not null) throw new InvalidOperationException("A revocation handler is already registered.");
        _revocation = handler;
        return this;
    }

    /// <summary>Dispatches a WebSocket message. Returns false when no handler applies.</summary>
    public Task<bool> DispatchAsync(EventSubMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return DispatchAsync(message.Metadata.MessageType, message.Payload, cancellationToken);
    }

    /// <summary>Dispatches a payload with its message type (the Twitch-Eventsub-Message-Type header for webhooks). Returns false when no handler applies.</summary>
    public async Task<bool> DispatchAsync(string messageType, EventSubPayload payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Subscription is not { } subscription) return false;
        switch (messageType)
        {
            case "notification" when _handlers.TryGetValue((subscription.Type, subscription.Version), out var handler):
                await handler(payload, cancellationToken).ConfigureAwait(false);
                return true;
            case "revocation" when _revocation is not null:
                await _revocation(subscription, cancellationToken).ConfigureAwait(false);
                return true;
            default:
                return false;
        }
    }
}
