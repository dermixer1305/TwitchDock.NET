namespace TwitchSdk.Chat.Irc;

/// <summary>A sliding-window limit: at most <see cref="PermitLimit"/> operations in any <see cref="Window"/>.</summary>
public sealed record IrcRateLimit
{
    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(1);

    /// <param name="permitLimit">Operations allowed per window, at least 1.</param>
    /// <param name="window">The window length, greater than zero and at most one day.</param>
    public IrcRateLimit(int permitLimit, TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(permitLimit, 1);
        if (window <= TimeSpan.Zero || window > MaxWindow) throw new ArgumentOutOfRangeException(nameof(window), "The window must be greater than zero and at most one day.");
        PermitLimit = permitLimit;
        Window = window;
    }

    /// <summary>Operations allowed per window.</summary>
    public int PermitLimit { get; }

    /// <summary>The window length.</summary>
    public TimeSpan Window { get; }

    /// <summary>Twitch's chat limit for regular users: 20 messages per 30 seconds.</summary>
    public static IrcRateLimit Messages { get; } = new(20, TimeSpan.FromSeconds(30));

    /// <summary>Twitch's chat limit when the sender is the broadcaster or a moderator in every channel it sends to: 100 messages per 30 seconds.</summary>
    public static IrcRateLimit ModeratorMessages { get; } = new(100, TimeSpan.FromSeconds(30));

    /// <summary>Twitch's JOIN limit: 20 channels per 10 seconds.</summary>
    public static IrcRateLimit Joins { get; } = new(20, TimeSpan.FromSeconds(10));

    /// <summary>Twitch's authentication limit: 20 login attempts per 10 seconds.</summary>
    public static IrcRateLimit Authentication { get; } = new(20, TimeSpan.FromSeconds(10));
}

/// <summary>
/// A thread-safe sliding-window rate limiter driven by a <see cref="TimeProvider"/>. Each acquisition holds its permit for one window.
/// Waiters are served one at a time in arrival order and waits are cancellable.
/// </summary>
public sealed class IrcSlidingWindowRateLimiter
{
    private readonly Queue<long> _grants = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private readonly TimeProvider _time;

    /// <param name="limit">The limit to enforce.</param>
    /// <param name="timeProvider">The clock; defaults to <see cref="TimeProvider.System"/>.</param>
    public IrcSlidingWindowRateLimiter(IrcRateLimit limit, TimeProvider? timeProvider = null)
    {
        Limit = limit ?? throw new ArgumentNullException(nameof(limit));
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The enforced limit.</summary>
    public IrcRateLimit Limit { get; }

    /// <summary>The number of permits that could be acquired without waiting right now.</summary>
    public int AvailablePermits
    {
        get
        {
            lock (_sync)
            {
                Prune(_time.GetTimestamp());
                return Limit.PermitLimit - _grants.Count;
            }
        }
    }

    /// <summary>Acquires a permit when one is available without waiting.</summary>
    public bool TryAcquire()
    {
        if (!_gate.Wait(0)) return false;
        try { return TryGrant(out _); }
        finally { _gate.Release(); }
    }

    /// <summary>Acquires a permit, waiting until the oldest permit in the window expires when necessary.</summary>
    /// <exception cref="OperationCanceledException">The wait was cancelled; no permit is consumed.</exception>
    public async Task AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (!TryGrant(out var wait)) await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryGrant(out TimeSpan wait)
    {
        lock (_sync)
        {
            var now = _time.GetTimestamp();
            Prune(now);
            if (_grants.Count < Limit.PermitLimit)
            {
                _grants.Enqueue(now);
                wait = TimeSpan.Zero;
                return true;
            }
            wait = Limit.Window - _time.GetElapsedTime(_grants.Peek(), now);
            if (wait <= TimeSpan.Zero) wait = TimeSpan.FromTicks(1);
            return false;
        }
    }

    private void Prune(long now)
    {
        while (_grants.Count > 0 && _time.GetElapsedTime(_grants.Peek(), now) >= Limit.Window) _grants.Dequeue();
    }
}
