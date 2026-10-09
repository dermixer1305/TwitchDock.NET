using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TwitchDock.Chat.Irc;
using TwitchDock.Core;

namespace TwitchDock.Tests;

/// <summary>Shared values and factories for the IRC client tests.</summary>
internal static class IrcTestSupport
{
    public const string Token = "secret-token";
    public const string Ping = "PING :tmi.twitch.tv";

    /// <summary>Bounds every await on client work so a regression fails the test instead of hanging the run.</summary>
    public static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    public static bool IsJoin(string line) => line.StartsWith("JOIN ", StringComparison.Ordinal);

    public static bool IsPrivmsg(string line) => line.StartsWith("PRIVMSG ", StringComparison.Ordinal);

    public static AccessToken UserToken(string value, params string[] scopes)
        => new(value, scopes: scopes.Length == 0 ? [TwitchScopes.ChatRead, TwitchScopes.ChatEdit] : scopes, kind: TwitchTokenKind.User);

    public static StaticAccessTokenProvider Tokens(params string[] scopes) => new(UserToken(Token, scopes));

    public static FakeIrcConnection Next(ConcurrentQueue<FakeIrcConnection> queue)
        => queue.TryDequeue(out var connection) ? connection : throw new InvalidOperationException("No more fake connections.");
}

/// <summary>Records log entries as "Level: message exception".</summary>
internal sealed class CapturingLogger : ILogger<TwitchIrcClient>
{
    private readonly ConcurrentQueue<string> _entries = new();
    public IReadOnlyCollection<string> Entries => _entries.ToArray();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => _entries.Enqueue($"{logLevel}: {formatter(state, exception)} {exception}");
}

/// <summary>
/// A scripted in-memory IRC transport. By default it answers NICK with CAP ACK and the 001 welcome. Setting <see cref="SendGate"/> makes
/// later sends wait for the gate, honoring their token unless <see cref="GateIgnoresCancellation"/> is set (a transport that hangs).
/// </summary>
internal sealed class FakeIrcConnection : IIrcConnection
{
    private readonly Channel<string?> _incoming = Channel.CreateUnbounded<string?>();
    private readonly ConcurrentQueue<string> _sent = new();
    private readonly ConcurrentQueue<string> _attempted = new();
    private volatile TaskCompletionSource? _sendGate;
    private volatile bool _disposed;
    private int _pending;

    public Func<string, IEnumerable<string>> Respond { get; init; } = line => line.StartsWith("NICK ", StringComparison.Ordinal)
        ? [":tmi.twitch.tv CAP * ACK :twitch.tv/tags twitch.tv/commands twitch.tv/membership", ":tmi.twitch.tv 001 bot :Welcome, GLHF!"]
        : [];
    public Exception? ConnectFailure { get; init; }
    public Exception? DisposeFailure { get; init; }
    public Exception? SendFailure { get; set; }
    public TaskCompletionSource? SendGate { get => _sendGate; set => _sendGate = value; }
    public bool GateIgnoresCancellation { get; init; }
    /// <summary>Reports a cancelled receive as <see cref="IOException"/>, like an aborted socket read on .NET 8.</summary>
    public bool CancellationAsIOException { get; init; }
    public CancellationToken LastSendToken { get; private set; }
    public Uri? Uri { get; private set; }
    public bool Disposed => _disposed;
    public int Pending => Volatile.Read(ref _pending);

    /// <summary>Lines whose write completed.</summary>
    public IReadOnlyList<string> Sent => _sent.ToArray();

    /// <summary>Lines whose write started, including writes still waiting for <see cref="SendGate"/>.</summary>
    public IReadOnlyList<string> Attempted => _attempted.ToArray();

    public void Enqueue(string line)
    {
        Interlocked.Increment(ref _pending);
        _incoming.Writer.TryWrite(line);
    }

    public void CloseFromServer()
    {
        Interlocked.Increment(ref _pending);
        _incoming.Writer.TryWrite(null);
    }

    public Task WaitForSentAsync(Func<string, bool> predicate) => ManualTimeProvider.WaitUntilAsync(() => Sent.Any(predicate));

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        Uri = uri;
        return ConnectFailure is null ? Task.CompletedTask : Task.FromException(ConnectFailure);
    }

    public async Task<string?> ReceiveLineAsync(CancellationToken cancellationToken)
    {
        try
        {
            var line = await _incoming.Reader.ReadAsync(cancellationToken);
            Interlocked.Decrement(ref _pending);
            return line;
        }
        catch (ChannelClosedException)
        {
            throw new WebSocketException("The fake connection was disposed.");
        }
        catch (OperationCanceledException ex) when (CancellationAsIOException)
        {
            throw new IOException("The I/O operation has been aborted.", ex);
        }
    }

    public Task SendLineAsync(string line, CancellationToken cancellationToken)
    {
        if (_disposed) return Task.FromException(new WebSocketException("The fake connection was disposed."));
        if (SendFailure is { } failure) return Task.FromException(failure);
        if (line.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0) throw new ArgumentException("Line injection.", nameof(line));
        LastSendToken = cancellationToken;
        _attempted.Enqueue(line);
        return _sendGate is { } gate ? CompleteAfterAsync(gate.Task, line, cancellationToken) : Complete(line);
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _incoming.Writer.TryComplete();
        return DisposeFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(DisposeFailure);
    }

    private Task Complete(string line)
    {
        _sent.Enqueue(line);
        foreach (var response in Respond(line)) Enqueue(response);
        return Task.CompletedTask;
    }

    private async Task CompleteAfterAsync(Task gate, string line, CancellationToken cancellationToken)
    {
        await (GateIgnoresCancellation ? gate : gate.WaitAsync(cancellationToken));
        await Complete(line);
    }
}

/// <summary>A manual clock that exposes the remaining delay of every active timer, so tests can wait for a specific timer before advancing.</summary>
internal sealed class IrcTestTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<Timer> _timers = [];
    private DateTimeOffset _now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() { lock (_gate) return _now; }
    public override long GetTimestamp() => GetUtcNow().UtcTicks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>
    /// True when timers with these remaining delays (seconds, any order, duplicates counted) are among the active timers.
    /// Other timers, for example a send timeout, may be active as well.
    /// </summary>
    public bool HasPendingDelays(params double[] seconds)
    {
        lock (_gate)
        {
            var remaining = _timers.Select(t => t.Due - _now).ToList();
            return seconds.All(delay => remaining.Remove(TimeSpan.FromSeconds(delay)));
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new Timer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan duration)
    {
        List<Timer> due;
        lock (_gate)
        {
            _now += duration;
            due = _timers.Where(t => t.Due <= _now).OrderBy(t => t.Due).ToList();
            foreach (var timer in due)
            {
                if (timer.Period == Timeout.InfiniteTimeSpan) _timers.Remove(timer);
                else timer.Due = _now + timer.Period;
            }
        }
        foreach (var timer in due) timer.Fire();
    }

    private sealed class Timer(IrcTestTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private bool _disposed;
        public DateTimeOffset Due { get; set; }
        public TimeSpan Period { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                if (_disposed) return false;
                owner._timers.Remove(this);
                Period = period;
                if (dueTime != Timeout.InfiniteTimeSpan)
                {
                    Due = owner._now + dueTime;
                    owner._timers.Add(this);
                }
                return true;
            }
        }

        public void Fire()
        {
            bool disposed;
            lock (owner._gate) disposed = _disposed;
            if (!disposed) callback(state);
        }

        public void Dispose()
        {
            lock (owner._gate)
            {
                _disposed = true;
                owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
