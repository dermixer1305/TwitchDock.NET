using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TwitchSdk.Core;
using TwitchSdk.Helix;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Tests;

public sealed class HelixGroupsTests
{
    private static HelixClient Client(HttpClient http) => new(new(http, new StaticAccessTokenProvider(new("test-token")), new() { ClientId = "test-client", MaxRateLimitRetries = 0, MaxTransientRetries = 0 }));
    private static string Fixture(string id) => ContractAssertions.Fixture("helix-groups.json", id);

    public static IEnumerable<object[]> Contracts()
    {
        var context = HelixJsonContext.Default;
        yield return ["start-commercial", context.HelixPageCommercialResult];
        yield return ["get-ad-schedule", context.HelixPageAdSchedule];
        yield return ["snooze-next-ad", context.HelixPageAdSnoozeResult];
        yield return ["get-extension-analytics", context.HelixPageExtensionAnalyticsReport];
        yield return ["get-game-analytics", context.HelixPageGameAnalyticsReport];
        yield return ["get-top-games", context.HelixPageTwitchGame];
        yield return ["get-games", context.HelixPageTwitchGame];
        yield return ["search-categories", context.HelixPageCategorySearchResult];
        yield return ["search-channels", context.HelixPageChannelSearchResult];
        yield return ["get-creator-goals", context.HelixPageCreatorGoal];
        yield return ["start-a-raid", context.HelixPageRaidResult];
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void ResponseContractsPreserveEveryDocumentedField(string id, JsonTypeInfo type)
        => ContractAssertions.Verify("helix-groups.json", id, type);

    [Fact]
    public async Task AdsUseJsonForCommercialAndQueriesForScheduleAndSnooze()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            step++;
            var id = step switch { 1 => "start-commercial", 2 => "get-ad-schedule", _ => "snooze-next-ad" };
            Assert.Equal(step == 2 ? HttpMethod.Get : HttpMethod.Post, request.Method);
            if (step == 1)
            {
                Assert.Equal("/helix/channels/commercial", request.RequestUri!.AbsolutePath);
                Assert.Equal("", request.RequestUri.Query);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal("channel-1", body.RootElement.GetProperty("broadcaster_id").GetString());
                Assert.Equal(60, body.RootElement.GetProperty("length").GetInt32());
            }
            else
            {
                Assert.Equal(step == 2 ? "/helix/channels/ads" : "/helix/channels/ads/schedule/snooze", request.RequestUri!.AbsolutePath);
                Assert.Equal("?broadcaster_id=channel-1", request.RequestUri.Query);
                Assert.Null(request.Content);
            }
            return TestHttpHandler.Json(Fixture(id));
        }));
        var ads = Client(http).Ads;
        Assert.Equal(480, (await ads.StartCommercialAsync(new() { BroadcasterId = "channel-1", Length = 60 })).Data.Single().RetryAfter);
        var schedule = (await ads.GetAdScheduleAsync("channel-1")).Data.Single();
        Assert.Equal("", schedule.NextAdAt);
        Assert.Equal("", schedule.LastAdAt);
        Assert.Equal(1, (await ads.SnoozeNextAdAsync("channel-1")).Data.Single().SnoozeCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnalyticsEncodesAllFiltersAndUppercaseReportUrl(bool extension)
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(extension ? "/helix/analytics/extensions" : "/helix/analytics/games", request.RequestUri!.AbsolutePath);
            Assert.Equal("?type=overview_v2&started_at=2026-10-01T00%3A00%3A00Z&ended_at=2026-10-05T00%3A00%3A00Z&first=100&after=a%2Bb&" + (extension ? "extension_id=ext-1" : "game_id=game-1"), request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture(extension ? "get-extension-analytics" : "get-game-analytics")));
        }));
        var analytics = Client(http).Analytics;
        var url = extension
            ? (await analytics.GetExtensionAnalyticsAsync(new() { ExtensionId = "ext-1", Type = "overview_v2", StartedAt = new(2026, 10, 1), EndedAt = new(2026, 10, 5), First = 100, After = "a+b" })).Data.Single().Url
            : (await analytics.GetGameAnalyticsAsync(new() { GameId = "game-1", Type = "overview_v2", StartedAt = new(2026, 10, 1), EndedAt = new(2026, 10, 5), First = 100, After = "a+b" })).Data.Single().Url;
        Assert.Equal("https://example.org/report.csv", url);
    }

    [Fact]
    public async Task AnalyticsOmittedDatesRemainAbsentAndResourceFilterStopsPagination()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            calls++;
            Assert.Equal("?extension_id=ext-1", request.RequestUri!.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture("get-extension-analytics")));
        }));
        var reports = new List<ExtensionAnalyticsReport>();
        await foreach (var report in Client(http).Analytics.EnumerateExtensionAnalyticsAsync(new() { ExtensionId = "ext-1" })) reports.Add(report);
        Assert.Single(reports);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GamesCombinesIdNameAndIgdbFiltersAndPreservesRepeatedValues()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal("/helix/games", request.RequestUri!.AbsolutePath);
            Assert.Equal("?id=1&id=2&name=Test%20%26%20Game&igdb_id=igdb-1", request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture("get-games")));
        }));
        var result = await Client(http).Games.GetGamesAsync(new() { Ids = ["1", "2"], Names = ["Test & Game"], IgdbIds = ["igdb-1"] });
        Assert.Equal("", Assert.Single(result.Data).IgdbId);
    }

    [Fact]
    public async Task TopGamesSupportsBackwardPagingAndForwardEnumeration()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            calls++;
            Assert.Equal("/helix/games/top", request.RequestUri!.AbsolutePath);
            Assert.Equal(calls switch { 1 => "?first=1&before=previous", 2 => "?first=1&after=start", _ => "?first=1&after=next" }, request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(calls < 3 ? Fixture("get-top-games") : "{\"data\":[],\"pagination\":{}}"));
        }));
        var games = Client(http).Games;
        await games.GetTopGamesAsync(new() { First = 1, Before = "previous" });
        var rows = new List<TwitchGame>();
        await foreach (var game in games.EnumerateTopGamesAsync(new() { First = 1, After = "start" })) rows.Add(game);
        Assert.Single(rows);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task SearchEncodesUnescapedTextAndExplicitFalseAndReadsOfflineChannels()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            step++;
            Assert.Equal(step == 1 ? "/helix/search/categories" : "/helix/search/channels", request.RequestUri!.AbsolutePath);
            Assert.Equal(step == 1 ? "?query=%23a%20%26%20b&first=5&after=next" : "?query=%23a%20%26%20b&live_only=false&first=5&after=next", request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture(step == 1 ? "search-categories" : "search-channels")));
        }));
        var search = Client(http).Search;
        Assert.Single((await search.SearchCategoriesAsync(new() { Query = "#a & b", First = 5, After = "next" })).Data);
        var channel = (await search.SearchChannelsAsync(new() { Query = "#a & b", LiveOnly = false, First = 5, After = "next" })).Data.Single();
        Assert.False(channel.IsLive);
        Assert.Equal("", channel.StartedAt);
        Assert.Equal("deutsch", Assert.Single(channel.Tags));
    }

    [Fact]
    public async Task GoalsAndRaidsHandleFutureGoalTypesAndBodylessMutations()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            step++;
            Assert.Equal(step switch { 1 => HttpMethod.Get, 2 => HttpMethod.Post, _ => HttpMethod.Delete }, request.Method);
            Assert.Equal(step == 1 ? "/helix/goals" : "/helix/raids", request.RequestUri!.AbsolutePath);
            Assert.Equal(step == 2 ? "?from_broadcaster_id=channel-1&to_broadcaster_id=channel-2" : "?broadcaster_id=channel-1", request.RequestUri.Query);
            Assert.Null(request.Content);
            return Task.FromResult(step == 3 ? new(HttpStatusCode.NoContent) : TestHttpHandler.Json(Fixture(step == 1 ? "get-creator-goals" : "start-a-raid")));
        }));
        var client = Client(http);
        Assert.Equal("future-goal-kind", (await client.Goals.GetCreatorGoalsAsync("channel-1")).Data.Single().Type);
        Assert.Equal(2026, (await client.Raids.StartRaidAsync("channel-1", "channel-2")).Data.Single().CreatedAt.Year);
        await client.Raids.CancelRaidAsync("channel-1");
    }

    [Fact]
    public async Task InvalidFiltersFailBeforeHttp()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("Must not send invalid input.")));
        var client = Client(http);
        await Assert.ThrowsAsync<ArgumentException>(() => client.Games.GetGamesAsync(new()));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Games.GetGamesAsync(new() { Ids = Enumerable.Repeat("1", 100).ToArray(), IgdbIds = ["2"] }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.Games.GetTopGamesAsync(new() { First = 101 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Games.GetTopGamesAsync(new() { Before = "b", After = "a" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Analytics.GetGameAnalyticsAsync(new() { StartedAt = new(2026, 10, 1) }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Analytics.GetExtensionAnalyticsAsync(new() { StartedAt = new(2026, 10, 5), EndedAt = new(2026, 10, 1) }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Search.SearchChannelsAsync(new() { Query = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Raids.StartRaidAsync("same", "same"));
    }

    [Theory]
    [InlineData("start-commercial")]
    [InlineData("get-ad-schedule")]
    [InlineData("snooze-next-ad")]
    [InlineData("get-extension-analytics")]
    [InlineData("get-game-analytics")]
    [InlineData("get-top-games")]
    [InlineData("get-games")]
    [InlineData("search-categories")]
    [InlineData("search-channels")]
    [InlineData("get-creator-goals")]
    [InlineData("start-a-raid")]
    [InlineData("cancel-a-raid")]
    public async Task EachEndpointPropagatesStructuredAuthorizationErrors(string id)
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal("Bearer test-token", request.Headers.Authorization!.ToString());
            Assert.Equal("test-client", Assert.Single(request.Headers.GetValues("Client-Id")));
            return Task.FromResult(TestHttpHandler.Json("{\"error\":\"Forbidden\",\"message\":\"Authorization missing for this resource\"}", HttpStatusCode.Forbidden));
        }));
        var client = Client(http);
        var exception = await Assert.ThrowsAsync<TwitchApiException>(() => Invoke(client, id));
        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Equal("Forbidden", exception.Error);
    }

    private static Task Invoke(HelixClient client, string id) => id switch
    {
        "start-commercial" => client.Ads.StartCommercialAsync(new() { BroadcasterId = "1", Length = 30 }),
        "get-ad-schedule" => client.Ads.GetAdScheduleAsync("1"),
        "snooze-next-ad" => client.Ads.SnoozeNextAdAsync("1"),
        "get-extension-analytics" => client.Analytics.GetExtensionAnalyticsAsync(),
        "get-game-analytics" => client.Analytics.GetGameAnalyticsAsync(),
        "get-top-games" => client.Games.GetTopGamesAsync(),
        "get-games" => client.Games.GetGamesAsync(new() { Ids = ["1"] }),
        "search-categories" => client.Search.SearchCategoriesAsync(new() { Query = "test" }),
        "search-channels" => client.Search.SearchChannelsAsync(new() { Query = "test" }),
        "get-creator-goals" => client.Goals.GetCreatorGoalsAsync("1"),
        "start-a-raid" => client.Raids.StartRaidAsync("1", "2"),
        "cancel-a-raid" => client.Raids.CancelRaidAsync("1"),
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };

}
