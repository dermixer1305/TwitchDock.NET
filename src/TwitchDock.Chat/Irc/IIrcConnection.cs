using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;

namespace TwitchDock.Chat.Irc;

/// <summary>
/// A line-oriented IRC transport. <see cref="TwitchIrcClient"/> creates one per connection attempt and disposes it afterwards.
/// One receive and one send may run concurrently; the client serializes sends.
/// </summary>
public interface IIrcConnection : IAsyncDisposable
{
    /// <summary>Opens the transport to <paramref name="uri"/>.</summary>
    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the next line without its CR LF terminator, or null when the server closed the connection.
    /// Transport failures throw, for example <see cref="System.Net.WebSockets.WebSocketException"/> or <see cref="IOException"/>.
    /// </summary>
    Task<string?> ReceiveLineAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sends one line. Implementations append CR LF and must reject lines that contain CR, LF or NUL with <see cref="ArgumentException"/>.
    /// Any other failure, including cancellation, may leave part of the line on the wire; the client then discards the connection.
    /// </summary>
    Task SendLineAsync(string line, CancellationToken cancellationToken);
}

/// <summary>Endpoint checks shared by the options and the transports.</summary>
internal static class IrcEndpoint
{
    /// <summary>
    /// True for IP literal loopback addresses and the host <c>localhost</c>, so plain-text endpoints stay on this machine. Other names are
    /// rejected even when hosts files usually map them to loopback (for example <c>localhost.localdomain</c> or <c>ip6-localhost</c>), because
    /// their resolution is outside the client's control. <see cref="Uri"/> itself normalizes the name <c>loopback</c> to <c>localhost</c>.
    /// </summary>
    public static bool IsLoopbackHost(Uri uri)
        => uri.IsAbsoluteUri && (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(uri.DnsSafeHost, out var address) && IPAddress.IsLoopback(address)));
}

/// <summary>Splits a byte stream into UTF-8 lines on LF, dropping a preceding CR. Oversized lines are discarded up to their terminator.</summary>
internal sealed class IrcLineDecoder
{
    public const int DefaultMaxLineBytes = 64 * 1024;
    private readonly Queue<string> _lines = new();
    private readonly int _maxLineBytes;
    private byte[] _buffer = new byte[1024];
    private int _count;
    private bool _discarding;

    public IrcLineDecoder(int maxLineBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLineBytes, 512);
        _maxLineBytes = maxLineBytes;
    }

    /// <summary>The number of oversized lines discarded so far.</summary>
    public int DiscardedLines { get; private set; }

    public bool TryDequeue([NotNullWhen(true)] out string? line) => _lines.TryDequeue(out line);

    public void Append(ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty)
        {
            var newline = data.IndexOf((byte)'\n');
            var chunk = newline < 0 ? data : data[..newline];
            if (!_discarding)
            {
                if (_count + chunk.Length > _maxLineBytes)
                {
                    _discarding = true;
                    _count = 0;
                    DiscardedLines++;
                }
                else Write(chunk);
            }
            if (newline < 0) return;
            if (!_discarding) Emit();
            _discarding = false;
            _count = 0;
            data = data[(newline + 1)..];
        }
    }

    private void Write(ReadOnlySpan<byte> chunk)
    {
        if (_count + chunk.Length > _buffer.Length) Array.Resize(ref _buffer, Math.Min(_maxLineBytes, Math.Max(_buffer.Length * 2, _count + chunk.Length)));
        chunk.CopyTo(_buffer.AsSpan(_count));
        _count += chunk.Length;
    }

    private void Emit()
    {
        var line = _buffer.AsSpan(0, _count);
        if (!line.IsEmpty && line[^1] == (byte)'\r') line = line[..^1];
        if (!line.IsEmpty) _lines.Enqueue(Encoding.UTF8.GetString(line));
    }

    internal static byte[] EncodeLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0) throw new ArgumentException("An IRC line must not contain CR, LF or NUL.", nameof(line));
        var bytes = new byte[Encoding.UTF8.GetByteCount(line) + 2];
        var written = Encoding.UTF8.GetBytes(line, bytes);
        bytes[written] = (byte)'\r';
        bytes[written + 1] = (byte)'\n';
        return bytes;
    }
}

/// <summary>Raised when the server closes the connection; classified as a transient connection failure.</summary>
internal sealed class IrcConnectionClosedException(string message) : IOException(message);
