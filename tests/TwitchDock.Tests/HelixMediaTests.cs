using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TwitchDock.Core;
using TwitchDock.Helix;

namespace TwitchDock.Tests;

public sealed class HelixMediaTests
{
    private const string Fixtures = "helix-media.json";
    private static HelixClient Client(HttpClient http, AccessToken? token = null) => new(new(http, new StaticAccessTokenProvider(token ?? new("token")), new() { ClientId = "client", MaxTransientRetries = 0 }));
    private static string Fixture(string id) => ContractAssertions.Fixture(Fixtures, id);

    public static IEnumerable<object[]> Contracts()
    {
        var c = HelixJsonContext.Default;
        yield return ["create-clip", c.HelixPageCreatedClip];
        yield return ["create-clip-from-vod", c.HelixPageCreatedClip];
        yield return ["get-clips", c.HelixPageTwitchClip];
        yield return ["get-clips-download", c.HelixPageClipDownload];
        yield return ["get-videos", c.HelixPageTwitchVideo];
        yield return ["delete-videos", c.HelixPageString];
        yield return ["get-charity-campaign", c.HelixPageCharityCampaign];
        yield return ["get-charity-campaign-donations", c.HelixPageCharityDonation];
        yield return ["get-channel-teams", c.HelixPageChannelTeam];
        yield return ["get-teams", c.HelixPageTwitchTeam];
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void ResponseContractsCoverAllDocumentedFieldsIncludingNulls(string id, JsonTypeInfo type) => ContractAssertions.Verify(Fixtures, id, type);

    [Theory]
    [InlineData("create-clip", "POST", "clips", "broadcaster_id=1&title=Test%20%26%20clip&duration=5.1")]
    [InlineData("create-clip-from-vod", "POST", "videos/clips", "editor_id=2&broadcaster_id=1&vod_id=video-1&vod_offset=100&duration=5.1&title=Test%20%26%20clip")]
    [InlineData("get-clips", "GET", "clips", "broadcaster_id=1&started_at=2026-10-01T00%3A00%3A00.0000000%2B00%3A00&ended_at=2026-10-02T00%3A00%3A00.0000000%2B00%3A00&first=5&before=previous&is_featured=false")]
    [InlineData("get-clips-download", "GET", "clips/downloads", "editor_id=2&broadcaster_id=1&clip_id=clip-1&clip_id=clip-2")]
    [InlineData("get-videos", "GET", "videos", "user_id=1&period=week&sort=views&type=archive&first=5&after=next")]
    [InlineData("delete-videos", "DELETE", "videos", "id=video-1&id=video-2")]
    [InlineData("get-charity-campaign", "GET", "charity/campaigns", "broadcaster_id=1")]
    [InlineData("get-charity-campaign-donations", "GET", "charity/donations", "broadcaster_id=1&first=5&after=next")]
    [InlineData("get-channel-teams", "GET", "teams/channel", "broadcaster_id=1")]
    [InlineData("get-teams", "GET", "teams", "name=example%20%26%20team")]
    public async Task EndpointUsesDocumentedMethodPathAndAllSelectedParameters(string id, string method, string path, string query)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            using var http = new HttpClient(new TestHttpHandler((request, _) =>
            {
                Assert.Equal(method, request.Method.Method);
                Assert.Equal("/helix/" + path, request.RequestUri!.AbsolutePath);
                Assert.Equal("?" + query, request.RequestUri.Query);
                Assert.Null(request.Content);
                return Task.FromResult(TestHttpHandler.Json(Fixture(id), id.StartsWith("create-", StringComparison.Ordinal) ? HttpStatusCode.Accepted : HttpStatusCode.OK));
            }));
            await Invoke(Client(http), id);
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    [Theory]
    [InlineData("create-clip")]
    [InlineData("create-clip-from-vod")]
    [InlineData("get-clips")]
    [InlineData("get-clips-download")]
    [InlineData("get-videos")]
    [InlineData("delete-videos")]
    [InlineData("get-charity-campaign")]
    [InlineData("get-charity-campaign-donations")]
    [InlineData("get-channel-teams")]
    [InlineData("get-teams")]
    public async Task EndpointExposesStructuredHttpErrors(string id)
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal("Bearer token", request.Headers.Authorization!.ToString());
            Assert.Equal("client", Assert.Single(request.Headers.GetValues("Client-Id")));
            return Task.FromResult(TestHttpHandler.Json("{\"error\":\"Forbidden\",\"message\":\"Resource not available to this user\"}", HttpStatusCode.Forbidden));
        }));
        var exception = await Assert.ThrowsAsync<TwitchApiException>(() => Invoke(Client(http), id));
        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Equal("Resource not available to this user", exception.Message);
    }

    [Theory]
    [InlineData("editor:manage:clips")]
    [InlineData("channel:manage:clips")]
    public async Task ClipManagementAcceptsEitherScope(string scope)
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json(Fixture("get-clips-download")))));
        await Client(http, new("token", scopes: [scope], kind: TwitchTokenKind.User, userId: "2")).Clips.GetClipsDownloadAsync(new() { EditorId = "2", BroadcasterId = "1", ClipIds = ["clip-1"] });
    }

    [Fact]
    public async Task ClipManagementReportsAlternativeScopesWithoutRequiringBoth()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("Must not send")));
        var ex = await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, new("token", scopes: [], kind: TwitchTokenKind.User)).Clips.GetClipsDownloadAsync(new() { EditorId = "2", BroadcasterId = "1", ClipIds = ["clip-1"] }));
        Assert.Empty(ex.MissingScopes);
        Assert.Equal(new[] { "editor:manage:clips", "channel:manage:clips" }, ex.RequiredAnyOfScopes);
    }

    [Fact]
    public async Task HandlesIdAndGameFiltersAndOptionalNullValues()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            step++;
            Assert.Equal(step switch
            {
                1 => "?id=clip-1&id=clip-2", 2 => "?game_id=game-1", 3 => "?id=video-1&id=video-2",
                4 => "?game_id=game-1&language=de&period=all&sort=time&type=all&first=100", _ => "?id=team-1"
            }, request.RequestUri!.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture(step < 3 ? "get-clips" : step < 5 ? "get-videos" : "get-teams")));
        }));
        var client = Client(http);
        Assert.Null((await client.Clips.GetClipsAsync(new() { Ids = ["clip-1", "clip-2"] })).Data.Single().VodOffset);
        await client.Clips.GetClipsAsync(new() { GameId = "game-1" });
        Assert.Null((await client.Videos.GetVideosAsync(new() { Ids = ["video-1", "video-2"] })).Data.Single().StreamId);
        await client.Videos.GetVideosAsync(new() { GameId = "game-1", Language = "de", Period = "all", Sort = "time", Type = "all", First = 100 });
        var team = (await client.Teams.GetTeamsAsync(new() { Id = "team-1" })).Data.Single();
        Assert.Null(team.Banner);
        Assert.Contains("<p>", team.Info);
        Assert.Equal("1", Assert.Single(team.Users).UserId);
    }

    [Fact]
    public async Task CharityPreservesMinorUnitsAndOptionalTarget()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json(Fixture("get-charity-campaign")))));
        var campaign = (await Client(http).Charity.GetCharityCampaignAsync("1")).Data.Single();
        Assert.Equal(550, campaign.CurrentAmount.Value);
        Assert.Equal(2, campaign.CurrentAmount.DecimalPlaces);
        Assert.Equal(5_000_000_000, campaign.TargetAmount!.Value);
        var optional = JsonSerializer.Deserialize(Fixture("get-charity-campaign").Replace("\"target_amount\":{\"value\":5000000000,\"decimal_places\":2,\"currency\":\"USD\"}", "\"target_amount\":null"), HelixJsonContext.Default.HelixPageCharityCampaign)!;
        Assert.Null(optional.Data.Single().TargetAmount);
    }

    [Fact]
    public async Task VideoGameEnumerationDoesNotFollowUnsupportedCursor()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) => { calls++; Assert.Equal("?game_id=game-1", request.RequestUri!.Query); return Task.FromResult(TestHttpHandler.Json(Fixture("get-videos"))); }));
        var count = 0;
        await foreach (var _ in Client(http).Videos.EnumerateVideosAsync(new() { GameId = "game-1" })) count++;
        Assert.Equal(1, count);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task InvalidCombinationsAndLimitsFailBeforeHttp()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("Must not send invalid parameters")));
        var client = Client(http);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.Clips.CreateClipAsync(new() { BroadcasterId = "1", Duration = 5.11m }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.Clips.CreateClipFromVodAsync(new() { EditorId = "2", BroadcasterId = "1", VodId = "v", Title = "title", VodOffset = 29 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Clips.GetClipsAsync(new() { GameId = "g", BroadcasterId = "b" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Clips.GetClipsDownloadAsync(new() { EditorId = "2", BroadcasterId = "1", ClipIds = Enumerable.Repeat("c", 11).ToArray() }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Videos.GetVideosAsync(new() { UserId = "1", GameId = "g" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Videos.GetVideosAsync(new() { GameId = "g", After = "cursor" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Videos.DeleteVideosAsync(Enumerable.Repeat("v", 6).ToArray()));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Teams.GetTeamsAsync(new() { Name = "team", Id = "t" }));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, new("token", kind: TwitchTokenKind.App)).Charity.GetCharityCampaignAsync("1"));
    }

    private static Task Invoke(HelixClient client, string id) => id switch
    {
        "create-clip" => client.Clips.CreateClipAsync(new() { BroadcasterId = "1", Title = "Test & clip", Duration = 5.1m }),
        "create-clip-from-vod" => client.Clips.CreateClipFromVodAsync(new() { EditorId = "2", BroadcasterId = "1", VodId = "video-1", VodOffset = 100, Duration = 5.1m, Title = "Test & clip" }),
        "get-clips" => client.Clips.GetClipsAsync(new() { BroadcasterId = "1", StartedAt = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), EndedAt = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero), First = 5, Before = "previous", IsFeatured = false }),
        "get-clips-download" => client.Clips.GetClipsDownloadAsync(new() { EditorId = "2", BroadcasterId = "1", ClipIds = ["clip-1", "clip-2"] }),
        "get-videos" => client.Videos.GetVideosAsync(new() { UserId = "1", Period = "week", Sort = "views", Type = "archive", First = 5, After = "next" }),
        "delete-videos" => client.Videos.DeleteVideosAsync(["video-1", "video-2"]),
        "get-charity-campaign" => client.Charity.GetCharityCampaignAsync("1"),
        "get-charity-campaign-donations" => client.Charity.GetCharityCampaignDonationsAsync(new() { BroadcasterId = "1", First = 5, After = "next" }),
        "get-channel-teams" => client.Teams.GetChannelTeamsAsync("1"),
        "get-teams" => client.Teams.GetTeamsAsync(new() { Name = "example & team" }),
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };
}
