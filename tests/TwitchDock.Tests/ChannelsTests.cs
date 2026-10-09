using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TwitchDock.Core;
using TwitchDock.Helix;
using TwitchDock.Helix.Clients;
using TwitchDock.Helix.Models;

namespace TwitchDock.Tests;

public sealed class ChannelsTests
{
    private static ChannelsClient Client(HttpClient http, AccessToken? token = null) => new HelixClient(new(http,
        new StaticAccessTokenProvider(token ?? new("token")), new() { ClientId = "client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 })).Channels;
    private static string Fixture(string id) => ContractAssertions.Fixture("helix-channels.json", id);

    public static IEnumerable<object[]> Contracts()
    {
        yield return ["get-channel-editors", HelixJsonContext.Default.HelixPageChannelEditor];
        yield return ["get-followed-channels", HelixJsonContext.Default.HelixPageFollowedChannel];
        yield return ["get-channel-followers", HelixJsonContext.Default.HelixPageChannelFollower];
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void ResponseContractsPreserveEveryDocumentedField(string id, JsonTypeInfo type)
        => ContractAssertions.Verify("helix-channels.json", id, type);

    [Fact]
    public async Task ChannelPatchSeparatesBroadcasterQueryAndPreservesEmptyAndFalseValues()
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("/helix/channels", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1", request.RequestUri.Query);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var body = json.RootElement;
            Assert.False(body.TryGetProperty("broadcaster_id", out _));
            Assert.Equal("", body.GetProperty("game_id").GetString());
            Assert.Equal("en", body.GetProperty("broadcaster_language").GetString());
            Assert.Equal("A title", body.GetProperty("title").GetString());
            Assert.Equal(0, body.GetProperty("delay").GetInt32());
            Assert.Equal(0, body.GetProperty("tags").GetArrayLength());
            Assert.False(body.GetProperty("is_branded_content").GetBoolean());
            var label = body.GetProperty("content_classification_labels")[0];
            Assert.Equal("ProfanityVulgarity", label.GetProperty("id").GetString());
            Assert.False(label.GetProperty("is_enabled").GetBoolean());
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        await Client(http, new("token", scopes: [TwitchScopes.ChannelManageBroadcast], kind: TwitchTokenKind.User, userId: "1"))
            .ModifyChannelInformationAsync(new()
            {
                BroadcasterId = "1", GameId = "", BroadcasterLanguage = "en", Title = "A title", Delay = 0, Tags = [], IsBrandedContent = false,
                ContentClassificationLabels = [new() { Id = "ProfanityVulgarity", IsEnabled = false }]
            });
    }

    [Fact]
    public async Task SingleFalseFieldCountsAsUpdateAndOtherFieldsAreOmitted()
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal("{\"is_branded_content\":false}", await request.Content!.ReadAsStringAsync(ct));
            return new(HttpStatusCode.NoContent);
        }));
        await Client(http).ModifyChannelInformationAsync(new() { BroadcasterId = "1", IsBrandedContent = false });
    }

