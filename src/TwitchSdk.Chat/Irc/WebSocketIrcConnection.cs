using System.Net.WebSockets;

namespace TwitchSdk.Chat.Irc;

/// <summary>
/// The default IRC transport over <see cref="ClientWebSocket"/>. A WebSocket message may carry several CR LF separated lines
/// and a line may span messages; lines longer than the configured cap are discarded.
/// </summary>
public sealed class WebSocketIrcConnection : IIrcConnection
{
    /// <summary>The default cap for a single received line in bytes.</summary>
    public const int DefaultMaxLineBytes = IrcLineDecoder.DefaultMaxLineBytes;
    private readonly ClientWebSocket _socket = new();
    private readonly IrcLineDecoder _decoder;
    private readonly byte[] _chunk = new byte[8192];
    private bool _closed;

    /// <param name="maxLineBytes">The largest accepted line in bytes, at least 512.</param>
    public WebSocketIrcConnection(int maxLineBytes = DefaultMaxLineBytes) => _decoder = new IrcLineDecoder(maxLineBytes);

    /// <inheritdoc />
    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme is not ("ws" or "wss")) throw new ArgumentException("A WebSocket IRC endpoint must use ws:// or wss://.", nameof(uri));
        return _socket.ConnectAsync(uri, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string?> ReceiveLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_decoder.TryDequeue(out var line)) return line;
            if (_closed) return null;
            var result = await _socket.ReceiveAsync(_chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) _closed = true;
            else _decoder.Append(_chunk.AsSpan(0, result.Count));
        }
    }

    /// <inheritdoc />
    public Task SendLineAsync(string line, CancellationToken cancellationToken)
        => _socket.SendAsync(IrcLineDecoder.EncodeLine(line).AsMemory(), WebSocketMessageType.Text, endOfMessage: true, cancellationToken).AsTask();

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
