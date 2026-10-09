namespace TwitchDock.Tests;

internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = DateTimeOffset.Parse("2026-10-09T12:00:00Z");
    public override DateTimeOffset GetUtcNow() { lock (_gate) return _now; }
    public override long GetTimestamp() => GetUtcNow().UtcTicks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public int TimerCount { get { lock (_gate) return _timers.Count; } }
    /// <summary>The time until the earliest pending timer fires; null without timers.</summary>
    public TimeSpan? NextTimerDueIn { get { lock (_gate) return _timers.Count == 0 ? null : _timers.Min(t => t.Due) - _now; } }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan duration)
    {
        List<ManualTimer> due;
        lock (_gate)
        {
            _now += duration;
            due = _timers.Where(t => t.Due <= _now).ToList();
            foreach (var timer in due)
            {
                if (timer.Period == Timeout.InfiniteTimeSpan) _timers.Remove(timer);
                else timer.Due = _now + timer.Period;
            }
        }
        foreach (var timer in due) timer.Fire();
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset Due { get; set; }
        public TimeSpan Period { get; private set; }
        private bool _disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                if (_disposed) return false;
                owner._timers.Remove(this);
                Period = period;
                if (dueTime != Timeout.InfiniteTimeSpan) { Due = owner._now + dueTime; owner._timers.Add(this); }
                return true;
            }
        }
        public void Fire() { if (!_disposed) callback(state); }
        public void Dispose() { lock (owner._gate) { _disposed = true; owner._timers.Remove(this); } }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    public static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(1, timeout.Token);
    }
}
