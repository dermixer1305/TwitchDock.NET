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
        using var fx = new OpenIdFixture();
        var oauth = fx.Client();
        var valid = fx.Payload(claims: ""","azp":"client","email":"a@example.org","preferred_username":"user","email_verified":true""");

        var claims = await oauth.ValidateIdTokenAsync(fx.Token(valid), "client", "n1");
        Assert.Equal(("713936733", "a@example.org", "user"), (claims.Subject, claims.Email, claims.PreferredUsername));
        await oauth.ValidateIdTokenWithoutNonceAsync(fx.Token(valid), "client");
        Assert.Equal(1, fx.Fetches);

        var tampered = fx.Token(valid)[..^4] + "AAAA";
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(tampered, "client", "n1"));
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(fx.Token(valid), "other", "n1"));
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(fx.Token(valid), "client", "n2"));
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenWithoutNonceAsync(fx.Token(valid, alg: "none"), "client"));
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenWithoutNonceAsync(fx.Token(valid.Replace("https://id.twitch.tv/oauth2", "https://evil.example")), "client"));
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenWithoutNonceAsync("not-a-jwt", "client"));
        fx.Time.Advance(TimeSpan.FromMinutes(21));
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(fx.Token(valid), "client", "n1"));
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenWithoutNonceAsync(fx.Token(valid, kid: "rotated"), "client"));
        Assert.Equal(2, fx.Fetches);
    }

    [Fact]
    public async Task IdTokenValidationRequiresTheNonceUnlessSkippedExplicitly()
    {
        using var fx = new OpenIdFixture();
        var oauth = fx.Client();
        await Assert.ThrowsAsync<ArgumentNullException>(() => oauth.ValidateIdTokenAsync(fx.Token(), "client", null!));
        await Assert.ThrowsAsync<ArgumentException>(() => oauth.ValidateIdTokenAsync(fx.Token(), "client", " "));
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(fx.Token(fx.Payload(nonce: null)), "client", "n1"));
        Assert.Equal("713936733", (await oauth.ValidateIdTokenWithoutNonceAsync(fx.Token(fx.Payload(nonce: null)), "client")).Subject);
        // Argument errors are raised before any key fetch.
        Assert.Equal(1, fx.Fetches);
    }

    [Fact]
    public async Task IdTokenAtHashMustMatchTheAccessTokenIssuedWithIt()
    {
        using var fx = new OpenIdFixture();
        var oauth = fx.Client();
        const string accessToken = "kpvu9i9cr0bbkf5kjcn7a2wgh0x8gl";
        var atHash = OpenIdFixture.B64(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken)).AsSpan(0, 16));
        var bound = fx.Token(fx.Payload(claims: ",\"at_hash\":\"" + atHash + "\""));

        Assert.Equal(atHash, (await oauth.ValidateIdTokenAsync(bound, "client", "n1", accessToken)).AccessTokenHash);
        await oauth.ValidateIdTokenWithoutNonceAsync(bound, "client", accessToken);
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(bound, "client", "n1", accessToken + "x"));
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenWithoutNonceAsync(bound, "client", "other-token"));
        // Without an access token, or without the claim, there is nothing to compare.
        await oauth.ValidateIdTokenAsync(bound, "client", "n1");
        await oauth.ValidateIdTokenAsync(fx.Token(), "client", "n1", accessToken);
        await Assert.ThrowsAsync<ArgumentException>(() => oauth.ValidateIdTokenAsync(bound, "client", "n1", " "));
    }

    [Fact]
    public async Task IdTokenAuthorizedPartyMustBeThisClientWhenPresent()
    {
        using var fx = new OpenIdFixture();
        var oauth = fx.Client();
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(fx.Token(fx.Payload(claims: ",\"azp\":\"other\"")), "client", "n1"));
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(fx.Token(fx.Payload(audience: "[\"client\",\"other\"]")), "client", "n1"));
        Assert.Equal("client", (await oauth.ValidateIdTokenAsync(fx.Token(fx.Payload(audience: "[\"client\",\"other\"]", claims: ",\"azp\":\"client\"")), "client", "n1")).AuthorizedParty);
        Assert.Null((await oauth.ValidateIdTokenAsync(fx.Token(), "client", "n1")).AuthorizedParty);
    }

    [Fact]
    public async Task OversizedIdTokensAreRejectedBeforeParsingOrFetchingKeys()
    {
        using var fx = new OpenIdFixture();
        var huge = fx.Token(fx.Payload(claims: ",\"pad\":\"" + new string('a', 16 * 1024) + "\""));
        Assert.Contains("16 KiB", (await Assert.ThrowsAsync<TwitchIdTokenException>(() => fx.Client().ValidateIdTokenAsync(huge, "client", "n1"))).Message);
        Assert.Equal(0, fx.Fetches);
    }

    [Fact]
    public async Task SigningKeysAreFetchedOnceForConcurrentValidationsAcrossClients()
    {
        using var fx = new OpenIdFixture();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fx.FetchGate = release.Task;
        var token = fx.Token();
        // DI creates a TwitchOAuthClient per use; the cache, not the client instance, owns the keys.
        var validations = Enumerable.Range(0, 8).Select(_ => fx.Client().ValidateIdTokenAsync(token, "client", "n1")).ToArray();
        await ManualTimeProvider.WaitUntilAsync(() => fx.Fetches == 1);
        release.SetResult();
        await Task.WhenAll(validations);
        Assert.Equal(1, fx.Fetches);
        await new TwitchOAuthClient(fx.Http, fx.Time, new OpenIdSigningKeyCache(fx.Time)).ValidateIdTokenAsync(token, "client", "n1");
        Assert.Equal(2, fx.Fetches);
    }

    [Fact]
    public async Task SigningKeysExpireAfterAnHourAndUnknownKeysRefetchAtMostEveryFiveMinutes()
    {
        using var fx = new OpenIdFixture();
        var oauth = fx.Client();
        await oauth.ValidateIdTokenAsync(fx.Token(), "client", "n1");
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(fx.Token(kid: "rotated"), "client", "n1"));
        Assert.Equal(1, fx.Fetches);
        fx.Time.Advance(TimeSpan.FromMinutes(5));
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(fx.Token(kid: "rotated"), "client", "n1"));
        Assert.Equal(2, fx.Fetches);
        fx.Time.Advance(TimeSpan.FromMinutes(59));
        await oauth.ValidateIdTokenAsync(fx.Token(), "client", "n1");
        Assert.Equal(2, fx.Fetches);
        fx.Time.Advance(TimeSpan.FromMinutes(1));
        await oauth.ValidateIdTokenAsync(fx.Token(), "client", "n1");
        Assert.Equal(3, fx.Fetches);
    }

    [Fact]
    public async Task SigningKeyFetchFailuresAreCachedAndExpiredKeysOnlyServeWhileTheRefreshIsHeldBack()
    {
        using var fx = new OpenIdFixture();
        var oauth = fx.Client();
        fx.FailFetches = true;
        var unavailable = await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(fx.Token(), "client", "n1"));
        Assert.IsType<TwitchApiException>(unavailable.InnerException);
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(fx.Token(), "client", "n1"));
        Assert.Equal(1, fx.Fetches);
        fx.Time.Advance(TimeSpan.FromSeconds(30));
        fx.FailFetches = false;
        await oauth.ValidateIdTokenAsync(fx.Token(), "client", "n1");
        Assert.Equal(2, fx.Fetches);

        // The keys expire and their refresh fails: that validation fails, but while the next refresh is held back the
        // expired keys still verify known key IDs.
        fx.Time.Advance(TimeSpan.FromHours(1));
        fx.FailFetches = true;
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(fx.Token(), "client", "n1"));
        Assert.Equal(3, fx.Fetches);
        await oauth.ValidateIdTokenAsync(fx.Token(), "client", "n1");
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(fx.Token(kid: "rotated"), "client", "n1"));
        Assert.Equal(3, fx.Fetches);
        fx.Time.Advance(TimeSpan.FromSeconds(30));
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(fx.Token(), "client", "n1"));
        Assert.Equal(4, fx.Fetches);
    }

    [Fact]
    public async Task HungKeyFetchTimesOutAndHoldsBackQueuedValidations()
    {
        using var fx = new OpenIdFixture();
        var oauth = fx.Client();
        fx.FetchGate = new TaskCompletionSource().Task;
        var hung = oauth.ValidateIdTokenAsync(fx.Token(), "client", "n1");
        await ManualTimeProvider.WaitUntilAsync(() => fx.Fetches == 1);
        // The fetch is bounded by the cache, and the failure is stamped when it occurred, not when the fetch started.
        fx.Time.Advance(TimeSpan.FromSeconds(30));
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => hung);
        await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(fx.Token(), "client", "n1"));
        Assert.Equal(1, fx.Fetches);
        fx.FetchGate = null;
        fx.Time.Advance(TimeSpan.FromSeconds(30));
        await oauth.ValidateIdTokenAsync(fx.Token(), "client", "n1");
        Assert.Equal(2, fx.Fetches);
    }

    [Fact]
    public async Task AnEmptyKeySetDoesNotReplaceTheCachedKeys()
    {
        using var fx = new OpenIdFixture();
        var oauth = fx.Client();
        await oauth.ValidateIdTokenAsync(fx.Token(), "client", "n1");
        fx.Time.Advance(TimeSpan.FromMinutes(5));
        fx.EmptyKeys = true;
        var error = await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenAsync(fx.Token(kid: "rotated"), "client", "n1"));
        Assert.IsType<InvalidDataException>(error.InnerException);
        await oauth.ValidateIdTokenAsync(fx.Token(), "client", "n1");
        Assert.Equal(2, fx.Fetches);
    }

    [Fact]
    public async Task StalledOAuthResponseBodiesTimeOutLikeHttpClient()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StalledContent() })))
        {
            Timeout = TimeSpan.FromMilliseconds(200),
        };
        // ResponseHeadersRead ends HttpClient.Timeout at the headers; the body read must still be bounded.
        var error = await Assert.ThrowsAsync<TaskCanceledException>(() => new TwitchOAuthClient(http).ValidateAsync("token").WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsType<TimeoutException>(error.InnerException);
    }

    [Fact]
    public void RepeatedCallbackParametersAreRejectedWithoutEchoingThem()
    {
        var error = Assert.Throws<TwitchOAuthCallbackException>(() => TwitchOAuthCallbacks.ParseAuthorizationCode(
            new Uri($"https://app.example/callback?code=a&state={State}&%3Cscript%3Ealert(1)=1&%3Cscript%3Ealert(1)=2"), State));
        Assert.Equal("The redirect repeats a parameter.", error.Message);
        Assert.Null(error.Error);
    }

    [Fact]
    public void ImplicitGrantWithOnlyAnIdTokenHasNoAccessToken()
    {
        var callback = TwitchOAuthCallbacks.ParseImplicitGrant(new Uri($"https://app.example/callback#id_token=jwt&state={State}"), State);
        Assert.Null(callback.AccessToken);
        Assert.Equal("jwt", callback.IdToken);
        Assert.Null(TwitchOAuthCallbacks.ParseImplicitGrant(new Uri($"https://app.example/callback#access_token=tok&state={State}"), State).IdToken);
    }

    [Fact]
    public async Task DevicePollingDefaultsAMissingLifetimeAndInterval()
    {
        var time = new ManualTimeProvider();
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(Interlocked.Increment(ref calls) < 3
            ? TestHttpHandler.Json("""{"status":400,"message":"authorization_pending"}""", HttpStatusCode.BadRequest)
            : TestHttpHandler.Json("""{"access_token":"user","expires_in":3600,"token_type":"bearer"}"""))));
        // Twitch always sends both; a response without them must not mean a one-second deadline or one-second polling.
        var polling = new TwitchOAuthClient(http, time).WaitForDeviceAuthorizationAsync("client", Device(interval: 0, expiresIn: 0), []);
        for (var expected = 1; expected <= 3; expected++)
        {
            await ManualTimeProvider.WaitUntilAsync(() => time.TimerCount == 1);
            time.Advance(TimeSpan.FromSeconds(4));
            Assert.Equal(expected - 1, Volatile.Read(ref calls));
            time.Advance(TimeSpan.FromSeconds(1));
            await ManualTimeProvider.WaitUntilAsync(() => Volatile.Read(ref calls) == expected);
        }
        Assert.Equal("user", (await polling).AccessToken);
    }

    [Fact]
    public async Task OAuthResponsesAreLimitedToOneMebibyte()
    {
        var big = Encoding.UTF8.GetBytes("{\"access_token\":\"" + new string('a', 1024 * 1024) + "\"}");
        using var declared = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(big) })));
        await Assert.ThrowsAsync<InvalidDataException>(() => new TwitchOAuthClient(declared).GetAppTokenAsync("client", "secret"));
        using var chunked = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnknownLengthContent(big) })));
        await Assert.ThrowsAsync<InvalidDataException>(() => new TwitchOAuthClient(chunked).ValidateAsync("token"));
        using var failing = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new UnknownLengthContent(big) })));
        Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAsync<TwitchApiException>(() => new TwitchOAuthClient(failing).RefreshAsync("client", "refresh"))).StatusCode);
    }

    private static DeviceAuthorization Device(int interval, int expiresIn) => new()
    {
        DeviceCode = "device", UserCode = "ABCDEFGH", VerificationUri = "https://www.twitch.tv/activate?device-code=ABCDEFGH", Interval = interval, ExpiresIn = expiresIn,
    };
}
