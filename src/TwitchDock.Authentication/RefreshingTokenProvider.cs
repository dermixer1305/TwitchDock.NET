using System.Runtime.ExceptionServices;
using TwitchDock.Core;

namespace TwitchDock.Authentication;

/// <summary>
/// Serializes refresh and rotation for one authorization. Share one instance across its clients.
/// A caller's cancellation token only limits its wait for the gate: once started, acquisition and persistence run to completion
/// (each receives a token cancelled after 30 seconds, surfacing as <see cref="TimeoutException"/>) so a rotated refresh token is
/// never lost. A failed acquisition is rethrown to callers for 5 seconds instead of retrying the token endpoint immediately.
/// </summary>
public sealed class RefreshingTokenProvider : IAccessTokenProvider, ITokenMetadataSink, IDisposable
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(5);
    private readonly Func<string?, CancellationToken, Task<OAuthTokenResponse>> _acquire;
    private readonly Func<OAuthTokenResponse, CancellationToken, Task>? _persist;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AccessToken? _token;
    private string? _refreshToken;
    private ExceptionDispatchInfo? _failure;
    private DateTimeOffset _failureUntil;

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
        // Only the wait honors the caller's token; see the class remarks.
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_token is not null && (rejected is null || _token.Value != rejected.Value)) return _token;
            if (_failure is not null && _time.GetUtcNow() < _failureUntil) _failure.Throw();
            OAuthTokenResponse response;
            try { response = await RunBoundedAsync(timeout => _acquire(_refreshToken, timeout), "acquisition").ConfigureAwait(false); }
            catch (Exception ex)
            {
                // No caller token reaches the delegate, so a cancellation from it (for example HttpClient.Timeout) is a timeout.
                // Replaying it as OperationCanceledException would look like shutdown to callers whose tokens were not cancelled.
                var failure = ex is OperationCanceledException ? new TimeoutException("Token acquisition was cancelled or timed out.", ex) : ex;
                _failure = ExceptionDispatchInfo.Capture(failure);
                _failureUntil = _time.GetUtcNow() + FailureBackoff;
                if (ReferenceEquals(failure, ex)) throw;
                throw failure;
            }
            _failure = null;
            // Keep rotated credentials in memory even if durable persistence fails; never reuse the old refresh token.
            SetToken(response);
            if (_persist is not null) await RunBoundedAsync(async timeout => { await _persist(response, timeout).ConfigureAwait(false); return response; }, "persistence").ConfigureAwait(false);
            return _token!;
        }
        finally { _gate.Release(); }
    }

    private async Task<T> RunBoundedAsync<T>(Func<CancellationToken, Task<T>> operation, string name)
    {
        using var timeout = new CancellationTokenSource(OperationTimeout, _time);
        try { return await operation(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"Token {name} did not finish within {OperationTimeout.TotalSeconds} seconds.", ex);
        }
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
