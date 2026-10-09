using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwitchSdk.Authentication;

public enum OpenIdResponseType
{
    /// <summary>Authorization code flow; the token response then includes an id_token.</summary>
    Code,
    Token,
    IdToken,
    TokenIdToken,
}

/// <summary>Optional claims to request: email, email_verified, picture, preferred_username, updated_at.</summary>
public sealed class OpenIdClaimsRequest
{
    public IReadOnlyList<string> IdToken { get; init; } = [];
    public IReadOnlyList<string> UserInfo { get; init; } = [];
}

/// <summary>The UserInfo endpoint response. Optional claims are present only when requested and granted.</summary>
public sealed class OpenIdUserInfo
{
    public string? Aud { get; init; }
    public long? Exp { get; init; }
    public long? Iat { get; init; }
    public string? Iss { get; init; }
    public required string Sub { get; init; }
    public string? Email { get; init; }
    public bool? EmailVerified { get; init; }
    public string? Picture { get; init; }
    public string? PreferredUsername { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>Claims of an ID token whose signature, issuer, audience, lifetime and nonce were verified.</summary>
public sealed class OpenIdTokenClaims
{
    public required string Subject { get; init; }
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset IssuedAt { get; init; }
    public string? Nonce { get; init; }
    public string? AuthorizedParty { get; init; }
    public string? AccessTokenHash { get; init; }
    public string? Email { get; init; }
    public bool? EmailVerified { get; init; }
    public string? Picture { get; init; }
    public string? PreferredUsername { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>An ID token failed validation. Never trust its claims.</summary>
public sealed class TwitchIdTokenException(string message) : Exception(message);

public sealed partial class TwitchOAuthClient
{
    public const string OpenIdIssuer = "https://id.twitch.tv/oauth2";
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan KeyRefreshInterval = TimeSpan.FromMinutes(5);
    private SigningKeys? _signingKeys;

    /// <summary>Creates an OpenID Connect authorization URI. The openid scope is added when missing; store state and nonce in the session.</summary>
    public static Uri CreateOpenIdAuthorizationUri(string clientId, Uri redirectUri, IEnumerable<string> scopes, string state, string nonce,
        OpenIdResponseType responseType = OpenIdResponseType.Code, OpenIdClaimsRequest? claims = null, bool forceVerify = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        ArgumentNullException.ThrowIfNull(scopes);
        ValidateRedirect(redirectUri);
        var scopeList = scopes.ToList();
        if (!scopeList.Contains("openid", StringComparer.Ordinal)) scopeList.Insert(0, "openid");
        var values = new Dictionary<string, string>
        {
            ["client_id"] = clientId, ["redirect_uri"] = redirectUri.AbsoluteUri,
            ["response_type"] = responseType switch
            {
                OpenIdResponseType.Code => "code",
                OpenIdResponseType.Token => "token",
                OpenIdResponseType.IdToken => "id_token",
                OpenIdResponseType.TokenIdToken => "token id_token",
                _ => throw new ArgumentOutOfRangeException(nameof(responseType)),
            },
            ["scope"] = string.Join(' ', scopeList), ["state"] = state, ["nonce"] = nonce, ["force_verify"] = forceVerify ? "true" : "false",
        };
        if (claims is not null && (claims.IdToken.Count > 0 || claims.UserInfo.Count > 0)) values.Add("claims", SerializeClaims(claims));
        return BuildAuthorizeUri(values);
    }

    /// <summary>Reads the user's claims with an access token that was granted the openid scope.</summary>
    public async Task<OpenIdUserInfo> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Authority, "userinfo"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(response, OAuthJsonContext.Default.OpenIdUserInfo, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the RS256 signature against Twitch's published keys and checks issuer, audience, expiry, issue time and nonce.
    /// Pass the nonce stored for the authorization request whenever one was sent.
    /// </summary>
    public async Task<OpenIdTokenClaims> ValidateIdTokenAsync(string idToken, string clientId, string? expectedNonce = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        var parts = idToken.Split('.');
        if (parts.Length != 3) throw new TwitchIdTokenException("The ID token is not a compact JWS.");
        var header = DeserializeSegment(parts[0], OAuthJsonContext.Default.IdTokenHeader);
        if (header.Alg != "RS256") throw new TwitchIdTokenException("The ID token must be signed with RS256.");
        var key = await GetSigningKeyAsync(header.Kid, cancellationToken).ConfigureAwait(false);
        using (var rsa = RSA.Create(key))
        {
            if (!rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), DecodeSegment(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                throw new TwitchIdTokenException("The ID token signature is invalid.");
        }
        var payload = DeserializeSegment(parts[1], OAuthJsonContext.Default.IdTokenPayload);
        if (payload.Iss != OpenIdIssuer) throw new TwitchIdTokenException("The ID token was not issued by Twitch.");
        var audiences = ReadAudiences(payload.Aud);
        if (!audiences.Contains(clientId, StringComparer.Ordinal) || (audiences.Count > 1 && payload.Azp != clientId))
            throw new TwitchIdTokenException("The ID token was issued for a different client.");
        var now = _time.GetUtcNow();
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.Exp);
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(payload.Iat);
        if (expiresAt + ClockSkew <= now) throw new TwitchIdTokenException("The ID token has expired.");
        if (issuedAt - ClockSkew > now) throw new TwitchIdTokenException("The ID token was issued in the future.");
        if (expectedNonce is not null && !ValidateState(expectedNonce, payload.Nonce)) throw new TwitchIdTokenException("The ID token nonce does not match.");
        if (string.IsNullOrEmpty(payload.Sub)) throw new TwitchIdTokenException("The ID token has no subject.");
        return new()
        {
            Subject = payload.Sub, Issuer = payload.Iss, Audience = clientId, ExpiresAt = expiresAt, IssuedAt = issuedAt, Nonce = payload.Nonce,
            AuthorizedParty = payload.Azp, AccessTokenHash = payload.AtHash, Email = payload.Email, EmailVerified = payload.EmailVerified,
            Picture = payload.Picture, PreferredUsername = payload.PreferredUsername, UpdatedAt = payload.UpdatedAt,
        };
    }

    private async Task<RSAParameters> GetSigningKeyAsync(string? kid, CancellationToken ct)
    {
        var keys = Volatile.Read(ref _signingKeys);
        var now = _time.GetUtcNow();
        if (keys is not null && keys.TryFind(kid, out var cached)) return cached;
        // Unknown key IDs trigger a refresh after key rotation, rate limited to avoid fetch storms.
        if (keys is null || now - keys.FetchedAt >= KeyRefreshInterval)
        {
            using var response = await _http.GetAsync(new Uri(Authority, "keys"), ct).ConfigureAwait(false);
            var set = await ReadAsync(response, OAuthJsonContext.Default.JsonWebKeySet, ct).ConfigureAwait(false);
            keys = new SigningKeys(set.Keys.Where(k => k.Kty == "RSA" && k.Use is null or "sig" && !string.IsNullOrEmpty(k.N) && !string.IsNullOrEmpty(k.E))
                .Select(k => (k.Kid, new RSAParameters { Modulus = DecodeSegment(k.N!), Exponent = DecodeSegment(k.E!) })).ToList(), now);
            Volatile.Write(ref _signingKeys, keys);
            if (keys.TryFind(kid, out var fresh)) return fresh;
        }
        throw new TwitchIdTokenException("No Twitch signing key matches the ID token.");
    }

    private static IReadOnlyList<string> ReadAudiences(JsonElement aud) => aud.ValueKind switch
    {
        JsonValueKind.String => [aud.GetString()!],
        JsonValueKind.Array => aud.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString()!).ToArray(),
        _ => [],
    };

    private static string SerializeClaims(OpenIdClaimsRequest claims)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            WriteClaims(writer, "id_token", claims.IdToken);
            WriteClaims(writer, "userinfo", claims.UserInfo);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteClaims(Utf8JsonWriter writer, string name, IReadOnlyList<string> claims)
    {
        if (claims.Count == 0) return;
        writer.WriteStartObject(name);
        foreach (var claim in claims) { ArgumentException.ThrowIfNullOrWhiteSpace(claim); writer.WriteNull(claim); }
        writer.WriteEndObject();
    }

    private static T DeserializeSegment<T>(string segment, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        try { return JsonSerializer.Deserialize(DecodeSegment(segment), type) ?? throw new TwitchIdTokenException("The ID token contains an empty segment."); }
        catch (JsonException) { throw new TwitchIdTokenException("The ID token contains malformed JSON."); }
    }

    private static byte[] DecodeSegment(string segment)
    {
        var base64 = segment.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        try { return Convert.FromBase64String(base64); }
        catch (FormatException) { throw new TwitchIdTokenException("The ID token is not valid base64url."); }
    }

    private sealed class SigningKeys(IReadOnlyList<(string? Kid, RSAParameters Key)> keys, DateTimeOffset fetchedAt)
    {
        public DateTimeOffset FetchedAt { get; } = fetchedAt;

        public bool TryFind(string? kid, out RSAParameters key)
        {
            var match = kid is null ? (keys.Count == 1 ? keys[0] : default) : keys.FirstOrDefault(k => k.Kid == kid);
            key = match.Key;
            return match.Key.Modulus is not null;
        }
    }
}
