using System.Net;
using System.Text.Json.Serialization.Metadata;
using TwitchDock.Core;
using TwitchDock.Helix;
using TwitchDock.Helix.Clients;
using TwitchDock.Helix.Models;

namespace TwitchDock.Tests;

public sealed class ModerationEnforcementTests
{
    private const string FixtureFile = "helix-moderation-enforcement.json";
    private static readonly string[] AllScopes =
    [
        TwitchScopes.ModerationRead, TwitchScopes.ModeratorManageAutomod, TwitchScopes.ModeratorManageAutomodSettings, TwitchScopes.ModeratorManageBannedUsers,
        TwitchScopes.ModeratorManageUnbanRequests, TwitchScopes.ModeratorManageBlockedTerms, TwitchScopes.ModeratorManageChatMessages
    ];

    private static ModerationClient Client(HttpClient http, AccessToken? token = null) => new HelixClient(new(http,
        new StaticAccessTokenProvider(token ?? new("token")), new() { ClientId = "client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 })).Moderation;
    private static string Fixture(string id) => ContractAssertions.Fixture(FixtureFile, id);
    private static HttpClient Recording(List<HttpRequestMessage> requests) => new(new TestHttpHandler((request, _) =>
    {
        requests.Add(request);
        return Task.FromResult(TestHttpHandler.Json("{\"data\":[]}"));
    }));

    public static IEnumerable<object[]> Contracts()
    {
        yield return ["check-automod-status", HelixJsonContext.Default.HelixPageAutoModCheckResult];
        yield return ["get-automod-settings", HelixJsonContext.Default.HelixPageAutoModSettings];
        yield return ["update-automod-settings", HelixJsonContext.Default.HelixPageAutoModSettings];
        yield return ["get-banned-users", HelixJsonContext.Default.HelixPageBannedUser];
        yield return ["ban-user", HelixJsonContext.Default.HelixPageBanUserResult];
        yield return ["get-unban-requests", HelixJsonContext.Default.HelixPageUnbanRequest];
        yield return ["resolve-unban-requests", HelixJsonContext.Default.HelixPageUnbanRequest];
        yield return ["get-blocked-terms", HelixJsonContext.Default.HelixPageBlockedTerm];
        yield return ["add-blocked-term", HelixJsonContext.Default.HelixPageBlockedTerm];
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void ResponseContractsPreserveEveryDocumentedField(string id, JsonTypeInfo type) => ContractAssertions.Verify(FixtureFile, id, type);

    [Fact]
    public async Task CheckAutoModStatusPostsDataArrayAndKeepsBroadcasterInQuery()
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/helix/moderation/enforcements/status", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1", request.RequestUri.Query);
            Assert.Equal("{\"data\":[{\"msg_id\":\"a\",\"msg_text\":\"Hello\"},{\"msg_id\":\"b\",\"msg_text\":\"Boo\"}]}", await request.Content!.ReadAsStringAsync(ct));
            return TestHttpHandler.Json(Fixture("check-automod-status"));
        }));
        var result = await Client(http).CheckAutoModStatusAsync(new() { BroadcasterId = "1", Data = [new() { MsgId = "a", MsgText = "Hello" }, new() { MsgId = "b", MsgText = "Boo" }] });
        Assert.Equal(new[] { true, false }, result.Data.Select(r => r.IsPermitted));
        Assert.Equal("b", result.Data[1].MsgId);
    }

    [Fact]
    public async Task CheckAutoModStatusRequiresOneToHundredCompleteMessages()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"data\":[]}")); }));
        var client = Client(http);
        var hundred = Enumerable.Range(1, 100).Select(i => new AutoModCheckMessage { MsgId = i.ToString(), MsgText = "text" }).ToArray();
        await client.CheckAutoModStatusAsync(new() { BroadcasterId = "1", Data = hundred });
        await Assert.ThrowsAsync<ArgumentException>(() => client.CheckAutoModStatusAsync(new() { BroadcasterId = "1" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CheckAutoModStatusAsync(new() { BroadcasterId = "1", Data = [.. hundred, new() { MsgId = "x", MsgText = "y" }] }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CheckAutoModStatusAsync(new() { BroadcasterId = "1", Data = [new() { MsgId = " ", MsgText = "y" }] }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CheckAutoModStatusAsync(new() { BroadcasterId = "1", Data = [new() { MsgId = "x", MsgText = "" }] }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CheckAutoModStatusAsync(new() { Data = [new() { MsgId = "x", MsgText = "y" }] }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ManageHeldMessageSendsOnlyBodyAndAcceptsNoContent()
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/helix/moderation/automod/message", request.RequestUri!.AbsolutePath);
            Assert.Equal("", request.RequestUri.Query);
            Assert.Equal("{\"user_id\":\"2\",\"msg_id\":\"m1\",\"action\":\"DENY\"}", await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        await Client(http).ManageHeldAutoModMessageAsync(new() { UserId = "2", MsgId = "m1", Action = "DENY" });
    }

    [Theory]
    [InlineData("2", "m1", "allow")]
    [InlineData("2", "m1", "DELETE")]
    [InlineData(" ", "m1", "ALLOW")]
    [InlineData("2", "", "ALLOW")]
    public async Task ManageHeldMessageValidatesActionAndIds(string userId, string messageId, string action)
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("No request expected")));
        await Assert.ThrowsAsync<ArgumentException>(() => Client(http).ManageHeldAutoModMessageAsync(new() { UserId = userId, MsgId = messageId, Action = action }));
    }

    [Fact]
    public async Task AutoModSettingsReadUsesModeratorQueryAndExposesNullOverallLevel()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/moderation/automod/settings", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1&moderator_id=2", request.RequestUri.Query);
            Assert.Null(request.Content);
            return Task.FromResult(TestHttpHandler.Json(Fixture("get-automod-settings")));
        }));
        var settings = (await Client(http).GetAutoModSettingsAsync("1", "2")).Data.Single();
        Assert.Null(settings.OverallLevel);
        Assert.Equal(4, settings.Bullying);
        Assert.Equal(2, settings.SexBasedTerms);
    }

    [Fact]
    public async Task UpdateAutoModSettingsSendsOverallLevelOnlyAndKeepsZero()
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("/helix/moderation/automod/settings", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1&moderator_id=2", request.RequestUri.Query);
            Assert.Equal("{\"overall_level\":0}", await request.Content!.ReadAsStringAsync(ct));
            return TestHttpHandler.Json(Fixture("update-automod-settings"));
        }));
        var settings = (await Client(http).UpdateAutoModSettingsAsync(new() { BroadcasterId = "1", ModeratorId = "2", OverallLevel = 0 })).Data.Single();
        Assert.Equal(3, settings.OverallLevel);
        Assert.Equal(2, settings.Bullying);
    }

    [Fact]
    public async Task UpdateAutoModSettingsSendsEveryIndividualLevelAndOmitsUnsetFields()
    {
        var bodies = new List<string>();
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return TestHttpHandler.Json(Fixture("get-automod-settings"));
        }));
        var client = Client(http);
        await client.UpdateAutoModSettingsAsync(new()
        {
            BroadcasterId = "1", ModeratorId = "2", Disability = 0, Aggression = 1, SexualitySexOrGender = 2, Misogyny = 3,
            Bullying = 4, Swearing = 0, RaceEthnicityOrReligion = 1, SexBasedTerms = 2
        });
        await client.UpdateAutoModSettingsAsync(new() { BroadcasterId = "1", ModeratorId = "2", Swearing = 3 });
        Assert.Equal(new[]
        {
            "{\"disability\":0,\"aggression\":1,\"sexuality_sex_or_gender\":2,\"misogyny\":3,\"bullying\":4,\"swearing\":0,\"race_ethnicity_or_religion\":1,\"sex_based_terms\":2}",
            "{\"swearing\":3}"
        }, bodies);
    }

    [Fact]
    public async Task UpdateAutoModSettingsRejectsMixedMissingAndOutOfRangeLevels()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("No request expected")));
        var client = Client(http);
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateAutoModSettingsAsync(new() { BroadcasterId = "1", ModeratorId = "2", OverallLevel = 2, Swearing = 1 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateAutoModSettingsAsync(new() { BroadcasterId = "1", ModeratorId = "2" }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.UpdateAutoModSettingsAsync(new() { BroadcasterId = "1", ModeratorId = "2", OverallLevel = 5 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.UpdateAutoModSettingsAsync(new() { BroadcasterId = "1", ModeratorId = "2", Aggression = -1 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateAutoModSettingsAsync(new() { BroadcasterId = "1", OverallLevel = 1 }));
    }

    [Fact]
    public async Task BannedUsersQueryCarriesRepeatedIdsPageSizeAndCursors()
    {
        var requests = new List<HttpRequestMessage>();
        using var http = Recording(requests);
        var client = Client(http);
        await client.GetBannedUsersAsync(new() { BroadcasterId = "1", UserIds = ["3", "4"], First = 100, After = "a+b" });
        await client.GetBannedUsersAsync(new() { BroadcasterId = "1", Before = "prev" });
        Assert.All(requests, r => Assert.Equal(HttpMethod.Get, r.Method));
        Assert.All(requests, r => Assert.Equal("/helix/moderation/banned", r.RequestUri!.AbsolutePath));
        Assert.Equal("?broadcaster_id=1&user_id=3&user_id=4&first=100&after=a%2Bb", requests[0].RequestUri!.Query);
        Assert.Equal("?broadcaster_id=1&before=prev", requests[1].RequestUri!.Query);
    }

    [Fact]
    public async Task BannedUsersExposeTimeoutsReasonsAndPagination()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json(Fixture("get-banned-users")))));
        var page = await Client(http).GetBannedUsersAsync(new() { BroadcasterId = "1" });
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 12, 5, 0, TimeSpan.Zero).AddTicks(1234567), page.Data[0].ExpiresAt);
        Assert.Equal("Spam", page.Data[0].Reason);
        Assert.Equal("", page.Data[1].Reason);
        Assert.Equal("next", page.Pagination!.Cursor);
    }

    [Fact]
    public async Task PermanentBanEmptyExpiresAtAndNullEndTimeMapToNull()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) => Task.FromResult(TestHttpHandler.Json(request.Method == HttpMethod.Get
            ? "{\"data\":[{\"user_id\":\"3\",\"user_login\":\"a\",\"user_name\":\"A\",\"expires_at\":\"\",\"created_at\":\"2026-10-09T12:00:00Z\",\"reason\":\"\",\"moderator_id\":\"1\",\"moderator_login\":\"b\",\"moderator_name\":\"B\"}],\"pagination\":{}}"
            : "{\"data\":[{\"broadcaster_id\":\"1\",\"moderator_id\":\"2\",\"user_id\":\"3\",\"created_at\":\"2021-09-28T18:22:31Z\",\"end_time\":null}]}"))));
        var client = Client(http);
        var banned = (await client.GetBannedUsersAsync(new() { BroadcasterId = "1", UserIds = ["3"] })).Data.Single();
        Assert.Null(banned.ExpiresAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero), banned.CreatedAt);
        var ban = (await client.BanUserAsync(new() { BroadcasterId = "1", ModeratorId = "2", Data = new() { UserId = "3" } })).Data.Single();
        Assert.Null(ban.EndTime);
    }

    [Fact]
    public async Task BannedUsersValidateIdCountPageSizeAndCursorExclusivity()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"data\":[]}")); }));
        var client = Client(http);
        var ids = Enumerable.Range(1, 100).Select(i => i.ToString()).ToArray();
        await client.GetBannedUsersAsync(new() { BroadcasterId = "1", UserIds = ids, First = 1 });
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetBannedUsersAsync(new() { BroadcasterId = "1", UserIds = [.. ids, "101"] }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetBannedUsersAsync(new() { BroadcasterId = "1", UserIds = [" "] }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetBannedUsersAsync(new() { BroadcasterId = "1", First = 0 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetBannedUsersAsync(new() { BroadcasterId = "1", First = 101 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetBannedUsersAsync(new() { BroadcasterId = "1", After = "a", Before = "b" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetBannedUsersAsync(new() { BroadcasterId = "" }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task BannedUsersEnumerationSnapshotsFiltersAndRejectsBefore()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal("?broadcaster_id=1&user_id=3&first=5&after=" + (++step == 1 ? "initial" : "next"), request.RequestUri!.Query);
            return Task.FromResult(TestHttpHandler.Json(step == 1 ? Fixture("get-banned-users") : "{\"data\":[],\"pagination\":{}}"));
        }));
        var client = Client(http);
        var ids = new List<string> { "3" };
        var users = client.EnumerateBannedUsersAsync(new() { BroadcasterId = "1", UserIds = ids, First = 5, After = "initial" });
        ids.Clear();
        var count = 0;
        await foreach (var _ in users) count++;
        Assert.Equal(2, count);
        Assert.Equal(2, step);
        Assert.Throws<ArgumentException>(() => client.EnumerateBannedUsersAsync(new() { BroadcasterId = "1", Before = "prev" }));
    }

    [Fact]
    public async Task BanWrapsBodyInDataAndOmitsDurationForPermanentBan()
    {
        var bodies = new List<string>();
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/helix/moderation/bans", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1&moderator_id=2", request.RequestUri.Query);
            bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return TestHttpHandler.Json(Fixture("ban-user"));
        }));
        var client = Client(http);
        await client.BanUserAsync(new() { BroadcasterId = "1", ModeratorId = "2", Data = new() { UserId = "3" } });
        var timeout = (await client.BanUserAsync(new() { BroadcasterId = "1", ModeratorId = "2", Data = new() { UserId = "3", Duration = 1_209_600, Reason = "Spam" } })).Data.Single();
        await client.BanUserAsync(new() { BroadcasterId = "1", ModeratorId = "2", Data = new() { UserId = "3", Duration = 1, Reason = "" } });
        Assert.Equal(new[]
        {
            "{\"data\":{\"user_id\":\"3\"}}",
            "{\"data\":{\"user_id\":\"3\",\"duration\":1209600,\"reason\":\"Spam\"}}",
            "{\"data\":{\"user_id\":\"3\",\"duration\":1,\"reason\":\"\"}}"
        }, bodies);
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567), timeout.EndTime);
        Assert.Equal("3", timeout.UserId);
    }

    [Fact]
    public async Task BanValidatesDurationReasonAndIds()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json(Fixture("ban-user"))); }));
        var client = Client(http);
        await client.BanUserAsync(new() { BroadcasterId = "1", ModeratorId = "2", Data = new() { UserId = "3", Reason = string.Concat(Enumerable.Repeat("😀", 500)) } });
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.BanUserAsync(new() { BroadcasterId = "1", ModeratorId = "2", Data = new() { UserId = "3", Duration = 0 } }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.BanUserAsync(new() { BroadcasterId = "1", ModeratorId = "2", Data = new() { UserId = "3", Duration = 1_209_601 } }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.BanUserAsync(new() { BroadcasterId = "1", ModeratorId = "2", Data = new() { UserId = "3", Reason = new('x', 501) } }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.BanUserAsync(new() { BroadcasterId = "1", ModeratorId = "2", Data = new() { UserId = "" } }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.BanUserAsync(new() { BroadcasterId = "1", Data = new() { UserId = "3" } }));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.BanUserAsync(new() { BroadcasterId = "1", ModeratorId = "2", Data = null! }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task UnbanUsesBodylessDeleteWithAllIdsInQuery()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("/helix/moderation/bans", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1&moderator_id=2&user_id=3", request.RequestUri.Query);
            Assert.Null(request.Content);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }));
        var client = Client(http);
        await client.UnbanUserAsync("1", "2", "3");
        await Assert.ThrowsAsync<ArgumentException>(() => client.UnbanUserAsync("1", "2", " "));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "user is already banned")]
    [InlineData(HttpStatusCode.Forbidden, "not a moderator")]
    [InlineData(HttpStatusCode.Conflict, "ban state is being updated")]
    [InlineData(HttpStatusCode.TooManyRequests, "too many bans")]
    public async Task BanStateErrorsAreSurfacedAsApiExceptions(HttpStatusCode status, string message)
    {
        var calls = 0;
        using var http = ErrorHttp(status, message, () => calls++);
        var client = Client(http);
        await AssertApiError(() => client.BanUserAsync(new() { BroadcasterId = "1", ModeratorId = "2", Data = new() { UserId = "3", Duration = 300 } }), status, message);
        await AssertApiError(() => client.UnbanUserAsync("1", "2", "3"), status, message);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task UnbanRequestsUseDocumentedQueryAndExposeUnresolvedNulls()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/moderation/unban_requests", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1&moderator_id=2&status=pending&user_id=3&after=c&first=50", request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture("get-unban-requests")));
        }));
        var page = await Client(http).GetUnbanRequestsAsync(new() { BroadcasterId = "1", ModeratorId = "2", Status = "pending", UserId = "3", After = "c", First = 50 });
        var pending = page.Data[0];
        Assert.Null(pending.ModeratorId);
        Assert.Null(pending.ModeratorLogin);
        Assert.Null(pending.ModeratorName);
        Assert.Null(pending.ResolvedAt);
        Assert.Null(pending.ResolutionText);
        Assert.Equal("Example", pending.BroadcasterName);
        Assert.Equal("denied", page.Data[1].Status);
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero), page.Data[1].ResolvedAt);
        Assert.Equal("next", page.Pagination!.Cursor);
    }

    [Fact]
    public async Task UnbanRequestsValidateStatusUserIdAndPageSize()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"data\":[]}")); }));
        var client = Client(http);
        foreach (var status in new[] { "pending", "approved", "denied", "acknowledged", "canceled" })
            await client.GetUnbanRequestsAsync(new() { BroadcasterId = "1", ModeratorId = "2", Status = status });
        // Twitch documents no upper page-size bound for this endpoint.
        await client.GetUnbanRequestsAsync(new() { BroadcasterId = "1", ModeratorId = "2", Status = "pending", First = 1000 });
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetUnbanRequestsAsync(new() { BroadcasterId = "1", ModeratorId = "2", Status = "Pending" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetUnbanRequestsAsync(new() { BroadcasterId = "1", ModeratorId = "2", Status = "pending", UserId = " " }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetUnbanRequestsAsync(new() { BroadcasterId = "1", ModeratorId = "2", Status = "pending", First = 0 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetUnbanRequestsAsync(new() { BroadcasterId = "1", ModeratorId = "", Status = "pending" }));
        Assert.Equal(6, calls);
    }

    [Fact]
    public async Task UnbanRequestEnumerationFollowsCursorsFromInitialAfter()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal("?broadcaster_id=1&moderator_id=2&status=pending&after=" + (++step == 1 ? "initial" : "next") + "&first=10", request.RequestUri!.Query);
            return Task.FromResult(TestHttpHandler.Json(step == 1 ? Fixture("get-unban-requests") : "{\"data\":[],\"pagination\":{}}"));
        }));
        var count = 0;
        await foreach (var _ in Client(http).EnumerateUnbanRequestsAsync(new() { BroadcasterId = "1", ModeratorId = "2", Status = "pending", After = "initial", First = 10 })) count++;
        Assert.Equal(2, count);
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task ResolveUnbanRequestSendsQueryOnlyPatch()
    {
        var queries = new List<string>();
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("/helix/moderation/unban_requests", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Content);
            queries.Add(request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture("resolve-unban-requests")));
        }));
        var client = Client(http);
        var resolved = (await client.ResolveUnbanRequestAsync(new() { BroadcasterId = "1", ModeratorId = "2", UnbanRequestId = "u1", Status = "denied", ResolutionText = "No thanks" })).Data.Single();
        await client.ResolveUnbanRequestAsync(new() { BroadcasterId = "1", ModeratorId = "2", UnbanRequestId = "u1", Status = "approved" });
        await client.ResolveUnbanRequestAsync(new() { BroadcasterId = "1", ModeratorId = "2", UnbanRequestId = "u1", Status = "approved", ResolutionText = "" });
        Assert.Equal(new[]
        {
            "?broadcaster_id=1&moderator_id=2&unban_request_id=u1&status=denied&resolution_text=No%20thanks",
            "?broadcaster_id=1&moderator_id=2&unban_request_id=u1&status=approved",
            "?broadcaster_id=1&moderator_id=2&unban_request_id=u1&status=approved&resolution_text="
        }, queries);
        Assert.Equal("approved", resolved.Status);
        Assert.Equal("", resolved.ResolutionText);
        Assert.Equal("2", resolved.ModeratorId);
    }

    [Fact]
    public async Task ResolveUnbanRequestValidatesStatusTextAndPreservesNotFound()
    {
        var calls = 0;
        using var http = ErrorHttp(HttpStatusCode.NotFound, "unban request not found", () => calls++);
        var client = Client(http);
        await Assert.ThrowsAsync<ArgumentException>(() => client.ResolveUnbanRequestAsync(new() { BroadcasterId = "1", ModeratorId = "2", UnbanRequestId = "u1", Status = "pending" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.ResolveUnbanRequestAsync(new() { BroadcasterId = "1", ModeratorId = "2", UnbanRequestId = "u1", Status = "APPROVED" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.ResolveUnbanRequestAsync(new() { BroadcasterId = "1", ModeratorId = "2", UnbanRequestId = "u1", Status = "denied", ResolutionText = new('x', 501) }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.ResolveUnbanRequestAsync(new() { BroadcasterId = "1", ModeratorId = "2", UnbanRequestId = " ", Status = "denied" }));
        Assert.Equal(0, calls);
        await AssertApiError(() => client.ResolveUnbanRequestAsync(new() { BroadcasterId = "1", ModeratorId = "2", UnbanRequestId = "u1", Status = "denied", ResolutionText = new('x', 500) }),
            HttpStatusCode.NotFound, "unban request not found");
    }

    [Fact]
    public async Task BlockedTermsListingAndEnumerationUseModeratorQueryAndCursors()
    {
        var queries = new List<string>();
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/moderation/blocked_terms", request.RequestUri!.AbsolutePath);
            queries.Add(request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(queries.Count == 2 ? Fixture("get-blocked-terms") : "{\"data\":[],\"pagination\":{}}"));
        }));
        var client = Client(http);
        await client.GetBlockedTermsAsync(new() { BroadcasterId = "1", ModeratorId = "2", First = 100, After = "x" });
        var terms = new List<BlockedTerm>();
        await foreach (var term in client.EnumerateBlockedTermsAsync(new() { BroadcasterId = "1", ModeratorId = "2", First = 10 })) terms.Add(term);
        Assert.Equal(new[]
        {
            "?broadcaster_id=1&moderator_id=2&first=100&after=x",
            "?broadcaster_id=1&moderator_id=2&first=10",
            "?broadcaster_id=1&moderator_id=2&first=10&after=next"
        }, queries);
        Assert.Equal(2, terms.Count);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero), terms[0].ExpiresAt);
        Assert.Null(terms[1].ExpiresAt);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetBlockedTermsAsync(new() { BroadcasterId = "1", ModeratorId = "2", First = 101 }));
    }

    [Fact]
    public async Task AddBlockedTermSendsTextBodyWithModeratorQuery()
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/helix/moderation/blocked_terms", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1&moderator_id=2", request.RequestUri.Query);
            Assert.Equal("{\"text\":\"crac*\"}", await request.Content!.ReadAsStringAsync(ct));
            return TestHttpHandler.Json(Fixture("add-blocked-term"));
        }));
        var term = (await Client(http).AddBlockedTermAsync(new() { BroadcasterId = "1", ModeratorId = "2", Text = "crac*" })).Data.Single();
        Assert.Equal("t1", term.Id);
        Assert.Null(term.ExpiresAt);
        Assert.Equal(term.CreatedAt, term.UpdatedAt);
    }

    [Theory]
    [InlineData("ab", true)]
    [InlineData("*foo", true)]
    [InlineData("foo*", true)]
    [InlineData("*foo*", true)]
    [InlineData("foo* bar", true)]
    [InlineData("a", false)]
    [InlineData("  ", false)]
    [InlineData("f*oo", false)]
    [InlineData("foo b**ar", false)]
    public async Task AddBlockedTermValidatesLengthAndWildcardPlacement(string text, bool valid)
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json(Fixture("add-blocked-term"))); }));
        var call = () => Client(http).AddBlockedTermAsync(new() { BroadcasterId = "1", ModeratorId = "2", Text = text });
        if (valid) await call();
        else await Assert.ThrowsAsync<ArgumentException>(call);
        Assert.Equal(valid ? 1 : 0, calls);
    }

    [Fact]
    public async Task AddBlockedTermLengthIsMeasuredInCodePoints()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json(Fixture("add-blocked-term"))); }));
        var client = Client(http);
        await client.AddBlockedTermAsync(new() { BroadcasterId = "1", ModeratorId = "2", Text = string.Concat(Enumerable.Repeat("😀", 500)) });
        await Assert.ThrowsAsync<ArgumentException>(() => client.AddBlockedTermAsync(new() { BroadcasterId = "1", ModeratorId = "2", Text = new('x', 501) }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.AddBlockedTermAsync(new() { BroadcasterId = "1", ModeratorId = "2", Text = "😀" }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RemoveBlockedTermUsesBodylessDeleteById()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("/helix/moderation/blocked_terms", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1&moderator_id=2&id=t1", request.RequestUri.Query);
            Assert.Null(request.Content);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }));
        var client = Client(http);
        await client.RemoveBlockedTermAsync("1", "2", "t1");
        await Assert.ThrowsAsync<ArgumentException>(() => client.RemoveBlockedTermAsync("1", "2", ""));
    }

    [Fact]
    public async Task ChatDeletionSendsMessageIdOnlyForSingleMessage()
    {
        var queries = new List<string>();
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("/helix/moderation/chat", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Content);
            queries.Add(request.RequestUri.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }));
        var client = Client(http);
        await client.DeleteChatMessageAsync("1", "2", "abc-123-def");
        await client.DeleteAllChatMessagesAsync("1", "2");
        await Assert.ThrowsAsync<ArgumentException>(() => client.DeleteChatMessageAsync("1", "2", " "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.DeleteChatMessageAsync("1", "2", null!));
        Assert.Equal(new[] { "?broadcaster_id=1&moderator_id=2&message_id=abc-123-def", "?broadcaster_id=1&moderator_id=2" }, queries);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "You may not delete another moderator's messages.")]
    [InlineData(HttpStatusCode.NotFound, "The specified message was created more than 6 hours ago.")]
    public async Task ChatDeletionAndHeldMessageErrorsArePreserved(HttpStatusCode status, string message)
    {
        using var http = ErrorHttp(status, message, () => { });
        var client = Client(http);
        await AssertApiError(() => client.DeleteChatMessageAsync("1", "2", "m1"), status, message);
        await AssertApiError(() => client.ManageHeldAutoModMessageAsync(new() { UserId = "2", MsgId = "m1", Action = "ALLOW" }), status, message);
    }

    [Fact]
    public async Task CheckAutoModStatusRateLimitIsSurfaced()
    {
        using var http = ErrorHttp(HttpStatusCode.TooManyRequests, "exceeded chat message checks", () => { });
        await AssertApiError(() => Client(http).CheckAutoModStatusAsync(new() { BroadcasterId = "1", Data = [new() { MsgId = "a", MsgText = "b" }] }),
            HttpStatusCode.TooManyRequests, "exceeded chat message checks");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task EveryOperationPreservesStructuredErrors(HttpStatusCode status)
    {
        var calls = 0;
        using var http = ErrorHttp(status, "not one of the broadcaster's moderators", () => calls++);
        var client = Client(http);
        foreach (var (_, call) in Operations()) await AssertApiError(() => call(client), status, "not one of the broadcaster's moderators");
        Assert.Equal(Operations().Length, calls);
    }

    [Fact]
    public async Task UserTokensWithoutDocumentedScopesAreRejectedBeforeHttp()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("No request expected")));
        var client = Client(http, new("user", scopes: [TwitchScopes.UserBot, TwitchScopes.ModeratorReadBannedUsers], kind: TwitchTokenKind.User));
        foreach (var (_, call) in Operations()) await Assert.ThrowsAsync<TwitchAuthorizationException>(() => call(client));
    }

    [Fact]
    public async Task MismatchedUserIsRejectedBeforeHttp()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("No request expected")));
        var client = Client(http, new("user", scopes: AllScopes, kind: TwitchTokenKind.User, userId: "other"));
        foreach (var (_, call) in Operations()) await Assert.ThrowsAsync<TwitchAuthorizationException>(() => call(client));
    }

    [Fact]
    public async Task IdentityChecksUseBroadcasterOrModeratorExactlyAsDocumented()
    {
        string[] broadcasterOnly = ["check-automod-status", "get-banned-users"];
        var requests = new List<HttpRequestMessage>();
        using var http = Recording(requests);
        var moderator = Client(http, new("moderator", scopes: AllScopes, kind: TwitchTokenKind.User, userId: "2"));
        var broadcaster = Client(http, new("broadcaster", scopes: AllScopes, kind: TwitchTokenKind.User, userId: "1"));
        foreach (var (name, call) in Operations())
        {
            if (broadcasterOnly.Contains(name))
            {
                await Assert.ThrowsAsync<TwitchAuthorizationException>(() => call(moderator));
                await call(broadcaster);
            }
            else
            {
                await call(moderator);
                await Assert.ThrowsAsync<TwitchAuthorizationException>(() => call(broadcaster));
            }
        }
        Assert.Equal(Operations().Length, requests.Count);
    }

    [Fact]
    public async Task AppTokensAreAcceptedExceptForUnbanRequests()
    {
        string[] userOnly = ["get-unban-requests", "resolve-unban-requests"];
        var requests = new List<HttpRequestMessage>();
        using var http = Recording(requests);
        var client = Client(http, new("app", scopes: [], kind: TwitchTokenKind.App));
        foreach (var (name, call) in Operations())
        {
            if (userOnly.Contains(name)) await Assert.ThrowsAsync<TwitchAuthorizationException>(() => call(client));
            else await call(client);
        }
        Assert.Equal(Operations().Length - userOnly.Length, requests.Count);
    }

    [Fact]
    public async Task ReadScopesAllowReadsButNotMutations()
    {
        string[] reads = ["check-automod-status", "get-automod-settings", "get-banned-users", "get-unban-requests", "get-blocked-terms"];
        var requests = new List<HttpRequestMessage>();
        using var http = Recording(requests);
        var client = Client(http, new("reader", kind: TwitchTokenKind.User, scopes:
            [TwitchScopes.ModerationRead, TwitchScopes.ModeratorReadAutomodSettings, TwitchScopes.ModeratorReadUnbanRequests, TwitchScopes.ModeratorReadBlockedTerms]));
        foreach (var (name, call) in Operations())
        {
            if (reads.Contains(name)) await call(client);
            else await Assert.ThrowsAsync<TwitchAuthorizationException>(() => call(client));
        }
        Assert.Equal(reads.Length, requests.Count);
    }

    [Theory]
    [InlineData("get-automod-settings", TwitchScopes.ModeratorManageAutomodSettings)]
    [InlineData("get-banned-users", TwitchScopes.ModeratorManageBannedUsers)]
    [InlineData("get-unban-requests", TwitchScopes.ModeratorManageUnbanRequests)]
    [InlineData("get-blocked-terms", TwitchScopes.ModeratorManageBlockedTerms)]
    public async Task ReadsAlsoAcceptTheManageScope(string operation, string scope)
    {
        var requests = new List<HttpRequestMessage>();
        using var http = Recording(requests);
        await Operations().Single(o => o.Name == operation).Call(Client(http, new("user", scopes: [scope], kind: TwitchTokenKind.User)));
        Assert.Single(requests);
    }

    private static (string Name, Func<ModerationClient, Task> Call)[] Operations() =>
    [
        ("check-automod-status", c => c.CheckAutoModStatusAsync(new() { BroadcasterId = "1", Data = [new() { MsgId = "a", MsgText = "Hello" }] })),
        ("manage-held-automod-messages", c => c.ManageHeldAutoModMessageAsync(new() { UserId = "2", MsgId = "m1", Action = "ALLOW" })),
        ("get-automod-settings", c => c.GetAutoModSettingsAsync("1", "2")),
        ("update-automod-settings", c => c.UpdateAutoModSettingsAsync(new() { BroadcasterId = "1", ModeratorId = "2", OverallLevel = 1 })),
        ("get-banned-users", c => c.GetBannedUsersAsync(new() { BroadcasterId = "1" })),
        ("ban-user", c => c.BanUserAsync(new() { BroadcasterId = "1", ModeratorId = "2", Data = new() { UserId = "3" } })),
        ("unban-user", c => c.UnbanUserAsync("1", "2", "3")),
        ("get-unban-requests", c => c.GetUnbanRequestsAsync(new() { BroadcasterId = "1", ModeratorId = "2", Status = "pending" })),
        ("resolve-unban-requests", c => c.ResolveUnbanRequestAsync(new() { BroadcasterId = "1", ModeratorId = "2", UnbanRequestId = "u1", Status = "approved" })),
        ("get-blocked-terms", c => c.GetBlockedTermsAsync(new() { BroadcasterId = "1", ModeratorId = "2" })),
        ("add-blocked-term", c => c.AddBlockedTermAsync(new() { BroadcasterId = "1", ModeratorId = "2", Text = "foo*" })),
        ("remove-blocked-term", c => c.RemoveBlockedTermAsync("1", "2", "t1")),
        ("delete-chat-messages", c => c.DeleteChatMessageAsync("1", "2", "m1")),
        ("delete-chat-messages-all", c => c.DeleteAllChatMessagesAsync("1", "2"))
    ];

    private static HttpClient ErrorHttp(HttpStatusCode status, string message, Action onCall) => new(new TestHttpHandler((_, _) =>
    {
        onCall();
        return Task.FromResult(TestHttpHandler.Json($"{{\"error\":\"{status}\",\"status\":{(int)status},\"message\":\"{message}\"}}", status));
    }));

    private static async Task AssertApiError(Func<Task> call, HttpStatusCode status, string message)
    {
        var error = await Assert.ThrowsAsync<TwitchApiException>(call);
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(message, error.Message);
    }
}
