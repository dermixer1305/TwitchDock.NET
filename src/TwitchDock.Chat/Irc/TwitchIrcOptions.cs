namespace TwitchDock.Chat.Irc;

/// <summary>Configuration for <see cref="TwitchIrcClient"/>.</summary>
public sealed class TwitchIrcOptions
{
    private static readonly TimeSpan MaxDelay = TimeSpan.FromDays(1);

    /// <summary>Twitch's IRC WebSocket endpoint.</summary>
    public static Uri DefaultEndpoint { get; } = new("wss://irc-ws.chat.twitch.tv:443");

    /// <summary>Twitch's IRC TLS endpoint for <see cref="TcpIrcConnection"/>.</summary>
    public static Uri DefaultTcpEndpoint { get; } = new("ircs://irc.chat.twitch.tv:6697");

    /// <summary>The capabilities requested by default: tags, commands and membership.</summary>
    public static IReadOnlyList<string> DefaultCapabilities { get; } = Array.AsReadOnly(new[] { "twitch.tv/tags", "twitch.tv/commands", "twitch.tv/membership" });

    /// <summary>The login name of the user that owns the access token. It is sent as NICK in lower case.</summary>
    public required string Login { get; init; }

    /// <summary>
    /// The server. Accepts wss:// (default, <see cref="WebSocketIrcConnection"/>) and ircs:// (<see cref="TcpIrcConnection"/>);
    /// ws:// and irc:// are only accepted on loopback IP addresses and <c>localhost</c>, for example test servers.
    /// </summary>
    public Uri Endpoint { get; init; } = DefaultEndpoint;

    /// <summary>Capabilities requested with <c>CAP REQ</c>; empty to request none. Typed views need <c>twitch.tv/tags</c>.</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = DefaultCapabilities;

    /// <summary>The PRIVMSG limit. Use <see cref="IrcRateLimit.ModeratorMessages"/> only when the account is broadcaster or moderator in every channel it sends to.</summary>
    public IrcRateLimit MessageRateLimit { get; init; } = IrcRateLimit.Messages;

    /// <summary>The JOIN limit, applied per channel joined.</summary>
    public IrcRateLimit JoinRateLimit { get; init; } = IrcRateLimit.Joins;

    /// <summary>The limit for login attempts, including reconnects.</summary>
    public IrcRateLimit AuthenticationRateLimit { get; init; } = IrcRateLimit.Authentication;

    /// <summary>Idle time after which the client sends PING. <see cref="Timeout.InfiniteTimeSpan"/> disables client keepalive.</summary>
    public TimeSpan KeepaliveInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long the client waits for any data after its PING before reconnecting. It also bounds every send: a line that cannot be written
    /// within this time marks the connection as dead and the client reconnects.
    /// </summary>
    public TimeSpan KeepaliveTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>The time allowed to connect and complete the login handshake.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The upper bound for the exponential reconnect backoff, which starts at one second. The backoff only resets after a connection stayed
    /// up for 30 seconds, so a server that accepts and then drops connections is retried with growing delays.
    /// </summary>
    public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        TwitchIrcClient.NormalizeLogin(Login);
        if (Endpoint is null || !Endpoint.IsAbsoluteUri || !string.IsNullOrEmpty(Endpoint.UserInfo)
            || !(Endpoint.Scheme is "wss" or "ircs" || (Endpoint.Scheme is "ws" or "irc" && IrcEndpoint.IsLoopbackHost(Endpoint))))
            throw new ArgumentException("Endpoint must use wss:// or ircs://, or ws:// or irc:// on a loopback IP address or localhost, without credentials.", nameof(Endpoint));
        if (Capabilities is null || Capabilities.Any(c => string.IsNullOrWhiteSpace(c) || c.AsSpan().IndexOfAny(" \r\n\0") >= 0 || c[0] == ':'))
            throw new ArgumentException("Capabilities must be nonempty names without spaces, CR, LF or NUL.", nameof(Capabilities));
        if (MessageRateLimit is null || JoinRateLimit is null || AuthenticationRateLimit is null)
            throw new ArgumentException("Rate limits must not be null.", nameof(MessageRateLimit));
        if (KeepaliveInterval != Timeout.InfiniteTimeSpan && !IsPositive(KeepaliveInterval)) throw new ArgumentOutOfRangeException(nameof(KeepaliveInterval));
        if (!IsPositive(KeepaliveTimeout)) throw new ArgumentOutOfRangeException(nameof(KeepaliveTimeout));
        if (!IsPositive(ConnectTimeout)) throw new ArgumentOutOfRangeException(nameof(ConnectTimeout));
        if (MaxReconnectDelay < TimeSpan.FromSeconds(1) || MaxReconnectDelay > MaxDelay) throw new ArgumentOutOfRangeException(nameof(MaxReconnectDelay));
    }

    private static bool IsPositive(TimeSpan value) => value > TimeSpan.Zero && value <= MaxDelay;
}
