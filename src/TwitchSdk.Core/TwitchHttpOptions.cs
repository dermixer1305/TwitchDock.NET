namespace TwitchSdk.Core;

public sealed class TwitchHttpOptions
{
    public required string ClientId { get; init; }
    public Uri BaseAddress { get; init; } = new("https://api.twitch.tv/helix/");
    public int MaxRateLimitRetries { get; init; } = 2;
    public int MaxTransientRetries { get; init; } = 2;
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan FallbackRetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ClientId);
        // Plain HTTP is only accepted for loopback hosts, such as the Twitch CLI mock API during local testing.
        if (!BaseAddress.IsAbsoluteUri || !(BaseAddress.Scheme == Uri.UriSchemeHttps || (BaseAddress.Scheme == Uri.UriSchemeHttp && BaseAddress.IsLoopback))
            || !BaseAddress.AbsolutePath.EndsWith('/') || !string.IsNullOrEmpty(BaseAddress.Query) || !string.IsNullOrEmpty(BaseAddress.UserInfo))
            throw new ArgumentException("BaseAddress must be an HTTPS (or loopback HTTP) directory URI without credentials or query parameters.");
        if (MaxRateLimitRetries < 0 || MaxTransientRetries < 0 || MaxRetryDelay <= TimeSpan.Zero || FallbackRetryDelay <= TimeSpan.Zero || FallbackRetryDelay > MaxRetryDelay)
            throw new ArgumentOutOfRangeException(nameof(MaxRateLimitRetries));
    }
}
