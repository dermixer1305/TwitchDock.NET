namespace TwitchDock.Core;

public enum TwitchTokenKind { Unknown, User, App }

/// <summary>A credential snapshot. Token values are deliberately excluded from ToString.</summary>
public sealed class AccessToken
{
    public AccessToken(string value, DateTimeOffset? expiresAt = null, IEnumerable<string>? scopes = null,
        TwitchTokenKind kind = TwitchTokenKind.Unknown, string? userId = null, string? clientId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
        ExpiresAt = expiresAt;
        Scopes = Array.AsReadOnly((scopes ?? []).Distinct(StringComparer.Ordinal).ToArray());
        ScopesKnown = scopes is not null;
        Kind = kind;
        UserId = userId;
        ClientId = clientId;
    }

    public string Value { get; }
    public DateTimeOffset? ExpiresAt { get; }
    public IReadOnlyList<string> Scopes { get; }
    public bool ScopesKnown { get; }
    public TwitchTokenKind Kind { get; }
    public string? UserId { get; }
    public string? ClientId { get; }
    public bool HasScopes(params string[] required) => required.All(s => Scopes.Contains(s, StringComparer.Ordinal));
    public override string ToString() => "AccessToken [redacted]";
}

public interface IAccessTokenProvider
{
    ValueTask<AccessToken> GetTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>Refreshes a rejected token, reusing a newer token if another request already refreshed it.</summary>
    ValueTask<AccessToken> RefreshTokenAsync(AccessToken rejectedToken, CancellationToken cancellationToken = default);
}

public interface ITokenMetadataSink
{
    /// <summary>Updates metadata only if the validated token is still current. Returns false after concurrent rotation.</summary>
    ValueTask<bool> UpdateMetadataAsync(AccessToken validatedToken, CancellationToken cancellationToken = default);
}

public sealed class StaticAccessTokenProvider(AccessToken token) : IAccessTokenProvider
{
    private readonly AccessToken _token = token ?? throw new ArgumentNullException(nameof(token));
    public ValueTask<AccessToken> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_token);
    }
    public ValueTask<AccessToken> RefreshTokenAsync(AccessToken rejectedToken, CancellationToken cancellationToken = default)
        => GetTokenAsync(cancellationToken);
}
