using System.Net;
using TwitchSdk.Authentication;
using TwitchSdk.Core;

namespace TwitchSdk.Tests;

public sealed class AuthenticationTests
{
    [Fact]
    public async Task ValidationRunsHourlyWhileIdleAndStopsOnCancellation()
    {
        var time = new ManualTimeProvider();
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(TestHttpHandler.Json("{\"client_id\":\"client\",\"scopes\":[],\"expires_in\":7200}"));
        }));
        using var stop = new CancellationTokenSource();
        var loop = TokenValidationLoop.RunAsync(new(http), new StaticAccessTokenProvider(new("token")), "client", (_, _) => Task.CompletedTask, time, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => time.TimerCount == 1);
        Assert.Equal(1, calls);
        time.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(1, calls);
        time.Advance(TimeSpan.FromMinutes(1));
        await ManualTimeProvider.WaitUntilAsync(() => Volatile.Read(ref calls) == 2);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loop);
    }

    [Fact]
    public async Task ConcurrentRejectionsShareOneRefreshAndRotateRefreshToken()
    {
        var calls = 0;
        var refreshes = new List<string?>();
        using var provider = new RefreshingTokenProvider(async (refresh, ct) =>
        {
            refreshes.Add(refresh);
            await Task.Delay(10, ct);
            var count = Interlocked.Increment(ref calls);
            return new() { AccessToken = $"token-{count}", RefreshToken = $"refresh-{count}", ExpiresIn = 3600 };
        }, new() { AccessToken = "old", RefreshToken = "refresh-old", ExpiresIn = 3600 });
        var old = await provider.GetTokenAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => provider.RefreshTokenAsync(old).AsTask()));
        Assert.All(results, token => Assert.Equal("token-1", token.Value));
        Assert.Equal(1, calls);
        await provider.RefreshTokenAsync(results[0]);
        Assert.Equal(new[] { "refresh-old", "refresh-1" }, refreshes);
    }

    [Fact]
    public async Task RefreshFormEncodesReservedCharactersExactlyOnce()
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal("https://id.twitch.tv/oauth2/token", request.RequestUri!.AbsoluteUri);
            var body = await request.Content!.ReadAsStringAsync(ct);
            Assert.Contains("refresh_token=a%2Bb%2Fc%25%26", body);
            Assert.DoesNotContain("client_secret", body);
            return TestHttpHandler.Json("{\"access_token\":\"fresh\",\"refresh_token\":\"rotated\",\"expires_in\":10,\"scope\":[\"user:read:chat\"],\"token_type\":\"bearer\"}");
        }));
        var response = await new TwitchOAuthClient(http).RefreshAsync("client", "a+b/c%&");
        Assert.Equal("rotated", response.RefreshToken);
        Assert.Equal("user:read:chat", Assert.Single(response.Scope));
        Assert.DoesNotContain("fresh", response.ToString());
        Assert.DoesNotContain("rotated", response.ToString());
    }

    [Fact]
    public async Task OAuthErrorsDoNotExposeResponseSecrets()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json("{\"message\":\"secret-leaked\"}", HttpStatusCode.BadRequest))));
        var ex = await Assert.ThrowsAsync<TwitchApiException>(() => new TwitchOAuthClient(http).GetAppTokenAsync("client", "secret"));
        Assert.DoesNotContain("secret-leaked", ex.ToString());
    }

    [Fact]
    public async Task StartupValidationChecksClientIdentity()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal("OAuth token", request.Headers.Authorization!.ToString());
            return Task.FromResult(TestHttpHandler.Json("{\"client_id\":\"wrong\",\"scopes\":[],\"expires_in\":1}"));
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => TokenValidationLoop.RunAsync(new(http), new StaticAccessTokenProvider(new("token")), "expected", (_, _) => Task.CompletedTask));
    }

    [Fact]
    public void AuthorizationUriEncodesRedirectScopesAndState()
    {
        var uri = TwitchOAuthClient.CreateAuthorizationUri("client", new("https://example.org/callback?x=1&y=2"), ["user:read:chat"], "state&value");
        Assert.Contains("state=state%26value", uri.AbsoluteUri);
        Assert.Contains("redirect_uri=https%3A%2F%2Fexample.org%2Fcallback%3Fx%3D1%26y%3D2", uri.AbsoluteUri);
        Assert.Contains("response_type=code", uri.AbsoluteUri);
        Assert.False(TwitchOAuthClient.ValidateState(null, null));
        Assert.False(TwitchOAuthClient.ValidateState("saved", "other"));
        Assert.True(TwitchOAuthClient.ValidateState("saved", "saved"));
        Assert.NotEqual(TwitchOAuthClient.CreateState(), TwitchOAuthClient.CreateState());
    }

    [Fact]
    public async Task RotatedCredentialIsKeptWhenPersistenceFails()
    {
        var calls = 0;
        using var provider = new RefreshingTokenProvider((_, _) =>
        {
            calls++;
            return Task.FromResult(new OAuthTokenResponse { AccessToken = "rotated", RefreshToken = "rotated-refresh" });
        }, persist: (_, _) => throw new IOException("storage unavailable"));
        await Assert.ThrowsAsync<IOException>(() => provider.GetTokenAsync().AsTask());
        Assert.Equal("rotated", (await provider.GetTokenAsync()).Value);
        Assert.Equal(1, calls);
    }
}
