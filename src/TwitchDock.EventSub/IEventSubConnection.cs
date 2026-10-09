using System.Net;
using System.Net.WebSockets;
using System.Text.Json;

namespace TwitchDock.EventSub;

public interface IEventSubConnection : IAsyncDisposable
{
    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);
    Task<EventSubMessage> ReceiveAsync(CancellationToken cancellationToken);
}

public sealed class ClientWebSocketConnection : IEventSubConnection
{
    private readonly ClientWebSocket _socket = new();

    /// <summary>Connects over wss://. Plain ws:// is only accepted for a loopback IP address or exactly localhost.</summary>
    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!IsAllowedEndpoint(uri)) throw new ArgumentException("EventSub connections require wss://, or ws:// on a loopback IP address or localhost.", nameof(uri));
        return _socket.ConnectAsync(uri, cancellationToken);
    }

    public async Task<EventSubMessage> ReceiveAsync(CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var result = await _socket.ReceiveAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) throw new WebSocketException($"EventSub connection closed ({(int?)_socket.CloseStatus}).");
            if (result.MessageType != WebSocketMessageType.Text) throw new JsonException("Expected an EventSub text message.");
            if (buffer.Length + result.Count > 1024 * 1024) throw new JsonException("EventSub message exceeds the 1 MiB limit.");
            buffer.Write(chunk, 0, result.Count);
            if (result.EndOfMessage) return EventSubMessage.Parse(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)));
        }
    }

    public ValueTask DisposeAsync() { _socket.Dispose(); return ValueTask.CompletedTask; }

    /// <summary>
    /// An absolute wss:// URI, or ws:// whose host is an IP-literal loopback address or the host name localhost (Uri also
    /// normalizes "loopback" to it), without user info.
    /// </summary>
    internal static bool IsAllowedEndpoint(Uri uri)
        => uri.IsAbsoluteUri && string.IsNullOrEmpty(uri.UserInfo) && (uri.Scheme == "wss" || (uri.Scheme == "ws" && IsLoopbackHost(uri)));

    private static bool IsLoopbackHost(Uri uri) => uri.HostNameType switch
    {
        UriHostNameType.IPv4 or UriHostNameType.IPv6 => IPAddress.TryParse(uri.DnsSafeHost, out var address) && IPAddress.IsLoopback(address),
        UriHostNameType.Dns => string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };
}
