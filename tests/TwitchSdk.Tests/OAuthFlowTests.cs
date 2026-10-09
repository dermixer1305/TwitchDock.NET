using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TwitchSdk.Authentication;
using TwitchSdk.Core;

namespace TwitchSdk.Tests;

public sealed class OAuthFlowTests
{
    private const string State = "state-123";

    [Fact]
    public void AuthorizationCodeCallbackReturnsCodeAndScopesAfterStateCheck()
    {
        var callback = TwitchOAuthCallbacks.ParseAuthorizationCode(new Uri($"https://app.example/callback?code=abc%2B1&scope=user%3Aread%3Aemail+chat%3Aread&state={State}"), State);
        Assert.Equal("abc+1", callback.Code);
        Assert.Equal(["user:read:email", "chat:read"], callback.Scopes);
        Assert.DoesNotContain("abc", callback.ToString());
    }

    [Fact]
    public void DeniedCallbackSurfacesTheOAuthErrorOnlyForTheMatchingState()
    {
        var denied = Assert.Throws<TwitchOAuthCallbackException>(() => TwitchOAuthCallbacks.ParseAuthorizationCode(
            new Uri($"https://app.example/callback?error=access_denied&error_description=The+user+denied+you+access&state={State}"), State));
        Assert.Equal(("access_denied", "The user denied you access"), (denied.Error, denied.ErrorDescription));
        var forged = Assert.Throws<TwitchOAuthCallbackException>(() => TwitchOAuthCallbacks.ParseAuthorizationCode(
            new Uri("https://app.example/callback?error=access_denied&state=other"), State));
        Assert.Null(forged.Error);
    }

    [Theory]
    [InlineData("https://app.example/callback?code=a&state=state-123&code=b")]
    [InlineData("https://app.example/callback?state=state-123")]
    [InlineData("https://app.example/callback?code=a")]
    public void MalformedAuthorizationCodeCallbacksAreRejected(string uri)
        => Assert.Throws<TwitchOAuthCallbackException>(() => TwitchOAuthCallbacks.ParseAuthorizationCode(new Uri(uri), State));

    [Fact]
    public void ImplicitGrantReadsFragmentTokensAndQueryErrors()
    {
        var callback = TwitchOAuthCallbacks.ParseImplicitGrant(new Uri($"https://app.example/callback#access_token=tok&id_token=jwt&scope=openid+chat%3Aread&state={State}&token_type=bearer"), State);
        Assert.Equal(("tok", "jwt", "bearer"), (callback.AccessToken, callback.IdToken, callback.TokenType));
        Assert.Equal(["openid", "chat:read"], callback.Scopes);
        var denied = Assert.Throws<TwitchOAuthCallbackException>(() => TwitchOAuthCallbacks.ParseImplicitGrant(new Uri($"https://app.example/callback?error=redirect_mismatch&state={State}"), State));
        Assert.Equal("redirect_mismatch", denied.Error);
        Assert.Throws<TwitchOAuthCallbackException>(() => TwitchOAuthCallbacks.ParseImplicitGrant(new Uri("https://app.example/callback#access_token=tok&state=wrong"), State));
    }

