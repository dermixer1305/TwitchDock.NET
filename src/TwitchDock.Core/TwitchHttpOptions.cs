using System.Net;

namespace TwitchDock.Core;

public sealed class TwitchHttpOptions
{
    /// <summary>The default <see cref="MaxResponseContentBytes"/>: 32 MiB.</summary>
    public const long DefaultMaxResponseContentBytes = 32L * 1024 * 1024;

    public required string ClientId { get; init; }
    public Uri BaseAddress { get; init => field = value ?? new("https://api.twitch.tv/helix/"); } = new("https://api.twitch.tv/helix/");
    public int MaxRateLimitRetries { get; init; } = 2;
    public int MaxTransientRetries { get; init; } = 2;
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan FallbackRetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The largest response body read from Twitch. Larger responses, by Content-Length or while streaming a chunked body,
    /// fail with <see cref="InvalidDataException"/>.
    /// </summary>
    public long MaxResponseContentBytes { get; init; } = DefaultMaxResponseContentBytes;

    /// <summary>Throws when a value is invalid, naming the offending property. Call it to fail fast at startup.</summary>
    /// <exception cref="ArgumentException">ClientId or BaseAddress is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A retry or size limit is out of range.</exception>
    public void EnsureValid() => Validate();

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ClientId);
        // Plain HTTP is only accepted for loopback hosts, such as the Twitch CLI mock API during local testing.
        if (!BaseAddress.IsAbsoluteUri || !(BaseAddress.Scheme == Uri.UriSchemeHttps || (BaseAddress.Scheme == Uri.UriSchemeHttp && IsLoopbackHost(BaseAddress)))
            || !BaseAddress.AbsolutePath.EndsWith('/') || !string.IsNullOrEmpty(BaseAddress.Query) || !string.IsNullOrEmpty(BaseAddress.UserInfo))
            throw new ArgumentException("BaseAddress must be an HTTPS (or loopback HTTP) directory URI without credentials or query parameters.", nameof(BaseAddress));
        if (MaxRateLimitRetries < 0) throw new ArgumentOutOfRangeException(nameof(MaxRateLimitRetries), MaxRateLimitRetries, "Must not be negative.");
        if (MaxTransientRetries < 0) throw new ArgumentOutOfRangeException(nameof(MaxTransientRetries), MaxTransientRetries, "Must not be negative.");
        if (MaxRetryDelay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(MaxRetryDelay), MaxRetryDelay, "Must be positive.");
        if (FallbackRetryDelay <= TimeSpan.Zero || FallbackRetryDelay > MaxRetryDelay)
            throw new ArgumentOutOfRangeException(nameof(FallbackRetryDelay), FallbackRetryDelay, "Must be positive and not exceed MaxRetryDelay.");
        if (MaxResponseContentBytes <= 0) throw new ArgumentOutOfRangeException(nameof(MaxResponseContentBytes), MaxResponseContentBytes, "Must be positive.");
    }

    /// <summary>
    /// An IP-literal loopback address or the host name localhost (Uri also normalizes "loopback" to it), the same explicit rule
    /// as the EventSub WebSocket endpoint, so the bearer token is only sent in clear text to this machine.
    /// </summary>
    private static bool IsLoopbackHost(Uri uri) => uri.HostNameType switch
    {
        UriHostNameType.IPv4 or UriHostNameType.IPv6 => IPAddress.TryParse(uri.DnsSafeHost, out var address) && IPAddress.IsLoopback(address),
        UriHostNameType.Dns => string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };
}
