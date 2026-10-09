using System.Security.Cryptography;

namespace TwitchSdk.Authentication;

/// <summary>
/// Caches Twitch's OpenID Connect signing keys across <see cref="TwitchOAuthClient"/> instances (DI creates the client per use).
/// Keys are refreshed after one hour; an unknown key ID triggers at most one refetch every five minutes; one fetch runs at a
/// time (bounded by 30 seconds) and concurrent validations share its result. A failed or empty fetch is remembered for 30 seconds: validations that need keys
/// then fail with <see cref="TwitchIdTokenException"/> without contacting Twitch, except that keys older than an hour may
/// still verify tokens while such a refresh is held back.
/// </summary>
public sealed class OpenIdSigningKeyCache
{
    internal static readonly TimeSpan MaxAge = TimeSpan.FromHours(1);
    internal static readonly TimeSpan UnknownKeyRefetchInterval = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan FailureRetention = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private readonly TimeProvider _time;
    private KeySet? _keys;
    private DateTimeOffset? _failedAt;

    /// <param name="timeProvider">Clock for key age, refetch rate limiting and failure retention.</param>
    public OpenIdSigningKeyCache(TimeProvider? timeProvider = null) => _time = timeProvider ?? TimeProvider.System;

    /// <summary>The process-wide cache used when a client is created without one.</summary>
    public static OpenIdSigningKeyCache Shared { get; } = new();

    internal async Task<RSAParameters> GetKeyAsync(string? kid, Func<CancellationToken, Task<IReadOnlyList<(string? Kid, RSAParameters Key)>>> fetch,
        CancellationToken ct)
    {
        var keys = Volatile.Read(ref _keys);
        if (keys is not null && !keys.IsExpired(_time.GetUtcNow()) && keys.TryFind(kid, out var cached)) return cached;
        await _refresh.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Another caller may have refreshed while this one waited (single flight).
            var now = _time.GetUtcNow();
            keys = _keys;
            var expired = keys is null || keys.IsExpired(now);
            if (!expired && keys!.TryFind(kid, out var current)) return current;
            if (_failedAt is { } failedAt && now - failedAt < FailureRetention)
            {
                // The refresh is held back after a failure; only now may expired keys still verify a token.
                if (expired && keys is not null && keys.TryFind(kid, out var stale)) return stale;
                throw new TwitchIdTokenException("Twitch's OpenID signing keys are temporarily unavailable.");
            }
            if (!expired && now - keys!.FetchedAt < UnknownKeyRefetchInterval)
                throw new TwitchIdTokenException("No Twitch signing key matches the ID token.");
            IReadOnlyList<(string? Kid, RSAParameters Key)> fetched;
            try
            {
                // The fetch holds the refresh gate for every waiting validation, so it is bounded independently of HttpClient.
                using var timeout = new CancellationTokenSource(FetchTimeout, _time);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                fetched = await fetch(linked.Token).ConfigureAwait(false);
                // An empty set would hide the cached keys until the next refetch; keep them and back off instead.
                if (fetched.Count == 0) throw new InvalidDataException("Twitch published no usable RSA signing keys.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Stamp the failure when it happened: a slow failure must still hold back the validations queued behind it.
                _failedAt = _time.GetUtcNow();
                throw new TwitchIdTokenException("Twitch's OpenID signing keys could not be fetched.", ex);
            }
            _failedAt = null;
            now = _time.GetUtcNow();
            keys = new KeySet(fetched, now);
            Volatile.Write(ref _keys, keys);
            return keys.TryFind(kid, out var fresh) ? fresh : throw new TwitchIdTokenException("No Twitch signing key matches the ID token.");
        }
        finally { _refresh.Release(); }
    }

    private sealed class KeySet(IReadOnlyList<(string? Kid, RSAParameters Key)> keys, DateTimeOffset fetchedAt)
    {
        public DateTimeOffset FetchedAt { get; } = fetchedAt;

        public bool IsExpired(DateTimeOffset now) => now - FetchedAt >= MaxAge;

        public bool TryFind(string? kid, out RSAParameters key)
        {
            var match = kid is null ? (keys.Count == 1 ? keys[0] : default) : keys.FirstOrDefault(k => k.Kid == kid);
            key = match.Key;
            return match.Key.Modulus is not null;
        }
    }
}