    [Fact]
    public async Task DevicePollingWaitsWhilePendingSlowsDownAndReturnsTheToken()
    {
        var time = new ManualTimeProvider();
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            var form = await request.Content!.ReadAsStringAsync(ct);
            Assert.Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code", form);
            Assert.Contains("device_code=device", form);
            return Interlocked.Increment(ref calls) switch
            {
                1 => TestHttpHandler.Json("""{"status":400,"message":"authorization_pending"}""", HttpStatusCode.BadRequest),
                2 => TestHttpHandler.Json("""{"status":400,"message":"slow_down"}""", HttpStatusCode.BadRequest),
                _ => TestHttpHandler.Json("""{"access_token":"user","refresh_token":"refresh","expires_in":3600,"scope":["chat:read"],"token_type":"bearer"}"""),
            };
        }));
        var oauth = new TwitchOAuthClient(http, time);
        var polling = oauth.WaitForDeviceAuthorizationAsync("client", Device(interval: 5, expiresIn: 1800), ["chat:read"]);
        foreach (var (wait, expectedCalls) in new[] { (5, 1), (5, 2), (10, 3) })
        {
            await ManualTimeProvider.WaitUntilAsync(() => time.TimerCount == 1);
            time.Advance(TimeSpan.FromSeconds(wait - 1));
            Assert.Equal(expectedCalls - 1, Volatile.Read(ref calls));
            time.Advance(TimeSpan.FromSeconds(1));
            await ManualTimeProvider.WaitUntilAsync(() => Volatile.Read(ref calls) == expectedCalls);
        }
        var token = await polling;
        Assert.Equal((TwitchTokenKind.User, "client"), (token.Kind, token.ClientId));
    }

    [Fact]
    public async Task DevicePollingStopsWhenTheCodeExpiresOrIsRejected()
    {
        var time = new ManualTimeProvider();
        using var pending = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json("""{"status":400,"message":"authorization_pending"}""", HttpStatusCode.BadRequest))));
        var expiring = new TwitchOAuthClient(pending, time).WaitForDeviceAuthorizationAsync("client", Device(interval: 5, expiresIn: 5), []);
        await ManualTimeProvider.WaitUntilAsync(() => time.TimerCount == 1);
        time.Advance(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<TimeoutException>(() => expiring);

        using var invalid = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json("""{"status":400,"message":"invalid device code"}""", HttpStatusCode.BadRequest))));
        var rejected = new TwitchOAuthClient(invalid, time).WaitForDeviceAuthorizationAsync("client", Device(interval: 1, expiresIn: 600), []);
        await ManualTimeProvider.WaitUntilAsync(() => time.TimerCount == 1);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("invalid_device_code", (await Assert.ThrowsAsync<TwitchApiException>(() => rejected)).Error);
    }

    [Fact]
    public async Task OAuthErrorMessagesOnlyExposeKnownCodes()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json("""{"error":"Bad Request","status":400,"message":"Invalid refresh token"}""", HttpStatusCode.BadRequest))));
        var error = await Assert.ThrowsAsync<TwitchApiException>(() => new TwitchOAuthClient(http).RefreshAsync("client", "secret-refresh"));
        Assert.Equal("invalid_refresh_token", error.Error);
        using var leaky = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json("""{"status":400,"message":"token abc123 rejected"}""", HttpStatusCode.BadRequest))));
        var hidden = await Assert.ThrowsAsync<TwitchApiException>(() => new TwitchOAuthClient(leaky).RefreshAsync("client", "secret-refresh"));
        Assert.Null(hidden.Error);
        Assert.DoesNotContain("abc123", hidden.Message);
    }

    [Fact]
    public void OpenIdAuthorizationUriAddsOpenIdNonceClaimsAndResponseType()
    {
        var uri = TwitchOAuthClient.CreateOpenIdAuthorizationUri("client", new Uri("https://app.example/callback"), ["user:read:email"], State, "nonce-1",
            OpenIdResponseType.TokenIdToken, new OpenIdClaimsRequest { IdToken = ["email", "email_verified"], UserInfo = ["picture"] });
        var query = Uri.UnescapeDataString(uri.Query);
        Assert.StartsWith("https://id.twitch.tv/oauth2/authorize?", uri.AbsoluteUri);
        Assert.Contains("response_type=token id_token", query);
        Assert.Contains("scope=openid user:read:email", query);
        Assert.Contains("nonce=nonce-1", query);
        Assert.Contains("""claims={"id_token":{"email":null,"email_verified":null},"userinfo":{"picture":null}}""", query);
    }

    [Fact]
    public async Task UserInfoUsesBearerTokenAndReadsOptionalClaims()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal("https://id.twitch.tv/oauth2/userinfo", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            return Task.FromResult(TestHttpHandler.Json("""{"aud":"client","exp":1760011200,"iat":1760007600,"iss":"https://id.twitch.tv/oauth2","sub":"713936733","email":"a@example.org","email_verified":true,"picture":"https://example.org/p.png","preferred_username":"user","updated_at":"2018-03-22T19:22:09Z"}"""));
        }));
        var info = await new TwitchOAuthClient(http).GetUserInfoAsync("token");
        Assert.Equal(("713936733", true, "user"), (info.Sub, info.EmailVerified, info.PreferredUsername));
    }

    [Fact]
    public async Task IdTokenValidationVerifiesSignatureAndClaims()
    {
        using var rsa = RSA.Create(2048);
        var time = new ManualTimeProvider();
        var now = time.GetUtcNow().ToUnixTimeSeconds();
        var keyFetches = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal("https://id.twitch.tv/oauth2/keys", request.RequestUri!.AbsoluteUri);
            Interlocked.Increment(ref keyFetches);
            var key = rsa.ExportParameters(false);
            return Task.FromResult(TestHttpHandler.Json($$"""{"keys":[{"alg":"RS256","e":"{{B64(key.Exponent!)}}","kid":"1","kty":"RSA","n":"{{B64(key.Modulus!)}}","use":"sig"}]}"""));
        }));
        var oauth = new TwitchOAuthClient(http, time);
        string Token(string payload, string alg = "RS256", string kid = "1") => Sign(rsa, $$"""{"alg":"{{alg}}","kid":"{{kid}}","typ":"JWT"}""", payload);
        var valid = $$"""{"iss":"https://id.twitch.tv/oauth2","sub":"713936733","aud":"client","exp":{{now + 900}},"iat":{{now}},"nonce":"n1","azp":"client","email":"a@example.org","email_verified":true,"preferred_username":"user"}""";

        var claims = await oauth.ValidateIdTokenAsync(Token(valid), "client", "n1");
        Assert.Equal(("713936733", "a@example.org", "user"), (claims.Subject, claims.Email, claims.PreferredUsername));
        await oauth.ValidateIdTokenAsync(Token(valid), "client");
        Assert.Equal(1, keyFetches);

        var tampered = Token(valid)[..^4] + "AAAA";
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(tampered, "client", "n1"));
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(Token(valid), "other", "n1"));
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(Token(valid), "client", "n2"));
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(Token(valid, alg: "none"), "client"));
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(Token(valid.Replace("https://id.twitch.tv/oauth2", "https://evil.example")), "client"));
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync("not-a-jwt", "client"));
        time.Advance(TimeSpan.FromMinutes(21));
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(Token(valid), "client", "n1"));
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(Token(valid, kid: "rotated"), "client"));
        Assert.Equal(2, keyFetches);
    }

    private static DeviceAuthorization Device(int interval, int expiresIn) => new()
    {
        DeviceCode = "device", UserCode = "ABCDEFGH", VerificationUri = "https://www.twitch.tv/activate?device-code=ABCDEFGH", Interval = interval, ExpiresIn = expiresIn,
    };

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Sign(RSA rsa, string header, string payload)
    {
        var signingInput = B64(Encoding.UTF8.GetBytes(header)) + "." + B64(Encoding.UTF8.GetBytes(payload));
        return signingInput + "." + B64(rsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }
}
