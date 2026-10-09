using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace TwitchSdk.EventSub;

/// <summary>Untyped view of an event definition, used for registry lookups.</summary>
public interface IEventSubEventDefinition
{
    string Type { get; }
    string Version { get; }
    Type EventType { get; }
    object Deserialize(JsonElement element);
}

/// <summary>Binds a subscription type and version to its typed event payload. Instances live on <see cref="EventSubEvents"/>.</summary>
public sealed class EventSubEventDefinition<TEvent> : IEventSubEventDefinition where TEvent : class
{
    public EventSubEventDefinition(string type, string version, JsonTypeInfo<TEvent> typeInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        Type = type;
        Version = version;
        TypeInfo = typeInfo ?? throw new ArgumentNullException(nameof(typeInfo));
    }

    public string Type { get; }
    public string Version { get; }
    public JsonTypeInfo<TEvent> TypeInfo { get; }
    Type IEventSubEventDefinition.EventType => typeof(TEvent);

    public bool Matches(string? type, string? version) => string.Equals(type, Type, StringComparison.Ordinal) && string.Equals(version, Version, StringComparison.Ordinal);

    public TEvent Deserialize(JsonElement element)
    {
        // Most events are objects; batched types such as drop.entitlement.grant deliver an array.
        if (element.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)) throw new JsonException($"The {this} event must be a JSON object or array.");
        return element.Deserialize(TypeInfo) ?? throw new JsonException($"Empty {this} event.");
    }

    object IEventSubEventDefinition.Deserialize(JsonElement element) => Deserialize(element);

    public override string ToString() => $"{Type}@{Version}";
}
