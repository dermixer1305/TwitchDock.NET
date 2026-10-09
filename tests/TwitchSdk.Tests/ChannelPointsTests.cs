using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TwitchSdk.Core;
using TwitchSdk.Helix;
using TwitchSdk.Helix.Clients;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Tests;

public sealed class ChannelPointsTests
{
    private static ChannelPointsClient Client(HttpClient http, AccessToken? token = null) => new HelixClient(new(http,
        new StaticAccessTokenProvider(token ?? new("token")), new() { ClientId = "client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 })).ChannelPoints;
    private static string Fixture(string id) => ContractAssertions.Fixture("helix-channel-points.json", id);

    public static IEnumerable<object[]> Contracts()
    {
        yield return ["create-custom-rewards", HelixJsonContext.Default.HelixPageCustomReward];
        yield return ["get-custom-reward", HelixJsonContext.Default.HelixPageCustomReward];
        yield return ["update-custom-reward", HelixJsonContext.Default.HelixPageCustomReward];
        yield return ["get-custom-reward-redemption", HelixJsonContext.Default.HelixPageCustomRewardRedemption];
        yield return ["update-redemption-status", HelixJsonContext.Default.HelixPageCustomRewardRedemption];
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void ResponseContractsPreserveEveryDocumentedField(string id, JsonTypeInfo type)
        => ContractAssertions.Verify("helix-channel-points.json", id, type);

    [Fact]
    public async Task CreateRewardSerializesAllSettingsAndKeepsBroadcasterInQuery()
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/helix/channel_points/custom_rewards", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1", request.RequestUri.Query);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var body = json.RootElement;
            Assert.Equal(13, body.EnumerateObject().Count());
            Assert.Equal("Highlight", body.GetProperty("title").GetString());
            Assert.Equal(2147483648, body.GetProperty("cost").GetInt64());
            Assert.Equal("Which moment?", body.GetProperty("prompt").GetString());
            Assert.False(body.GetProperty("is_enabled").GetBoolean());
            Assert.Equal("#9147FF", body.GetProperty("background_color").GetString());
            Assert.True(body.GetProperty("is_user_input_required").GetBoolean());
            Assert.True(body.GetProperty("is_max_per_stream_enabled").GetBoolean());
            Assert.Equal(5, body.GetProperty("max_per_stream").GetInt64());
            Assert.True(body.GetProperty("is_max_per_user_per_stream_enabled").GetBoolean());
            Assert.Equal(2, body.GetProperty("max_per_user_per_stream").GetInt64());
            Assert.True(body.GetProperty("is_global_cooldown_enabled").GetBoolean());
            Assert.Equal(60, body.GetProperty("global_cooldown_seconds").GetInt64());
            Assert.False(body.GetProperty("should_redemptions_skip_request_queue").GetBoolean());
            return TestHttpHandler.Json(Fixture("create-custom-rewards"));
        }));
        var result = await Client(http).CreateCustomRewardAsync("1", new()
        {
            Title = "Highlight", Cost = 2147483648, Prompt = "Which moment?", IsEnabled = false, BackgroundColor = "#9147FF", IsUserInputRequired = true,
            IsMaxPerStreamEnabled = true, MaxPerStream = 5, IsMaxPerUserPerStreamEnabled = true, MaxPerUserPerStream = 2,
            IsGlobalCooldownEnabled = true, GlobalCooldownSeconds = 60, ShouldRedemptionsSkipRequestQueue = false
        });
        Assert.Equal(2147483648, result.Data.Single().Cost);
        Assert.Equal("https://example.org/custom2", result.Data.Single().Image!.Url2x);
    }

    [Fact]
    public async Task RewardPatchSerializesAllOptionalFieldsIncludingPause()
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("/helix/channel_points/custom_rewards", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1&id=r1", request.RequestUri.Query);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var body = json.RootElement;
            Assert.Equal(14, body.EnumerateObject().Count());
            Assert.Equal("New title", body.GetProperty("title").GetString());
            Assert.Equal(2147483649, body.GetProperty("cost").GetInt64());
            Assert.Equal("", body.GetProperty("prompt").GetString());
            Assert.Equal("#abcdef", body.GetProperty("background_color").GetString());
            foreach (var field in new[] { "is_enabled", "is_user_input_required", "is_max_per_stream_enabled", "is_max_per_user_per_stream_enabled", "is_global_cooldown_enabled", "should_redemptions_skip_request_queue" })
                Assert.False(body.GetProperty(field).GetBoolean());
            foreach (var field in new[] { "max_per_stream", "max_per_user_per_stream", "global_cooldown_seconds" }) Assert.Equal(0, body.GetProperty(field).GetInt64());
            Assert.True(body.GetProperty("is_paused").GetBoolean());
            return TestHttpHandler.Json(Fixture("update-custom-reward"));
        }));
        await Client(http).UpdateCustomRewardAsync("1", "r1", new()
        {
            Title = "New title", Cost = 2147483649, Prompt = "", BackgroundColor = "#abcdef", IsEnabled = false, IsUserInputRequired = false,
            IsMaxPerStreamEnabled = false, MaxPerStream = 0, IsMaxPerUserPerStreamEnabled = false, MaxPerUserPerStream = 0,
            IsGlobalCooldownEnabled = false, GlobalCooldownSeconds = 0, IsPaused = true, ShouldRedemptionsSkipRequestQueue = false
        });
    }

    [Fact]
    public async Task PatchCanEnableExistingLimitWithoutResendingValueAndOnlyWritesSelectedField()
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal("{\"is_global_cooldown_enabled\":true}", await request.Content!.ReadAsStringAsync(ct));
            return TestHttpHandler.Json(Fixture("update-custom-reward"));
        }));
        await Client(http).UpdateCustomRewardAsync("1", "r1", new() { IsGlobalCooldownEnabled = true });
    }

    [Fact]
    public async Task RewardListingPreservesFalseFilterAndNullableResponseFields()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/channel_points/custom_rewards", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1&id=r1&id=r2&only_manageable_rewards=false", request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture("get-custom-reward")));
        }));
        var reward = (await Client(http).GetCustomRewardsAsync(new() { BroadcasterId = "1", Ids = ["r1", "r2"], OnlyManageableRewards = false })).Data.Single();
        Assert.Null(reward.Image);
        Assert.Null(reward.RedemptionsRedeemedCurrentStream);
        Assert.Null(reward.CooldownExpiresAt);
        Assert.Equal(2147483648, reward.MaxPerStreamSetting.MaxPerStream);
    }

    [Fact]
    public async Task DeleteRewardUsesBodylessRequestAndAcceptsNoContent()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("/helix/channel_points/custom_rewards", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1&id=r1", request.RequestUri.Query);
            Assert.Null(request.Content);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }));
        await Client(http).DeleteCustomRewardAsync("1", "r1");
    }

    [Fact]
    public async Task RedemptionListingAndUpdateSeparateQueryFromStatusBody()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal("/helix/channel_points/custom_rewards/redemptions", request.RequestUri!.AbsolutePath);
            if (++step == 1)
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("?broadcaster_id=1&reward_id=r1&status=UNFULFILLED&id=d1&id=d2&sort=NEWEST&first=50&after=a%2Bb", request.RequestUri.Query);
                Assert.Null(request.Content);
            }
            else
            {
                Assert.Equal(HttpMethod.Patch, request.Method);
                Assert.Equal("?broadcaster_id=1&reward_id=r1&id=d1&id=d2", request.RequestUri.Query);
                Assert.Equal("{\"status\":\"CANCELED\"}", await request.Content!.ReadAsStringAsync(ct));
            }
            return TestHttpHandler.Json(Fixture(step == 1 ? "get-custom-reward-redemption" : "update-redemption-status"));
        }));
        var client = Client(http);
        var page = await client.GetCustomRewardRedemptionsAsync(new() { BroadcasterId = "1", RewardId = "r1", Status = "UNFULFILLED", Ids = ["d1", "d2"], Sort = "NEWEST", First = 50, After = "a+b" });
        Assert.Equal(2147483648, page.Data.Single().Reward.Cost);
        Assert.Equal("next", page.Pagination!.Cursor);
        Assert.Equal("CANCELED", (await client.UpdateRedemptionStatusAsync(new() { BroadcasterId = "1", RewardId = "r1", Ids = ["d1", "d2"], Status = "CANCELED" })).Data.Single().Status);
        Assert.Equal(2, step);
    }

    [Theory]
    [InlineData(TwitchScopes.ChannelReadRedemptions)]
    [InlineData(TwitchScopes.ChannelManageRedemptions)]
    public async Task ReadsAcceptEitherScopeAndPermitIdsWithoutStatus(string scope)
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            calls++;
            return Task.FromResult(TestHttpHandler.Json(Fixture(request.RequestUri!.AbsolutePath.EndsWith("/redemptions", StringComparison.Ordinal) ? "get-custom-reward-redemption" : "get-custom-reward")));
        }));
        var client = Client(http, new("user", scopes: [scope], kind: TwitchTokenKind.User, userId: "1"));
        await client.GetCustomRewardsAsync(new() { BroadcasterId = "1" });
        await client.GetCustomRewardRedemptionsAsync(new() { BroadcasterId = "1", RewardId = "r1", Ids = ["d1"] });
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task AllOperationsRequireBroadcasterUserAndMutationsRequireManageScope()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("No request expected")));
        foreach (var token in new AccessToken[]
        {
            new("app", kind: TwitchTokenKind.App), new("user", scopes: [], kind: TwitchTokenKind.User, userId: "1"),
            new("other", scopes: [TwitchScopes.ChannelManageRedemptions], kind: TwitchTokenKind.User, userId: "other")
        })
        {
            foreach (var call in Operations(Client(http, token))) await Assert.ThrowsAsync<TwitchAuthorizationException>(call);
        }
        var reader = Client(http, new("reader", scopes: [TwitchScopes.ChannelReadRedemptions], kind: TwitchTokenKind.User, userId: "1"));
        foreach (var call in Operations(reader).Take(4)) await Assert.ThrowsAsync<TwitchAuthorizationException>(call);
    }

    [Fact]
    public async Task LimitsValidateOnlyEnabledSettingsAndRetainLargeCosts()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json(Fixture("create-custom-rewards"))); }));
        var client = Client(http);
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateCustomRewardAsync("1", new() { Title = new('x', 46), Cost = 1 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.CreateCustomRewardAsync("1", new() { Title = "Title", Cost = 0 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateCustomRewardAsync("1", new() { Title = "Title", Cost = 1, Prompt = new('x', 201) }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateCustomRewardAsync("1", "r1", new() { Title = "" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateCustomRewardAsync("1", "r1", new() { BackgroundColor = "#12345z" }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.CreateCustomRewardAsync("1", new() { Title = "Title", Cost = 1, IsMaxPerStreamEnabled = true }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.CreateCustomRewardAsync("1", new() { Title = "Title", Cost = 1, IsMaxPerUserPerStreamEnabled = true, MaxPerUserPerStream = 0 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.UpdateCustomRewardAsync("1", "r1", new() { IsGlobalCooldownEnabled = true, GlobalCooldownSeconds = 604801 }));
        await client.CreateCustomRewardAsync("1", new() { Title = string.Concat(Enumerable.Repeat("😀", 45)), Cost = long.MaxValue, Prompt = new('x', 200), IsMaxPerStreamEnabled = false, MaxPerStream = 0 });
        await client.UpdateCustomRewardAsync("1", "r1", new() { IsGlobalCooldownEnabled = true, GlobalCooldownSeconds = 604800 });
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RedemptionFiltersValidateStatusCasePageSizeAndFiftyIds()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"data\":[]}")); }));
        var client = Client(http);
        var ids = Enumerable.Range(1, 50).Select(i => i.ToString()).ToArray();
        await client.GetCustomRewardsAsync(new() { BroadcasterId = "1", Ids = ids });
        await client.GetCustomRewardRedemptionsAsync(new() { BroadcasterId = "1", RewardId = "r1", Ids = ids, First = 50 });
        await client.UpdateRedemptionStatusAsync(new() { BroadcasterId = "1", RewardId = "r1", Ids = ids, Status = "FULFILLED" });
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetCustomRewardsAsync(new() { BroadcasterId = "1", Ids = [.. ids, "extra"] }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetCustomRewardRedemptionsAsync(new() { BroadcasterId = "1", RewardId = "r1" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetCustomRewardRedemptionsAsync(new() { BroadcasterId = "1", RewardId = "r1", Status = "unfulfilled" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetCustomRewardRedemptionsAsync(new() { BroadcasterId = "1", RewardId = "r1", Status = "UNFULFILLED", Sort = "oldest" }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetCustomRewardRedemptionsAsync(new() { BroadcasterId = "1", RewardId = "r1", Status = "UNFULFILLED", First = 51 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateRedemptionStatusAsync(new() { BroadcasterId = "1", RewardId = "r1", Status = "FULFILLED" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateRedemptionStatusAsync(new() { BroadcasterId = "1", RewardId = "r1", Ids = ["d1"], Status = "UNFULFILLED" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateRedemptionStatusAsync(new() { BroadcasterId = "1", RewardId = "r1", Ids = [.. ids, "extra"], Status = "FULFILLED" }));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task RedemptionEnumerationSnapshotsFiltersAndPreservesInitialCursor()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal("?broadcaster_id=1&reward_id=r1&status=UNFULFILLED&id=d1&sort=OLDEST&first=5&after=" + (++step == 1 ? "initial" : "next"), request.RequestUri!.Query);
            return Task.FromResult(TestHttpHandler.Json(step == 1 ? Fixture("get-custom-reward-redemption") : "{\"data\":[],\"pagination\":{}}"));
        }));
        var ids = new List<string> { "d1" };
        var items = Client(http).EnumerateCustomRewardRedemptionsAsync(new() { BroadcasterId = "1", RewardId = "r1", Status = "UNFULFILLED", Ids = ids, Sort = "OLDEST", First = 5, After = "initial" });
        ids.Clear();
        var count = 0;
        await foreach (var _ in items) count++;
        Assert.Equal(1, count);
        Assert.Equal(2, step);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task OwnershipAndMissingRewardErrorsArePreservedForAllOperations(HttpStatusCode status)
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"error\":\"Reward error\",\"message\":\"not manageable or unavailable\"}", status)); }));
        foreach (var call in Operations(Client(http)))
        {
            var error = await Assert.ThrowsAsync<TwitchApiException>(call);
            Assert.Equal(status, error.StatusCode);
            Assert.Equal("not manageable or unavailable", error.Message);
        }
        Assert.Equal(6, calls);
    }

    private static Func<Task>[] Operations(ChannelPointsClient client) =>
    [
        () => client.CreateCustomRewardAsync("1", new() { Title = "Title", Cost = 1 }),
        () => client.UpdateCustomRewardAsync("1", "r1", new() { IsPaused = true }),
        () => client.DeleteCustomRewardAsync("1", "r1"),
        () => client.UpdateRedemptionStatusAsync(new() { BroadcasterId = "1", RewardId = "r1", Ids = ["d1"], Status = "FULFILLED" }),
        () => client.GetCustomRewardsAsync(new() { BroadcasterId = "1" }),
        () => client.GetCustomRewardRedemptionsAsync(new() { BroadcasterId = "1", RewardId = "r1", Status = "UNFULFILLED" })
    ];
}
