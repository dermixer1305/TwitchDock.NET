namespace TwitchSdk.EventSub;

/// <summary>Bounded, process-local duplicate suppression. Use a shared durable inbox across webhook replicas.</summary>
public sealed class MessageDeduplicator
{
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Id, DateTimeOffset Expires)>> _seen = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Id, DateTimeOffset Expires)> _expiry = new();
    private readonly TimeProvider _time;
    private readonly TimeSpan _retention;
    private readonly int _capacity;

    public MessageDeduplicator(TimeSpan? retention = null, int capacity = 10000, TimeProvider? timeProvider = null)
    {
        _retention = retention ?? TimeSpan.FromMinutes(10);
        if (_retention <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retention));
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _time = timeProvider ?? TimeProvider.System;
    }

    public bool TryAdd(string messageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            while (_expiry.First is { } first && first.Value.Expires <= now) { _expiry.RemoveFirst(); _seen.Remove(first.Value.Id); }
            if (_seen.ContainsKey(messageId)) return false;
            if (_seen.Count >= _capacity) throw new InvalidOperationException("EventSub deduplication capacity exhausted. Increase capacity or use a durable inbox.");
            var expires = now + _retention;
            _seen.Add(messageId, _expiry.AddLast((messageId, expires)));
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
}
