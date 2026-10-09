using TwitchSdk.Core;

namespace TwitchSdk.Helix.Extensions;

/// <summary>
/// Issues a freshly signed Extension Backend Service JWT (<c>role: "external"</c>, <c>user_id</c> = extension owner) for every request.
/// Use it for a <see cref="TwitchHttpClient"/> dedicated to JWT-authenticated extension endpoints and construct that client with
/// <see cref="TwitchHttpOptions.ClientId"/> set to <see cref="ExtensionClientId"/>. OAuth app/user token endpoints need a separate client.
/// </summary>
/// <remarks>
/// Tokens have <see cref="TwitchTokenKind.Unknown"/> kind, so local scope preflight defers to Twitch. A refresh after HTTP 401 issues a new JWT.
/// When <see cref="Clients.ExtensionsClient"/> sends a chat or PubSub message, this provider adds the <c>channel_id</c> and
/// <c>pubsub_perms</c> claims those endpoints require for that request.
/// </remarks>
public sealed class ExtensionJwtTokenProvider : IAccessTokenProvider
{
    private readonly TimeProvider _time;
    private ExtensionSecret _secret;

    public ExtensionJwtTokenProvider(string extensionClientId, string ownerUserId, ExtensionSecret secret, TimeSpan? lifetime = null, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserId);
        ArgumentNullException.ThrowIfNull(secret);
        Lifetime = ExtensionJwt.ValidateLifetime(lifetime);
        ExtensionClientId = extensionClientId;
        OwnerUserId = ownerUserId;
        _secret = secret;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The extension's client ID; it must equal the transport's configured client ID.</summary>
    public string ExtensionClientId { get; }

    /// <summary>The extension owner's user ID, written to the <c>user_id</c> claim.</summary>
    public string OwnerUserId { get; }

    public TimeSpan Lifetime { get; }

    /// <summary>Signs subsequent tokens with a rotated secret, for example once a secret from Create Extension Secret is active.</summary>
    public void ReplaceSecret(ExtensionSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        Volatile.Write(ref _secret, secret);
    }

    public ValueTask<AccessToken> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var claims = ExtensionJwtRequestClaims.Current;
        var expiresAt = ExtensionJwt.ComputeExpiry(Lifetime, _time);
        var value = ExtensionJwt.Create(Volatile.Read(ref _secret), OwnerUserId, claims?.ChannelId, claims?.SendTargets, expiresAt);
        return ValueTask.FromResult(new AccessToken(value, expiresAt, kind: TwitchTokenKind.Unknown, userId: OwnerUserId, clientId: ExtensionClientId));
    }

    /// <summary>A JWT cannot be refreshed; this issues a new token with a new expiry.</summary>
    public ValueTask<AccessToken> RefreshTokenAsync(AccessToken rejectedToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rejectedToken);
        return GetTokenAsync(cancellationToken);
    }

    public override string ToString() => $"ExtensionJwtTokenProvider {{ ExtensionClientId = {ExtensionClientId}, OwnerUserId = {OwnerUserId}, Secret = [redacted] }}";
}

/// <summary>Per-request claims for endpoints whose JWT must name a channel. Scoped to one asynchronous request flow.</summary>
internal sealed record ExtensionJwtRequestClaims(string ChannelId, IReadOnlyList<string>? SendTargets)
{
    private static readonly AsyncLocal<ExtensionJwtRequestClaims?> CurrentClaims = new();

    public static ExtensionJwtRequestClaims? Current => CurrentClaims.Value;

    /// <summary>Runs the request with these claims. The assignment happens inside an async method, so it never flows back to the caller.</summary>
    public async Task RunAsync(Func<Task> send)
    {
        CurrentClaims.Value = this;
        await send().ConfigureAwait(false);
    }
}
