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

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(OAuthTokenResponse))]
[JsonSerializable(typeof(TokenValidation))]
[JsonSerializable(typeof(DeviceAuthorization))]
internal partial class OAuthJsonContext : JsonSerializerContext;
