namespace TwitchDock.EventSub;

/// <summary>Transports that a subscription type accepts.</summary>
[Flags]
public enum EventSubTransports
{
    None = 0,
    WebSocket = 1,
    Webhook = 2,
    Conduit = 4,
    All = WebSocket | Webhook | Conduit,
}

/// <summary>
/// A typed subscription: the documented type, version and condition plus the authorization Twitch checks for it.
/// Create instances with <see cref="EventSubSubscriptions"/> and send them with
/// <see cref="HelixEventSubExtensions.CreateEventSubSubscriptionAsync"/>.
/// </summary>
public sealed class EventSubSubscriptionSpec
{
    public required string Type { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyDictionary<string, string> Condition { get; init; }
    /// <summary>Scopes the authorizing user must have granted (all of them).</summary>
    public IReadOnlyList<string> RequiredScopes { get; init => field = value ?? []; } = [];
    /// <summary>When nonempty, the authorizing user must also have granted at least one of these scopes.</summary>
    public IReadOnlyList<string> AnyOfScopes { get; init => field = value ?? []; } = [];
    /// <summary>The condition user whose token must authorize a WebSocket subscription; null when no specific user is required.</summary>
    public string? AuthorizingUserId { get; init; }
    public EventSubTransports Transports { get; init; } = EventSubTransports.All;
    /// <summary>Sends is_batching_enabled=true, which batched types such as drop.entitlement.grant require.</summary>
    public bool IsBatchingEnabled { get; init; }

    public override string ToString() => $"{Type}@{Version}";
}

/// <summary>Builds condition maps. Required values must be nonblank; optional null values are omitted.</summary>
internal static class EventSubCondition
{
    public static IReadOnlyDictionary<string, string> Create(params (string Name, string? Value, bool Required)[] values)
    {
        var condition = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value, required) in values)
        {
            if (value is null && !required) continue;
            ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
            condition.Add(name, value);
        }
        return condition.AsReadOnly();
    }

    public static (string Name, string? Value, bool Required) Required(string name, string value) => (name, value, true);
    public static (string Name, string? Value, bool Required) Optional(string name, string? value) => (name, value, false);
}
