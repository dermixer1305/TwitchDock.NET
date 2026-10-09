using System.Net;
using TwitchSdk.Authentication;
using TwitchSdk.Core;
using TwitchSdk.Helix;

namespace TwitchSdk.Tests;

public sealed class AuthorizationTests
{
    private static HelixClient Client(HttpClient http, AccessToken token) => new(new(http, new StaticAccessTokenProvider(token), new() { ClientId = "client" }));

    [Fact]
    public async Task KnownUserTokenFailsBeforeSendingWhenScopeIsMissing()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("Must not send")));
        var client = Client(http, new("token", scopes: ["user:read:email"], kind: TwitchTokenKind.User, userId: "1"));
        var exception = await Assert.ThrowsAsync<TwitchAuthorizationException>(() => client.Goals.GetCreatorGoalsAsync("1"));
        Assert.Equal("channel:read:goals", Assert.Single(exception.MissingScopes));
    }

    [Fact]
    public async Task KnownUserAndClientMismatchesFailBeforeHttp()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("Must not send")));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, new("token", scopes: ["channel:read:goals"], kind: TwitchTokenKind.User, userId: "wrong")).Goals.GetCreatorGoalsAsync("1"));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, new("token", clientId: "another-client")).GetUsersAsync(new() { Ids = ["1"] }));
    }

    [Fact]
    public async Task KnownAppTokenCanUseAdsPriorGrantButCannotUseUserOnlyAnalytics()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"data\":[]}")); }));
        var client = Client(http, new("token", scopes: [], kind: TwitchTokenKind.App));
        await client.Ads.GetAdScheduleAsync("1");
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => client.Analytics.GetGameAnalyticsAsync());
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => client.GetUsersAsync());
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task UnknownMetadataDefersToTwitchInsteadOfAssumingMissingGrants()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json("{\"data\":[]}"))));
        await Client(http, new("token")).Goals.GetCreatorGoalsAsync("1");
    }

    [Fact]
    public async Task KnownTokenKindEnforcesWebhookAndSourceOnlyChatRequirements()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("Must not send")));
        var userClient = Client(http, new("token", scopes: ["user:write:chat"], kind: TwitchTokenKind.User, userId: "1"));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => userClient.SendChatMessageAsync(new() { BroadcasterId = "2", SenderId = "1", Message = "hello", ForSourceOnly = false }));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => userClient.CreateEventSubSubscriptionAsync(new()
        {
            Type = "stream.online", Version = "1", Condition = new Dictionary<string, string> { ["broadcaster_user_id"] = "1" },
            Transport = new() { Method = "webhook", Callback = "https://example.org/events", Secret = "long-enough-secret" }
        }));
    }

    [Fact]
    public async Task OAuthGrantTagsTokenKindAndValidationPopulatesIdentity()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) => Task.FromResult(TestHttpHandler.Json(request.RequestUri!.AbsolutePath.EndsWith("validate")
            ? "{\"client_id\":\"client\",\"user_id\":\"1\",\"scopes\":[\"channel:read:goals\"],\"expires_in\":3600}"
            : "{\"access_token\":\"token\",\"refresh_token\":\"refresh\",\"scope\":[\"channel:read:goals\"],\"expires_in\":3600}"))));
        var oauth = new TwitchOAuthClient(http);
        var app = await oauth.GetAppTokenAsync("client", "secret");
        Assert.Equal(TwitchTokenKind.App, app.Kind);
        var user = await oauth.RefreshAsync("client", "refresh");
        Assert.Equal(TwitchTokenKind.User, user.Kind);
        using var provider = new RefreshingTokenProvider((_, _) => Task.FromResult(user), user);
        using var stop = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TokenValidationLoop.RunAsync(oauth, provider, "client", (_, _) => { stop.Cancel(); return Task.CompletedTask; }, cancellationToken: stop.Token));
        var token = await provider.GetTokenAsync();
        Assert.Equal("1", token.UserId);
        Assert.Equal("client", token.ClientId);
        Assert.True(token.ScopesKnown);
    }

    [Fact]
    public async Task StaleValidationCannotOverwriteRotatedToken()
    {
        using var provider = new RefreshingTokenProvider((_, _) => Task.FromResult(new OAuthTokenResponse { AccessToken = "new" }), new() { AccessToken = "old" });
        var old = await provider.GetTokenAsync();
        await provider.RefreshTokenAsync(old);
        Assert.False(await provider.UpdateMetadataAsync(new("old", scopes: ["user:read:email"], kind: TwitchTokenKind.User)));
        Assert.Equal("new", (await provider.GetTokenAsync()).Value);
    }

    [Fact]
    public async Task RetryRechecksScopesAfterRefresh()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{}", HttpStatusCode.Unauthorized)); }));
        var transport = new TwitchHttpClient(http, new RotatingProvider(), new() { ClientId = "client" });
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => new HelixClient(transport).Goals.GetCreatorGoalsAsync("1"));
        Assert.Equal(1, calls);
    }

    private sealed class RotatingProvider : IAccessTokenProvider
    {
        public ValueTask<AccessToken> GetTokenAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new AccessToken("old", scopes: ["channel:read:goals"], kind: TwitchTokenKind.User));
        public ValueTask<AccessToken> RefreshTokenAsync(AccessToken rejectedToken, CancellationToken cancellationToken = default) => ValueTask.FromResult(new AccessToken("new", scopes: [], kind: TwitchTokenKind.User));
    }
}
