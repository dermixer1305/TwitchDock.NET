using System.Net.WebSockets;

namespace TwitchDock.Chat.Irc;

/// <summary>
/// The default IRC transport over <see cref="ClientWebSocket"/>. A WebSocket message may carry several CR LF separated lines
/// and a line may span messages; lines longer than the configured cap are discarded.
/// Plain <c>ws://</c> is only accepted on loopback IP addresses and <c>localhost</c>, for example local test servers.
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

    /// <summary>The number of oversized received lines discarded so far.</summary>
    internal int DiscardedLines => _decoder.DiscardedLines;

    /// <inheritdoc />
    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || !(uri.Scheme == "wss" || (uri.Scheme == "ws" && IrcEndpoint.IsLoopbackHost(uri))))
            throw new ArgumentException("A WebSocket IRC endpoint must use wss://, or ws:// on a loopback IP address or localhost.", nameof(uri));
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
