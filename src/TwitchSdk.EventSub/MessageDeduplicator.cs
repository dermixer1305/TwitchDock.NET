namespace TwitchSdk.EventSub;

/// <summary>
/// Bounded, process-local duplicate suppression. Use a shared durable inbox across webhook replicas.
/// When full, the oldest retained ID is evicted (counted by <see cref="EvictedCount"/>) unless fail-closed mode is enabled.
/// </summary>
public sealed class MessageDeduplicator
{
    /// <summary>The default number of retained message IDs.</summary>
    public const int DefaultCapacity = 100_000;

    /// <summary>The default retention: the webhook freshness window (10 minutes) plus the accepted future clock skew (1 minute).</summary>
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromMinutes(11);

    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Id, DateTimeOffset Expires)>> _seen = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Id, DateTimeOffset Expires)> _expiry = new();
    private readonly TimeProvider _time;
    private readonly TimeSpan _retention;
    private readonly int _capacity;
    private readonly bool _throwWhenFull;
    private long _evicted;

    /// <param name="retention">How long an accepted ID is remembered; defaults to <see cref="DefaultRetention"/>.</param>
    /// <param name="capacity">The maximum number of retained IDs; defaults to <see cref="DefaultCapacity"/>.</param>
    /// <param name="timeProvider">Clock for expiry.</param>
    /// <param name="throwWhenFull">
    /// Fail closed: throw <see cref="EventSubDeduplicationException"/> instead of evicting the oldest unexpired ID when full.
    /// The WebSocket client then stops and the webhook handler answers 503.
    /// </param>
    public MessageDeduplicator(TimeSpan? retention = null, int capacity = DefaultCapacity, TimeProvider? timeProvider = null, bool throwWhenFull = false)
    {
        _retention = retention ?? DefaultRetention;
        if (_retention <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retention));
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _time = timeProvider ?? TimeProvider.System;
        _throwWhenFull = throwWhenFull;
    }

    /// <summary>The number of unexpired IDs evicted to make room. A growing value means replay protection is shorter than the retention.</summary>
    public long EvictedCount => Interlocked.Read(ref _evicted);

    /// <summary>Records the ID. Returns false when it was already seen within the retention.</summary>
    /// <exception cref="EventSubDeduplicationException">Fail-closed mode is enabled and the capacity is exhausted.</exception>
    public bool TryAdd(string messageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            while (_expiry.First is { } first && first.Value.Expires <= now) RemoveFirst();
            if (_seen.ContainsKey(messageId)) return false;
            if (_seen.Count >= _capacity)
            {
                if (_throwWhenFull) throw new EventSubDeduplicationException("EventSub deduplication capacity exhausted. Increase capacity or use a durable inbox.");
                RemoveFirst();
                Interlocked.Increment(ref _evicted);
            }
            _seen.Add(messageId, _expiry.AddLast((messageId, now + _retention)));
            return true;
        }
    }

    /// <summary>Releases a failed delivery so a later retry can be processed.</summary>
    public void Remove(string messageId)
    {
        lock (_gate)
        {
            if (_seen.Remove(messageId, out var node)) _expiry.Remove(node);
        }
    }

    private void RemoveFirst()
    {
        var first = _expiry.First!;
        _expiry.RemoveFirst();
        _seen.Remove(first.Value.Id);
    }
}

/// <summary>A fail-closed <see cref="MessageDeduplicator"/> could not accept another message ID.</summary>
public sealed class EventSubDeduplicationException(string message) : InvalidOperationException(message);
