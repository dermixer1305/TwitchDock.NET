using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TwitchSdk.Core;
using TwitchSdk.Helix;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Tests;

public sealed class BitsAndSubscriptionsTests
{
    private static HelixClient Client(HttpClient http, AccessToken? token = null) => new(new(http,
        new StaticAccessTokenProvider(token ?? new("token")), new() { ClientId = "client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 }));
    private static string Fixture(string id) => ContractAssertions.Fixture("helix-bits-subscriptions.json", id);

    public static IEnumerable<object[]> Contracts()
    {
        yield return ["get-broadcaster-subscriptions", HelixJsonContext.Default.BroadcasterSubscriptionsResponse];
        yield return ["check-user-subscription", HelixJsonContext.Default.HelixPageUserSubscription];
        yield return ["get-bits-leaderboard", HelixJsonContext.Default.BitsLeaderboardResponse];
        yield return ["get-cheermotes", HelixJsonContext.Default.HelixPageCheermote];
        yield return ["get-extension-transactions", HelixJsonContext.Default.HelixPageExtensionTransaction];
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void ResponseContractsPreserveEveryDocumentedField(string id, JsonTypeInfo type)
        => ContractAssertions.Verify("helix-bits-subscriptions.json", id, type);

    [Fact]
    public async Task SubscriptionRequestsPreserveFiltersOptionalAggregatesAndGiftMetadata()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Content);
            Assert.Equal(++step == 3 ? "/helix/subscriptions/user" : "/helix/subscriptions", request.RequestUri!.AbsolutePath);
            Assert.Equal(step switch { 1 => "?broadcaster_id=1&first=100&before=previous", 2 => "?broadcaster_id=1&user_id=2&user_id=3&first=1", _ => "?broadcaster_id=1&user_id=2" }, request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(step switch
            {
                1 => Fixture("get-broadcaster-subscriptions"),
                2 => "{\"data\":[],\"pagination\":{},\"points\":null,\"total\":null}",
                _ => Fixture("check-user-subscription")
            }));
        }));
        var client = Client(http).Subscriptions;
        var full = await client.GetBroadcasterSubscriptionsAsync(new() { BroadcasterId = "1", First = 100, Before = "previous" });
        Assert.Equal(6, full.Points);
        Assert.Equal("", full.Data.Single().GifterId);
        var filtered = await client.GetBroadcasterSubscriptionsAsync(new() { BroadcasterId = "1", UserIds = ["2", "3"], First = 1 });
        Assert.Null(filtered.Points);
        Assert.Null(filtered.Total);
        Assert.Equal("3", (await client.CheckUserSubscriptionAsync(new() { BroadcasterId = "1", UserId = "2" })).Data.Single().GifterId);
        Assert.Equal(3, step);
    }

    [Fact]
    public void NonGiftUserSubscriptionCanOmitGifterFields()
    {
        var page = JsonSerializer.Deserialize("{\"data\":[{\"broadcaster_id\":\"1\",\"broadcaster_login\":\"example\",\"broadcaster_name\":\"Example\",\"is_gift\":false,\"tier\":\"1000\"}]}", HelixJsonContext.Default.HelixPageUserSubscription)!;
        var subscription = page.Data.Single();
        Assert.False(subscription.IsGift);
        Assert.Null(subscription.GifterId);
        Assert.Null(subscription.GifterLogin);
        Assert.Null(subscription.GifterName);
    }

    [Fact]
    public async Task SubscriptionsAllowExtensionAppGrantButCheckUserRequiresItsOwnScopeAndIdentity()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json(Fixture("get-broadcaster-subscriptions"))); }));
        var extension = Client(http, new("app", kind: TwitchTokenKind.App)).Subscriptions;
        await extension.GetBroadcasterSubscriptionsAsync(new() { BroadcasterId = "1" });
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => extension.CheckUserSubscriptionAsync(new() { BroadcasterId = "1", UserId = "2" }));
        var wrongOwner = Client(http, new("user", scopes: [TwitchScopes.ChannelReadSubscriptions, TwitchScopes.UserReadSubscriptions], kind: TwitchTokenKind.User, userId: "other")).Subscriptions;
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => wrongOwner.GetBroadcasterSubscriptionsAsync(new() { BroadcasterId = "1" }));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => wrongOwner.CheckUserSubscriptionAsync(new() { BroadcasterId = "1", UserId = "2" }));
        var noScope = Client(http, new("user", scopes: [], kind: TwitchTokenKind.User)).Subscriptions;
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => noScope.GetBroadcasterSubscriptionsAsync(new() { BroadcasterId = "1" }));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => noScope.CheckUserSubscriptionAsync(new() { BroadcasterId = "1", UserId = "2" }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FilteredSubscriptionEnumerationSnapshotsIdsAndNeverUsesUnsupportedCursor()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            calls++;
            Assert.Equal("?broadcaster_id=1&user_id=2", request.RequestUri!.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture("get-broadcaster-subscriptions")));
        }));
        var ids = new List<string> { "2" };
        var items = Client(http).Subscriptions.EnumerateBroadcasterSubscriptionsAsync(new() { BroadcasterId = "1", UserIds = ids });
        ids[0] = "changed";
        var count = 0;
        await foreach (var _ in items) count++;
        Assert.Equal(1, count);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SubscriptionEnumerationFollowsUnfilteredPagesAndHonorsInitialCursor()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal("?broadcaster_id=1&first=5&after=" + (++step == 1 ? "initial" : "next"), request.RequestUri!.Query);
            return Task.FromResult(TestHttpHandler.Json(step == 1 ? Fixture("get-broadcaster-subscriptions") : "{\"data\":[],\"pagination\":{}}"));
        }));
        var count = 0;
        await foreach (var _ in Client(http).Subscriptions.EnumerateBroadcasterSubscriptionsAsync(new() { BroadcasterId = "1", First = 5, After = "initial" })) count++;
        Assert.Equal(1, count);
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task BitsLeaderboardPreservesInstantAndEmptyDateWindow()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/bits/leaderboard", request.RequestUri!.AbsolutePath);
            Assert.Equal(++step == 1 ? "?count=100&period=month&started_at=2026-01-01T08%3A00%3A00.0000000%2B00%3A00&user_id=2" : "", request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture("get-bits-leaderboard")));
        }));
        var client = Client(http, new("user", scopes: [TwitchScopes.BitsRead], kind: TwitchTokenKind.User)).Bits;
        var result = await client.GetBitsLeaderboardAsync(new() { Count = 100, Period = "month", StartedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(-8)), UserId = "2" });
        Assert.Equal(2147483648L, result.Data.Single().Score);
        Assert.Equal("", (await client.GetBitsLeaderboardAsync()).DateRange.StartedAt);
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task CheermotesExposeEveryThemeFormatAndFractionalSizeWithAppToken()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal("/helix/bits/cheermotes", request.RequestUri!.AbsolutePath);
            Assert.Equal(++step == 1 ? "" : "?broadcaster_id=1", request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture("get-cheermotes")));
        }));
        var client = Client(http, new("app", kind: TwitchTokenKind.App)).Bits;
        var tier = (await client.GetCheermotesAsync()).Data.Single().Tiers.Single();
        Assert.Equal("https://example.org/da15", tier.Images.Dark.Animated["1.5"]);
        Assert.Equal(5, tier.Images.Light.Static.Count);
        Assert.True(tier.CanCheer);
        Assert.False(tier.ShowInBitsCard);
        Assert.True((await client.GetCheermotesAsync("1")).Data.Single().IsCharitable);
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task ExtensionTransactionsPreserveCamelCaseProductFieldsAndSnapshotPaginationFilters()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/extensions/transactions", request.RequestUri!.AbsolutePath);
            Assert.Equal("?extension_id=extension1&id=t1&id=t2&first=5&after=" + (++step == 1 ? "initial" : "next"), request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(step == 1 ? Fixture("get-extension-transactions") : "{\"data\":[],\"pagination\":{}}"));
        }));
        var ids = new List<string> { "t1", "t2" };
        var items = Client(http, new("app", kind: TwitchTokenKind.App)).Bits.EnumerateExtensionTransactionsAsync(new() { ExtensionId = "extension1", Ids = ids, First = 5, After = "initial" });
        ids.Clear();
        var results = new List<ExtensionTransaction>();
        await foreach (var item in items) results.Add(item);
        var product = Assert.Single(results).ProductData;
        Assert.True(product.InDevelopment);
        Assert.Equal("Example product", product.DisplayName);
        Assert.Equal("", product.Expiration);
        Assert.Equal(100, product.Cost.Amount);
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task BitsAccessChecksDistinguishBroadcasterUserAndExtensionAppTokens()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("No request expected")));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, new("app", kind: TwitchTokenKind.App)).Bits.GetBitsLeaderboardAsync());
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, new("user", scopes: [], kind: TwitchTokenKind.User)).Bits.GetBitsLeaderboardAsync());
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, new("user", kind: TwitchTokenKind.User)).Bits.GetExtensionTransactionsAsync(new() { ExtensionId = "extension1" }));
    }

    [Fact]
    public async Task BoundsRejectInvalidFiltersAndAcceptHundredIds()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"data\":[]}")); }));
        var client = Client(http);
        var ids = Enumerable.Range(1, 100).Select(i => i.ToString()).ToArray();
        await client.Subscriptions.GetBroadcasterSubscriptionsAsync(new() { BroadcasterId = "1", UserIds = ids });
        await client.Bits.GetExtensionTransactionsAsync(new() { ExtensionId = "e", Ids = ids });
        await Assert.ThrowsAsync<ArgumentException>(() => client.Subscriptions.GetBroadcasterSubscriptionsAsync(new() { BroadcasterId = "1", UserIds = [.. ids, "extra"] }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Subscriptions.GetBroadcasterSubscriptionsAsync(new() { BroadcasterId = "1", UserIds = ["2"], After = "next" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Subscriptions.GetBroadcasterSubscriptionsAsync(new() { BroadcasterId = "1", Before = "previous", After = "next" }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.Subscriptions.GetBroadcasterSubscriptionsAsync(new() { BroadcasterId = "1", First = 0 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Bits.GetExtensionTransactionsAsync(new() { ExtensionId = "e", Ids = [.. ids, "extra"] }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.Bits.GetExtensionTransactionsAsync(new() { ExtensionId = "e", First = 101 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.Bits.GetBitsLeaderboardAsync(new() { Count = 0 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Bits.GetBitsLeaderboardAsync(new() { Period = "invalid" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Bits.GetBitsLeaderboardAsync(new() { Period = "day" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Bits.GetBitsLeaderboardAsync(new() { StartedAt = DateTimeOffset.UtcNow }));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task AllFiveEndpointsPreserveErrorsIncludingNotSubscribedAndUnknownTransactions()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) => Task.FromResult(TestHttpHandler.Json("{\"error\":\"Not Found\",\"message\":\"not found\"}", HttpStatusCode.NotFound))));
        var client = Client(http);
        Func<Task>[] calls = [() => client.Subscriptions.GetBroadcasterSubscriptionsAsync(new() { BroadcasterId = "1" }),
            () => client.Subscriptions.CheckUserSubscriptionAsync(new() { BroadcasterId = "1", UserId = "2" }), () => client.Bits.GetBitsLeaderboardAsync(),
            () => client.Bits.GetCheermotesAsync(), () => client.Bits.GetExtensionTransactionsAsync(new() { ExtensionId = "e" })];
        foreach (var call in calls) Assert.Equal(HttpStatusCode.NotFound, (await Assert.ThrowsAsync<TwitchApiException>(call)).StatusCode);
    }
}
