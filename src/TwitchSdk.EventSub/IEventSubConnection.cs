using System.Net.WebSockets;
using System.Text.Json;

namespace TwitchSdk.EventSub;

public interface IEventSubConnection : IAsyncDisposable
{
    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);
    Task<EventSubMessage> ReceiveAsync(CancellationToken cancellationToken);
}

public sealed class ClientWebSocketConnection : IEventSubConnection
{
    private readonly ClientWebSocket _socket = new();
    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken) => _socket.ConnectAsync(uri, cancellationToken);

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
}
