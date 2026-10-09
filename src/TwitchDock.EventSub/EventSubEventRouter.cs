using System.Text.Json;
using TwitchDock.Helix.Models;

namespace TwitchDock.EventSub;

/// <summary>
/// Routes notifications to typed handlers for WebSocket messages and verified webhook payloads.
/// Register all handlers before dispatching; dispatching is then safe to call concurrently.
/// </summary>
public sealed class EventSubEventRouter
{
    private readonly Dictionary<(string Type, string Version), Func<EventSubPayload, CancellationToken, Task>> _handlers = [];
    private Func<EventSubSubscription, CancellationToken, Task>? _revocation;
    private Func<EventSubPayload, JsonException, CancellationToken, Task>? _deserializationError;

    public EventSubEventRouter On<TEvent>(EventSubEventDefinition<TEvent> definition, Func<TEvent, EventSubSubscription, CancellationToken, Task> handler)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(handler);
        if (!_handlers.TryAdd((definition.Type, definition.Version), (payload, ct) => InvokeAsync(definition, handler, payload, ct)))
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

    /// <summary>
    /// Observes notifications whose event does not match the typed model (for example after a Twitch schema change).
    /// Such a message is reported here, never to its typed handler, and counts as handled so it cannot stop a WebSocket
    /// client or make a webhook fail forever. Without this hook the message is dropped silently. Exceptions thrown by this
    /// hook propagate like handler exceptions.
    /// </summary>
    public EventSubEventRouter OnDeserializationError(Func<EventSubPayload, JsonException, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (_deserializationError is not null) throw new InvalidOperationException("A deserialization error handler is already registered.");
        _deserializationError = handler;
        return this;
    }

    /// <summary>Dispatches a WebSocket message. Returns false when no handler applies.</summary>
    public Task<bool> DispatchAsync(EventSubMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return DispatchAsync(message.Metadata.MessageType, message.Payload, cancellationToken);
    }

    /// <summary>
    /// Dispatches a payload with its message type (the Twitch-Eventsub-Message-Type header for webhooks). Returns false when no
    /// handler applies, and true when a handler ran or the event could not be deserialized (see <see cref="OnDeserializationError"/>).
    /// </summary>
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

    private async Task InvokeAsync<TEvent>(EventSubEventDefinition<TEvent> definition, Func<TEvent, EventSubSubscription, CancellationToken, Task> handler,
        EventSubPayload payload, CancellationToken ct) where TEvent : class
    {
        TEvent evt;
        // Only the deserialization is guarded; exceptions from the typed handler keep their semantics.
        try { evt = definition.Deserialize(payload.EventData); }
        catch (JsonException ex)
        {
            if (_deserializationError is { } report) await report(payload, ex, ct).ConfigureAwait(false);
            return;
        }
        await handler(evt, payload.Subscription!, ct).ConfigureAwait(false);
    }
}
