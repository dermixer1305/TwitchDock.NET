using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwitchDock.Authentication;

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
    public IReadOnlyList<string> IdToken { get; init => field = value ?? []; } = [];
    public IReadOnlyList<string> UserInfo { get; init => field = value ?? []; } = [];
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

/// <summary>
/// Claims of an ID token whose signature, issuer, audience, authorized party and lifetime were verified, as well as the nonce
/// (unless validated with ValidateIdTokenWithoutNonceAsync) and the at_hash (when an access token was supplied).
/// </summary>
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
public sealed class TwitchIdTokenException : Exception
{
    public TwitchIdTokenException(string message) : base(message) { }
    public TwitchIdTokenException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed partial class TwitchOAuthClient
{
    public const string OpenIdIssuer = "https://id.twitch.tv/oauth2";
    private const int MaxIdTokenLength = 16 * 1024;
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

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
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(response, OAuthJsonContext.Default.OpenIdUserInfo, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the RS256 signature against Twitch's published keys and checks issuer, audience, authorized party, expiry,
    /// issue time and the nonce stored for the authorization request.
    /// </summary>
    /// <param name="idToken">The compact JWS ID token, at most 16 KiB.</param>
    /// <param name="clientId">Your client ID; it must be an audience and, when present, the authorized party (azp).</param>
    /// <param name="expectedNonce">The nonce sent with the authorization request. Use <see cref="ValidateIdTokenWithoutNonceAsync"/> only for flows that sent none.</param>
    /// <param name="accessToken">The access token issued with the ID token; when given and the token has an at_hash claim, the hash must match.</param>
    /// <param name="cancellationToken">Cancels a signing key fetch.</param>
    /// <exception cref="TwitchIdTokenException">The token is invalid or its signing keys are unavailable.</exception>
    public Task<OpenIdTokenClaims> ValidateIdTokenAsync(string idToken, string clientId, string expectedNonce, string? accessToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedNonce);
        ValidateIdTokenArguments(idToken, clientId, accessToken);
        return ValidateIdTokenCoreAsync(idToken, clientId, expectedNonce, accessToken, cancellationToken);
    }

    /// <summary>
    /// Validates like <see cref="ValidateIdTokenAsync"/> but without a nonce check, for flows whose authorization request carried
    /// no nonce (for example an authorization code redeemed directly by the server). A nonce in the token is ignored.
    /// </summary>
    /// <exception cref="TwitchIdTokenException">The token is invalid or its signing keys are unavailable.</exception>
    public Task<OpenIdTokenClaims> ValidateIdTokenWithoutNonceAsync(string idToken, string clientId, string? accessToken = null, CancellationToken cancellationToken = default)
    {
        ValidateIdTokenArguments(idToken, clientId, accessToken);
        return ValidateIdTokenCoreAsync(idToken, clientId, null, accessToken, cancellationToken);
    }

    private static void ValidateIdTokenArguments(string idToken, string clientId, string? accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        if (accessToken is not null) ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
    }

    private async Task<OpenIdTokenClaims> ValidateIdTokenCoreAsync(string idToken, string clientId, string? expectedNonce, string? accessToken, CancellationToken cancellationToken)
    {
        // Bound the decoding work (and key fetches) an oversized, attacker-supplied token could cause.
        if (idToken.Length > MaxIdTokenLength) throw new TwitchIdTokenException("The ID token exceeds the 16 KiB limit.");
        var parts = idToken.Split('.');
        if (parts.Length != 3) throw new TwitchIdTokenException("The ID token is not a compact JWS.");
        var header = DeserializeSegment(parts[0], OAuthJsonContext.Default.IdTokenHeader);
        if (header.Alg != "RS256") throw new TwitchIdTokenException("The ID token must be signed with RS256.");
        var key = await _signingKeys.GetKeyAsync(header.Kid, FetchSigningKeysAsync, cancellationToken).ConfigureAwait(false);
        using (var rsa = RSA.Create(key))
        {
            if (!rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), DecodeSegment(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                throw new TwitchIdTokenException("The ID token signature is invalid.");
        }
        var payload = DeserializeSegment(parts[1], OAuthJsonContext.Default.IdTokenPayload);
        if (payload.Iss != OpenIdIssuer) throw new TwitchIdTokenException("The ID token was not issued by Twitch.");
        var audiences = ReadAudiences(payload.Aud);
        // azp is required with several audiences and must name this client whenever it is present.
        if (!audiences.Contains(clientId, StringComparer.Ordinal) || ((audiences.Count > 1 || payload.Azp is not null) && payload.Azp != clientId))
            throw new TwitchIdTokenException("The ID token was issued for a different client.");
        var now = _time.GetUtcNow();
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.Exp);
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(payload.Iat);
        if (expiresAt + ClockSkew <= now) throw new TwitchIdTokenException("The ID token has expired.");
        if (issuedAt - ClockSkew > now) throw new TwitchIdTokenException("The ID token was issued in the future.");
        if (expectedNonce is not null && !ValidateState(expectedNonce, payload.Nonce)) throw new TwitchIdTokenException("The ID token nonce does not match.");
        if (accessToken is not null && payload.AtHash is not null && !AccessTokenHashMatches(accessToken, payload.AtHash))
            throw new TwitchIdTokenException("The ID token was not issued with this access token (at_hash mismatch).");
        if (string.IsNullOrEmpty(payload.Sub)) throw new TwitchIdTokenException("The ID token has no subject.");
        return new()
        {
            Subject = payload.Sub, Issuer = payload.Iss, Audience = clientId, ExpiresAt = expiresAt, IssuedAt = issuedAt, Nonce = payload.Nonce,
            AuthorizedParty = payload.Azp, AccessTokenHash = payload.AtHash, Email = payload.Email, EmailVerified = payload.EmailVerified,
            Picture = payload.Picture, PreferredUsername = payload.PreferredUsername, UpdatedAt = payload.UpdatedAt,
        };
    }

    private async Task<IReadOnlyList<(string? Kid, RSAParameters Key)>> FetchSigningKeysAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Authority, "keys"));
        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        var set = await ReadAsync(response, OAuthJsonContext.Default.JsonWebKeySet, ct).ConfigureAwait(false);
        return set.Keys.Where(k => k.Kty == "RSA" && k.Use is null or "sig" && !string.IsNullOrEmpty(k.N) && !string.IsNullOrEmpty(k.E))
            .Select(k => (k.Kid, new RSAParameters { Modulus = DecodeSegment(k.N!), Exponent = DecodeSegment(k.E!) })).ToList();
    }

    /// <summary>at_hash is the base64url encoding of the left half of SHA-256 over the ASCII access token (OpenID Connect Core 3.2.2.9).</summary>
    private static bool AccessTokenHashMatches(string accessToken, string atHash)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.ASCII.GetBytes(accessToken), hash);
        var expected = Convert.ToBase64String(hash[..(hash.Length / 2)]).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(atHash));
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
}
