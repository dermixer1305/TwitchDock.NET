using System.Net.Security;
using System.Net.Sockets;

namespace TwitchSdk.Chat.Irc;

/// <summary>
/// IRC over TCP. <c>ircs://</c> uses TLS with certificate validation against the host name (Twitch: <c>ircs://irc.chat.twitch.tv:6697</c>);
/// plain <c>irc://</c> is only accepted on loopback hosts, for example local test servers.
/// </summary>
public sealed class TcpIrcConnection : IIrcConnection
{
    /// <summary>The default cap for a single received line in bytes.</summary>
    public const int DefaultMaxLineBytes = IrcLineDecoder.DefaultMaxLineBytes;
    private const int DefaultTlsPort = 6697;
    private const int DefaultPlainPort = 6667;
    private readonly TcpClient _client = new();
    private readonly IrcLineDecoder _decoder;
    private readonly byte[] _chunk = new byte[8192];
    private Stream? _stream;
    private bool _closed;

    /// <param name="maxLineBytes">The largest accepted line in bytes, at least 512.</param>
    public TcpIrcConnection(int maxLineBytes = DefaultMaxLineBytes) => _decoder = new IrcLineDecoder(maxLineBytes);

    /// <inheritdoc />
    public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var tls = uri.Scheme == "ircs";
        if (!uri.IsAbsoluteUri || !(tls || (uri.Scheme == "irc" && uri.IsLoopback)))
            throw new ArgumentException("A TCP IRC endpoint must use ircs://, or irc:// on a loopback host.", nameof(uri));
        if (_stream is not null) throw new InvalidOperationException("The connection is already open.");
        var port = uri.Port > 0 ? uri.Port : tls ? DefaultTlsPort : DefaultPlainPort;
        await _client.ConnectAsync(uri.DnsSafeHost, port, cancellationToken).ConfigureAwait(false);
        Stream stream = _client.GetStream();
        if (tls)
        {
            var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
            try
            {
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = uri.DnsSafeHost }, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await ssl.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            stream = ssl;
        }
        _stream = stream;
    }

    /// <inheritdoc />
    public async Task<string?> ReceiveLineAsync(CancellationToken cancellationToken)
    {
        var stream = _stream ?? throw new InvalidOperationException("The connection is not open.");
        while (true)
        {
            if (_decoder.TryDequeue(out var line)) return line;
            if (_closed) return null;
            var read = await stream.ReadAsync(_chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0) _closed = true;
            else _decoder.Append(_chunk.AsSpan(0, read));
        }
    }

    /// <inheritdoc />
    public async Task SendLineAsync(string line, CancellationToken cancellationToken)
    {
        var bytes = IrcLineDecoder.EncodeLine(line);
        var stream = _stream ?? throw new InvalidOperationException("The connection is not open.");
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_stream is not null) await _stream.DisposeAsync().ConfigureAwait(false);
        _client.Dispose();
    }
}
