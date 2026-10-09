using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.EventSub;

public sealed class EventSubMessage
{
    public required EventSubMetadata Metadata { get; init; }
    public required EventSubPayload Payload { get; init; }
    public T ReadEvent<T>(JsonTypeInfo<T> type) => Payload.Event.Deserialize(type) ?? throw new JsonException("Empty EventSub event.");

    /// <summary>Reads a typed notification event. Returns false for other message types and other subscription types or versions.</summary>
    public bool TryReadEvent<TEvent>(EventSubEventDefinition<TEvent> definition, [NotNullWhen(true)] out TEvent? evt) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(definition);
        evt = null;
        return Metadata.MessageType == "notification" && Payload.TryReadEvent(definition, out evt);
    }

    public static EventSubMessage Parse(ReadOnlySpan<byte> utf8)
        => JsonSerializer.Deserialize(utf8, EventSubJsonContext.Default.EventSubMessage) ?? throw new JsonException("Empty EventSub message.");
}

public sealed class EventSubMetadata
{
    public required string MessageId { get; init; }
    public required string MessageType { get; init; }
    // Preserve Twitch's nanosecond timestamps without truncating the signed text.
    public required string MessageTimestamp { get; init; }
    public string? SubscriptionType { get; init; }
    public string? SubscriptionVersion { get; init; }
}

public sealed class EventSubPayload
{
    public EventSubSession? Session { get; init; }
    public EventSubSubscription? Subscription { get; init; }
    public JsonElement Event { get; init; }
    /// <summary>Batched subscription types such as drop.entitlement.grant deliver an array here instead of <see cref="Event"/>.</summary>
    public JsonElement Events { get; init; }
    public string? Challenge { get; init; }

    /// <summary>The event payload: <see cref="Event"/>, or <see cref="Events"/> for batched types.</summary>
    internal JsonElement EventData => Event.ValueKind != JsonValueKind.Undefined ? Event : Events;

    /// <summary>Reads a typed event when the payload's subscription matches the definition. Returns false when no event is present.</summary>
    public bool TryReadEvent<TEvent>(EventSubEventDefinition<TEvent> definition, [NotNullWhen(true)] out TEvent? evt) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(definition);
        evt = null;
        if (Subscription is null || EventData.ValueKind == JsonValueKind.Undefined || !definition.Matches(Subscription.Type, Subscription.Version)) return false;
        evt = definition.Deserialize(EventData);
        return true;
    }
}

public sealed class EventSubSession
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public int? KeepaliveTimeoutSeconds { get; init; }
    public string? ReconnectUrl { get; init; }
    public string? ConnectedAt { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(EventSubMessage))]
[JsonSerializable(typeof(EventSubPayload))]
public partial class EventSubJsonContext : JsonSerializerContext;
