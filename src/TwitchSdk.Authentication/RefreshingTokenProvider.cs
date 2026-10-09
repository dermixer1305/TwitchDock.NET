using TwitchSdk.Core;

namespace TwitchSdk.Authentication;

/// <summary>Serializes refresh and rotation for one authorization. Share one instance across its clients.</summary>
public sealed class RefreshingTokenProvider : IAccessTokenProvider, ITokenMetadataSink, IDisposable
{
    private readonly Func<string?, CancellationToken, Task<OAuthTokenResponse>> _acquire;
    private readonly Func<OAuthTokenResponse, CancellationToken, Task>? _persist;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AccessToken? _token;
    private string? _refreshToken;

    public RefreshingTokenProvider(Func<string?, CancellationToken, Task<OAuthTokenResponse>> acquire,
        OAuthTokenResponse? initialToken = null,
        Func<OAuthTokenResponse, CancellationToken, Task>? persist = null, TimeProvider? timeProvider = null)
    {
        _acquire = acquire ?? throw new ArgumentNullException(nameof(acquire));
        _persist = persist;
        _time = timeProvider ?? TimeProvider.System;
        if (initialToken is not null) SetToken(initialToken);
    }

    public async ValueTask<AccessToken> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var token = Volatile.Read(ref _token);
        return token ?? await AcquireAsync(null, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<AccessToken> RefreshTokenAsync(AccessToken rejectedToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rejectedToken);
        return AcquireAsync(rejectedToken, cancellationToken);
    }

    private async ValueTask<AccessToken> AcquireAsync(AccessToken? rejected, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_token is not null && (rejected is null || _token.Value != rejected.Value)) return _token;
            var response = await _acquire(_refreshToken, ct).ConfigureAwait(false);
            // Keep rotated credentials in memory even if durable persistence fails; never reuse the old refresh token.
            SetToken(response);
            if (_persist is not null) await _persist(response, ct).ConfigureAwait(false);
            return _token!;
        }
        finally { _gate.Release(); }
    }

    private void SetToken(OAuthTokenResponse response)
    {
        var next = new AccessToken(response.AccessToken, _time.GetUtcNow().AddSeconds(response.ExpiresIn), response.Scope, response.Kind, clientId: response.ClientId);
        _refreshToken = response.RefreshToken ?? _refreshToken;
        Volatile.Write(ref _token, next);
    }

    public async ValueTask<bool> UpdateMetadataAsync(AccessToken validatedToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validatedToken);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_token?.Value != validatedToken.Value) return false;
            Volatile.Write(ref _token, validatedToken);
            return true;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Dispose only after all clients using this provider have stopped.</summary>
    public void Dispose() => _gate.Dispose();
}
