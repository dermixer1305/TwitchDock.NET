using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using TwitchSdk.EventSub.Events;

namespace TwitchSdk.EventSub;

/// <summary>Typed event definitions for every supported subscription type and version.</summary>
public static partial class EventSubEvents
{
    private static readonly Lazy<FrozenDictionary<(string Type, string Version), IEventSubEventDefinition>> Registry =
        new(() => Definitions().ToFrozenDictionary(d => (d.Type, d.Version)));

    public static EventSubEventDefinition<StreamOnlineEvent> StreamOnlineV1 { get; } = new("stream.online", "1", EventSubEventsJsonContext.Default.StreamOnlineEvent);
    public static EventSubEventDefinition<StreamOfflineEvent> StreamOfflineV1 { get; } = new("stream.offline", "1", EventSubEventsJsonContext.Default.StreamOfflineEvent);

    /// <summary>All registered definitions.</summary>
    public static IReadOnlyCollection<IEventSubEventDefinition> All => Registry.Value.Values;

    public static bool TryGetDefinition(string type, string version, [NotNullWhen(true)] out IEventSubEventDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(version);
        return Registry.Value.TryGetValue((type, version), out definition);
    }

    // Each group keeps its own region so parallel additions merge without conflicts.
    private static IEnumerable<IEventSubEventDefinition> Definitions()
    {
        yield return StreamOnlineV1;
        yield return StreamOfflineV1;
        // <group:chat-automod>
        // </group:chat-automod>
        // <group:moderation-channel>
        // </group:moderation-channel>
        // <group:monetization-interaction>
        // </group:monetization-interaction>
        // <group:community-system>
        // </group:community-system>
    }
}
