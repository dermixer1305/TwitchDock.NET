using System.Text.Json.Serialization;
using TwitchSdk.Core;

namespace TwitchSdk.Authentication;

public sealed class OAuthTokenResponse
{
    [JsonPropertyName("access_token")] public required string AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public int ExpiresIn { get; init; }
    public IReadOnlyList<string> Scope { get; init; } = [];
    public string TokenType { get; init; } = "bearer";
    /// <summary>Present for authorization-code grants that requested the openid scope. Validate with ValidateIdTokenAsync.</summary>
    public string? IdToken { get; init; }
    [JsonIgnore] public TwitchTokenKind Kind { get; internal set; }
    [JsonIgnore] public string? ClientId { get; internal set; }
    public override string ToString() => "OAuthTokenResponse [redacted]";
}

public sealed class TokenValidation
{
    public required string ClientId { get; init; }
    public string? Login { get; init; }
    public string? UserId { get; init; }
    public IReadOnlyList<string> Scopes { get; init; } = [];
    public int ExpiresIn { get; init; }
    public AccessToken ToAccessToken(string value, TimeProvider? timeProvider = null)
        => new(value, (timeProvider ?? TimeProvider.System).GetUtcNow().AddSeconds(ExpiresIn), Scopes,
            string.IsNullOrEmpty(UserId) ? TwitchTokenKind.App : TwitchTokenKind.User, UserId, ClientId);
}

public sealed class DeviceAuthorization
{
    public required string DeviceCode { get; init; }
    public required string UserCode { get; init; }
    public required string VerificationUri { get; init; }
    public int ExpiresIn { get; init; }
    public int Interval { get; init; }
    public override string ToString() => "DeviceAuthorization [redacted]";
}

internal sealed class IdTokenHeader
{
    public string? Alg { get; init; }
    public string? Kid { get; init; }
}

internal sealed class IdTokenPayload
{
    public string? Iss { get; init; }
    public string? Sub { get; init; }
    public System.Text.Json.JsonElement Aud { get; init; }
    public long Exp { get; init; }
    public long Iat { get; init; }
    public string? Nonce { get; init; }
    public string? Azp { get; init; }
    public string? AtHash { get; init; }
    public string? Email { get; init; }
    public bool? EmailVerified { get; init; }
    public string? Picture { get; init; }
    public string? PreferredUsername { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

internal sealed class JsonWebKeySet
{
    public IReadOnlyList<JsonWebKey> Keys { get; init; } = [];
}

internal sealed class JsonWebKey
{
    public string? Kty { get; init; }
    public string? Kid { get; init; }
    public string? Use { get; init; }
    public string? N { get; init; }
    public string? E { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(OAuthTokenResponse))]
[JsonSerializable(typeof(TokenValidation))]
[JsonSerializable(typeof(DeviceAuthorization))]
[JsonSerializable(typeof(OpenIdUserInfo))]
[JsonSerializable(typeof(IdTokenHeader))]
[JsonSerializable(typeof(IdTokenPayload))]
[JsonSerializable(typeof(JsonWebKeySet))]
internal partial class OAuthJsonContext : JsonSerializerContext;