    [Fact]
    public async Task ChannelPatchValidatesDocumentedBoundsAndAcceptsLimits()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); }));
        var client = Client(http);
        ModifyChannelInformationRequest[] invalid =
        [
            new() { BroadcasterId = "1" }, new() { BroadcasterId = "1", Title = "" }, new() { BroadcasterId = "1", Title = new('x', 141) },
            new() { BroadcasterId = "1", Tags = Enumerable.Repeat("Tag", 11).ToArray() },
            new() { BroadcasterId = "1", Tags = [new('x', 26)] }, new() { BroadcasterId = "1", Tags = [""] }, new() { BroadcasterId = "1", Tags = ["two words"] }
        ];
        foreach (var request in invalid) await Assert.ThrowsAsync<ArgumentException>(() => client.ModifyChannelInformationAsync(request));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.ModifyChannelInformationAsync(new() { BroadcasterId = "1", Delay = -1 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.ModifyChannelInformationAsync(new() { BroadcasterId = "1", Delay = 901 }));
        await client.ModifyChannelInformationAsync(new() { BroadcasterId = "1", Delay = 900, Title = string.Concat(Enumerable.Repeat("😀", 140)), Tags = Enumerable.Range(0, 10).Select(i => new string('x', 24) + i).ToArray() });
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task EditorsAndFollowPagesEncodeEveryDocumentedFilter()
    {
        var step = 0;
        string[] paths = ["editors", "followed", "followers"];
        string[] queries = ["?broadcaster_id=1", "?user_id=2&broadcaster_id=1&first=100&after=a%2Bb", "?broadcaster_id=1&user_id=2&first=1&after=a%2Bb"];
        string[] fixtures = ["get-channel-editors", "get-followed-channels", "get-channel-followers"];
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/channels/" + paths[step], request.RequestUri!.AbsolutePath);
            Assert.Equal(queries[step], request.RequestUri.Query);
            Assert.Null(request.Content);
            return Task.FromResult(TestHttpHandler.Json(Fixture(fixtures[step++])));
        }));
        var client = Client(http);
        Assert.Equal("Editor", (await client.GetChannelEditorsAsync("1")).Data.Single().UserName);
        Assert.Equal(3, (await client.GetFollowedChannelsAsync(new() { UserId = "2", BroadcasterId = "1", First = 100, After = "a+b" })).Total);
        Assert.Equal(42, (await client.GetChannelFollowersAsync(new() { BroadcasterId = "1", UserId = "2", First = 1, After = "a+b" })).Total);
        Assert.Equal(3, step);
    }

    [Fact]
    public async Task FollowerTotalIsAvailableWithoutScopeButUserFilterRequiresIt()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"data\":[],\"pagination\":{},\"total\":42}")); }));
        var client = Client(http, new("token", scopes: [], kind: TwitchTokenKind.User, userId: "unrelated-user"));
        var page = await client.GetChannelFollowersAsync(new() { BroadcasterId = "1" });
        Assert.Empty(page.Data);
        Assert.Equal(42, page.Total);
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => client.GetChannelFollowersAsync(new() { BroadcasterId = "1", UserId = "2" }));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, new("app", kind: TwitchTokenKind.App)).GetChannelFollowersAsync(new() { BroadcasterId = "1" }));
        // A moderator's token need not belong to the broadcaster; Twitch verifies that role.
        await Client(http, new("mod", scopes: [TwitchScopes.ModeratorReadFollowers], kind: TwitchTokenKind.User, userId: "moderator"))
            .GetChannelFollowersAsync(new() { BroadcasterId = "1", UserId = "2" });
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task OwnerOperationsRequireScopesAndMatchingUserIdentity()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("No request expected")));
        var unscoped = Client(http, new("token", scopes: [], kind: TwitchTokenKind.User, userId: "1"));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => unscoped.GetChannelEditorsAsync("1"));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => unscoped.GetFollowedChannelsAsync(new() { UserId = "1" }));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => unscoped.ModifyChannelInformationAsync(new() { BroadcasterId = "1", Title = "Title" }));
        var wrongOwner = Client(http, new("token", scopes: [TwitchScopes.ChannelManageBroadcast, TwitchScopes.ChannelReadEditors, TwitchScopes.UserReadFollows], kind: TwitchTokenKind.User, userId: "other"));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => wrongOwner.GetChannelEditorsAsync("1"));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => wrongOwner.GetFollowedChannelsAsync(new() { UserId = "1" }));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => wrongOwner.ModifyChannelInformationAsync(new() { BroadcasterId = "1", Title = "Title" }));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FollowEnumeratorsPreserveFiltersAndAdvanceCursor(bool followed)
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            var query = followed ? "?user_id=2&broadcaster_id=1" : "?broadcaster_id=1&user_id=2";
            Assert.Equal(query + "&first=5&after=" + (++step == 1 ? "initial" : "next"), request.RequestUri!.Query);
            return Task.FromResult(TestHttpHandler.Json(step == 1 ? Fixture(followed ? "get-followed-channels" : "get-channel-followers") : "{\"data\":[],\"pagination\":{},\"total\":0}"));
        }));
        var client = Client(http);
        var count = 0;
        if (followed)
            await foreach (var _ in client.EnumerateFollowedChannelsAsync(new() { UserId = "2", BroadcasterId = "1", First = 5, After = "initial" })) count++;
        else
            await foreach (var _ in client.EnumerateFollowersAsync(new() { BroadcasterId = "1", UserId = "2", First = 5, After = "initial" })) count++;
        Assert.Equal(1, count);
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task FollowPagesValidateBoundsAndCancellationBeforeHttp()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("No request expected")));
        var client = Client(http);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetFollowedChannelsAsync(new() { UserId = "1", First = 0 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetChannelFollowersAsync(new() { BroadcasterId = "1", First = 101 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetChannelFollowersAsync(new() { BroadcasterId = "1", UserId = " " }));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in client.EnumerateFollowersAsync(new() { BroadcasterId = "1" }, cts.Token)) { }
        });
    }

    [Fact]
    public async Task ChannelFailuresRemainStructuredAndPatchConflictIsNotRetried()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            calls++;
            return Task.FromResult(request.Method == HttpMethod.Patch
                ? TestHttpHandler.Json("{\"error\":\"Conflict\",\"message\":\"branded flag changed too often\"}", HttpStatusCode.Conflict)
                : TestHttpHandler.Json("{\"error\":\"Unauthorized\",\"message\":\"denied\"}", HttpStatusCode.Unauthorized));
        }));
        var client = Client(http);
        var conflict = await Assert.ThrowsAsync<TwitchApiException>(() => client.ModifyChannelInformationAsync(new() { BroadcasterId = "1", IsBrandedContent = false }));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Null(conflict.ExistingSubscriptionId);
        Func<Task>[] reads = [() => client.GetChannelEditorsAsync("1"), () => client.GetFollowedChannelsAsync(new() { UserId = "1" }), () => client.GetChannelFollowersAsync(new() { BroadcasterId = "1" })];
        foreach (var read in reads) Assert.Equal(HttpStatusCode.Unauthorized, (await Assert.ThrowsAsync<TwitchApiException>(read)).StatusCode);
        Assert.Equal(4, calls);
    }
}
