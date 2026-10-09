using System.Net;
using System.Text.Json.Serialization.Metadata;
using TwitchDock.Core;
using TwitchDock.Helix;
using TwitchDock.Helix.Clients;
using TwitchDock.Helix.Models;

namespace TwitchDock.Tests;

public sealed class ModerationRolesTests
{
    private const string FixtureFile = "helix-moderation-roles.json";
    private static ModerationClient Client(HttpClient http, AccessToken? token = null) => new HelixClient(new(http,
        new StaticAccessTokenProvider(token ?? new("token")), new() { ClientId = "client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 })).Moderation;
    private static string Fixture(string id) => ContractAssertions.Fixture(FixtureFile, id);
    private static AccessToken User(string userId, params string[] scopes) => new("user", scopes: scopes, kind: TwitchTokenKind.User, userId: userId);
    private static readonly AccessToken App = new("app", kind: TwitchTokenKind.App);

    /// <summary>Captures each request with its body text and answers with the supplied response.</summary>
    private static HttpClient Http(List<(HttpRequestMessage Request, string? Body)> log, Func<int, HttpResponseMessage> respond) => new(new TestHttpHandler(async (request, ct) =>
    {
        log.Add((request, request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
        return respond(log.Count);
    }));
    private static HttpClient NoHttp() => new(new TestHttpHandler((_, _) => throw new InvalidOperationException("No request expected")));
    private static HttpResponseMessage NoContent() => new(HttpStatusCode.NoContent);

    public static IEnumerable<object[]> Contracts()
    {
        yield return ["get-moderated-channels", HelixJsonContext.Default.HelixPageModeratedChannel];
        yield return ["get-moderators", HelixJsonContext.Default.HelixPageChannelModerator];
        yield return ["get-vips", HelixJsonContext.Default.HelixPageChannelVip];
        yield return ["update-shield-mode-status", HelixJsonContext.Default.HelixPageShieldModeStatus];
        yield return ["get-shield-mode-status", HelixJsonContext.Default.HelixPageShieldModeStatus];
        yield return ["warn-chat-user", HelixJsonContext.Default.HelixPageChatUserWarning];
        yield return ["add-suspicious-status-to-chat-user", HelixJsonContext.Default.HelixPageSuspiciousChatUserStatus];
        yield return ["remove-suspicious-status-from-chat-user", HelixJsonContext.Default.HelixPageSuspiciousChatUserStatus];
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void ResponseContractsPreserveEveryDocumentedField(string id, JsonTypeInfo type) => ContractAssertions.Verify(FixtureFile, id, type);

    [Fact]
    public async Task ModeratedChannelsSendUserAndPageAndAcceptUserOrAppTokens()
    {
        var log = new List<(HttpRequestMessage Request, string? Body)>();
        using var http = Http(log, _ => TestHttpHandler.Json(Fixture("get-moderated-channels")));
        var request = new GetModeratedChannelsRequest { UserId = "1", First = 100, After = "a+b" };
        var page = await Client(http, User("1", TwitchScopes.UserReadModeratedChannels)).GetModeratedChannelsAsync(request);
        await Client(http, App).GetModeratedChannelsAsync(request);
        Assert.All(log, entry =>
        {
            Assert.Equal(HttpMethod.Get, entry.Request.Method);
            Assert.Equal("/helix/moderation/channels", entry.Request.RequestUri!.AbsolutePath);
            Assert.Equal("?user_id=1&first=100&after=a%2Bb", entry.Request.RequestUri.Query);
            Assert.Null(entry.Body);
        });
        Assert.Equal(2, log.Count);
        Assert.Equal(["grateful_broadcaster", "bashfulgamer"], page.Data.Select(c => c.BroadcasterLogin));
        Assert.Equal("Grateful_Broadcaster", page.Data[0].BroadcasterName);
        Assert.Equal("next", page.Pagination!.Cursor);
    }

    [Fact]
    public async Task ModeratedChannelsRequireMatchingUserScopeAndValidPage()
    {
        using var http = NoHttp();
        var request = new GetModeratedChannelsRequest { UserId = "1" };
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, User("2", TwitchScopes.UserReadModeratedChannels)).GetModeratedChannelsAsync(request));
        var missing = await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, User("1", TwitchScopes.ModerationRead)).GetModeratedChannelsAsync(request));
        Assert.Equal([TwitchScopes.UserReadModeratedChannels], missing.MissingScopes);
        await Assert.ThrowsAsync<ArgumentException>(() => Client(http).GetModeratedChannelsAsync(new() { UserId = " " }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Client(http).GetModeratedChannelsAsync(request with { First = 0 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Client(http).GetModeratedChannelsAsync(request with { First = 101 }));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Client(http).GetModeratedChannelsAsync(null!));
    }

    [Theory]
    [InlineData(TwitchScopes.ModerationRead, TwitchScopes.ChannelReadVips)]
    [InlineData(TwitchScopes.ChannelManageModerators, TwitchScopes.ChannelManageVips)]
    public async Task ModeratorAndVipListsSendRepeatedUserFiltersAndAcceptEitherScope(string moderatorScope, string vipScope)
    {
        var log = new List<(HttpRequestMessage Request, string? Body)>();
        using var http = Http(log, step => TestHttpHandler.Json(Fixture(step == 1 ? "get-moderators" : "get-vips")));
        var client = Client(http, User("1", moderatorScope, vipScope));
        var moderators = await client.GetModeratorsAsync(new() { BroadcasterId = "1", UserIds = ["2", "3"], First = 1, After = "c" });
        var vips = await client.GetVipsAsync(new() { BroadcasterId = "1", UserIds = ["4"], First = 100 });
        Assert.Equal(HttpMethod.Get, log[0].Request.Method);
        Assert.Equal("/helix/moderation/moderators", log[0].Request.RequestUri!.AbsolutePath);
        Assert.Equal("?broadcaster_id=1&user_id=2&user_id=3&first=1&after=c", log[0].Request.RequestUri!.Query);
        Assert.Equal(HttpMethod.Get, log[1].Request.Method);
        Assert.Equal("/helix/channels/vips", log[1].Request.RequestUri!.AbsolutePath);
        Assert.Equal("?broadcaster_id=1&user_id=4&first=100", log[1].Request.RequestUri!.Query);
        Assert.All(log, entry => Assert.Null(entry.Body));
        Assert.Equal(["424596340", "424596341"], moderators.Data.Select(m => m.UserId));
        Assert.Equal("Second_Mod", moderators.Data[1].UserName);
        Assert.Equal("userloginname", vips.Data.Single().UserLogin);
        Assert.Equal("UserDisplayName", vips.Data.Single().UserName);
        Assert.Equal("next", vips.Pagination!.Cursor);
    }

    [Fact]
    public async Task ModeratorAndVipListsRequireBroadcasterUserTokens()
    {
        using var http = NoHttp();
        foreach (var token in new[] { App, User("1"), User("2", TwitchScopes.ModerationRead, TwitchScopes.ChannelReadVips),
            User("1", TwitchScopes.ChannelManageVips + "x", TwitchScopes.ModeratorReadModerators, TwitchScopes.ModeratorReadVips) })
        {
            var client = Client(http, token);
            await Assert.ThrowsAsync<TwitchAuthorizationException>(() => client.GetModeratorsAsync(new() { BroadcasterId = "1" }));
            await Assert.ThrowsAsync<TwitchAuthorizationException>(() => client.GetVipsAsync(new() { BroadcasterId = "1" }));
        }
        var any = await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, User("1")).GetVipsAsync(new() { BroadcasterId = "1" }));
        Assert.Equal([TwitchScopes.ChannelReadVips, TwitchScopes.ChannelManageVips], any.RequiredAnyOfScopes);
    }

    [Fact]
    public async Task ModeratorAndVipListsValidateHundredIdsPageSizeAndBlankValues()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"data\":[],\"pagination\":{}}")); }));
        var client = Client(http);
        var ids = Enumerable.Range(1, 100).Select(i => i.ToString()).ToArray();
        await client.GetModeratorsAsync(new() { BroadcasterId = "1", UserIds = ids });
        await client.GetVipsAsync(new() { BroadcasterId = "1", UserIds = ids });
        Assert.Equal(2, calls);
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetModeratorsAsync(new() { BroadcasterId = "1", UserIds = [.. ids, "101"] }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetVipsAsync(new() { BroadcasterId = "1", UserIds = [.. ids, "101"] }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetModeratorsAsync(new() { BroadcasterId = "1", UserIds = [""] }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetVipsAsync(new() { BroadcasterId = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetModeratorsAsync(new() { BroadcasterId = "" }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetModeratorsAsync(new() { BroadcasterId = "1", First = 101 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetVipsAsync(new() { BroadcasterId = "1", First = 0 }));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task EnumeratorsSnapshotFiltersAndFollowCursors()
    {
        var log = new List<(HttpRequestMessage Request, string? Body)>();
        using var http = Http(log, step => TestHttpHandler.Json(step switch
        {
            1 => Fixture("get-moderated-channels"),
            3 => Fixture("get-moderators"),
            5 => Fixture("get-vips"),
            _ => "{\"data\":[],\"pagination\":{}}"
        }));
        var client = Client(http);
        var ids = new List<string> { "2" };
        var channels = client.EnumerateModeratedChannelsAsync(new() { UserId = "1", First = 5, After = "initial" });
        var moderators = client.EnumerateModeratorsAsync(new() { BroadcasterId = "1", UserIds = ids, First = 5 });
        var vips = client.EnumerateVipsAsync(new() { BroadcasterId = "1", UserIds = ids, After = "initial" });
        ids.Clear();
        Assert.Equal(2, await Count(channels));
        Assert.Equal(2, await Count(moderators));
        Assert.Equal(1, await Count(vips));
        Assert.Equal(
        [
            "/helix/moderation/channels?user_id=1&first=5&after=initial", "/helix/moderation/channels?user_id=1&first=5&after=next",
            "/helix/moderation/moderators?broadcaster_id=1&user_id=2&first=5", "/helix/moderation/moderators?broadcaster_id=1&user_id=2&first=5&after=next",
            "/helix/channels/vips?broadcaster_id=1&user_id=2&after=initial", "/helix/channels/vips?broadcaster_id=1&user_id=2&after=next"
        ], log.Select(entry => entry.Request.RequestUri!.PathAndQuery));
    }

    public static IEnumerable<object[]> RoleChanges()
    {
        yield return [0, "POST", "/helix/moderation/moderators"];
        yield return [1, "DELETE", "/helix/moderation/moderators"];
        yield return [2, "POST", "/helix/channels/vips"];
        yield return [3, "DELETE", "/helix/channels/vips"];
    }

    [Theory]
    [MemberData(nameof(RoleChanges))]
    public async Task RoleChangesSendQueryOnlyAndAcceptNoContent(int operation, string method, string path)
    {
        var log = new List<(HttpRequestMessage Request, string? Body)>();
        using var http = Http(log, _ => NoContent());
        await RoleChange(Client(http, User("1", TwitchScopes.ChannelManageModerators, TwitchScopes.ChannelManageVips)), operation)();
        var (request, body) = Assert.Single(log);
        Assert.Equal(method, request.Method.Method);
        Assert.Equal(path, request.RequestUri!.AbsolutePath);
        Assert.Equal("?broadcaster_id=1&user_id=2", request.RequestUri.Query);
        Assert.Null(body);
    }

    [Theory]
    [MemberData(nameof(RoleChanges))]
    public async Task RoleChangesValidateIdsAndRejectAppTokensOrMissingScopes(int operation, string method, string path)
    {
        Assert.NotNull(method);
        Assert.NotNull(path);
        using var http = NoHttp();
        var scope = operation < 2 ? TwitchScopes.ChannelManageModerators : TwitchScopes.ChannelManageVips;
        var readScope = operation < 2 ? TwitchScopes.ModerationRead : TwitchScopes.ChannelReadVips;
        await Assert.ThrowsAsync<TwitchAuthorizationException>(RoleChange(Client(http, App), operation));
        var missing = await Assert.ThrowsAsync<TwitchAuthorizationException>(RoleChange(Client(http, User("1", readScope)), operation));
        Assert.Equal([scope], missing.MissingScopes);
        var client = Client(http);
        await Assert.ThrowsAsync<ArgumentException>(() => operation switch
        {
            0 => client.AddChannelModeratorAsync(" ", "2"),
            1 => client.RemoveChannelModeratorAsync("1", ""),
            2 => client.AddChannelVipAsync("", "2"),
            _ => client.RemoveChannelVipAsync("1", " ")
        });
    }

    [Fact]
    public async Task OnlyBroadcasterMayChangeModeratorsOrAddVipsButVipMayRemoveOwnStatus()
    {
        using (var http = NoHttp())
        {
            var other = Client(http, User("2", TwitchScopes.ChannelManageModerators, TwitchScopes.ChannelManageVips));
            for (var operation = 0; operation < 3; operation++) await Assert.ThrowsAsync<TwitchAuthorizationException>(RoleChange(other, operation));
        }
        var log = new List<(HttpRequestMessage Request, string? Body)>();
        using var allowed = Http(log, _ => NoContent());
        await Client(allowed, User("2", TwitchScopes.ChannelManageVips)).RemoveChannelVipAsync("1", "2");
        await Client(allowed, User("1", TwitchScopes.ChannelManageVips)).RemoveChannelVipAsync("1", "2");
        Assert.Equal(2, log.Count);
    }

    [Theory]
    [InlineData(0, 400)]
    [InlineData(0, 422)]
    [InlineData(0, 429)]
    [InlineData(1, 400)]
    [InlineData(1, 429)]
    [InlineData(2, 400)]
    [InlineData(2, 404)]
    [InlineData(2, 409)]
    [InlineData(2, 422)]
    [InlineData(2, 425)]
    [InlineData(2, 429)]
    [InlineData(3, 403)]
    [InlineData(3, 404)]
    [InlineData(3, 422)]
    [InlineData(3, 429)]
    public async Task RoleChangeErrorsArePreservedWithoutRetry(int operation, int status)
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(TestHttpHandler.Json("{\"error\":\"Role error\",\"status\":" + status + ",\"message\":\"role change rejected\"}", (HttpStatusCode)status));
        }));
        var error = await Assert.ThrowsAsync<TwitchApiException>(RoleChange(Client(http), operation));
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.Equal("Role error", error.Error);
        Assert.Equal("role change rejected", error.Message);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(true, "{\"is_active\":true}")]
    [InlineData(false, "{\"is_active\":false}")]
    public async Task ShieldModeUpdateSerializesExplicitFlagAndKeepsIdsInQuery(bool isActive, string expectedBody)
    {
        var log = new List<(HttpRequestMessage Request, string? Body)>();
        using var http = Http(log, _ => TestHttpHandler.Json(Fixture("update-shield-mode-status")));
        var status = (await Client(http, User("2", TwitchScopes.ModeratorManageShieldMode)).UpdateShieldModeStatusAsync("1", "2", isActive)).Data.Single();
        var (request, body) = Assert.Single(log);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/helix/moderation/shield_mode", request.RequestUri!.AbsolutePath);
        Assert.Equal("?broadcaster_id=1&moderator_id=2", request.RequestUri.Query);
        Assert.Equal(expectedBody, body);
        Assert.False(status.IsActive);
        Assert.Equal("simplysimple", status.ModeratorLogin);
        Assert.Equal(DateTimeOffset.Parse("2022-07-26T17:16:03.1234567Z"), status.LastActivatedAt);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("null")]
    public async Task ShieldModeStatusMapsNeverActivatedValuesToEmptyAndNull(string timestamp)
    {
        var log = new List<(HttpRequestMessage Request, string? Body)>();
        using var http = Http(log, _ => TestHttpHandler.Json("{\"data\":[{\"is_active\":false,\"moderator_id\":\"\",\"moderator_login\":\"\",\"moderator_name\":\"\",\"last_activated_at\":" + timestamp + "}]}"));
        var status = (await Client(http, User("2", TwitchScopes.ModeratorReadShieldMode)).GetShieldModeStatusAsync("1", "2")).Data.Single();
        var (request, body) = Assert.Single(log);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/helix/moderation/shield_mode", request.RequestUri!.AbsolutePath);
        Assert.Equal("?broadcaster_id=1&moderator_id=2", request.RequestUri.Query);
        Assert.Null(body);
        Assert.False(status.IsActive);
        Assert.Equal("", status.ModeratorId);
        Assert.Equal("", status.ModeratorLogin);
        Assert.Equal("", status.ModeratorName);
        Assert.Null(status.LastActivatedAt);
    }

    [Fact]
    public async Task ShieldModeAcceptsAppTokensAndChecksModeratorIdentityAndScopes()
    {
        var log = new List<(HttpRequestMessage Request, string? Body)>();
        using var http = Http(log, _ => TestHttpHandler.Json(Fixture("get-shield-mode-status")));
        await Client(http, App).UpdateShieldModeStatusAsync("1", "2", true);
        await Client(http, App).GetShieldModeStatusAsync("1", "2");
        await Client(http, User("2", TwitchScopes.ModeratorManageShieldMode)).GetShieldModeStatusAsync("1", "2");
        Assert.Equal(DateTimeOffset.Parse("2022-07-26T17:16:03.9876543Z"), (await Client(http, User("2", TwitchScopes.ModeratorReadShieldMode)).GetShieldModeStatusAsync("1", "2")).Data.Single().LastActivatedAt);
        Assert.Equal(4, log.Count);

        using var none = NoHttp();
        var reader = Client(none, User("2", TwitchScopes.ModeratorReadShieldMode));
        Assert.Equal([TwitchScopes.ModeratorManageShieldMode], (await Assert.ThrowsAsync<TwitchAuthorizationException>(() => reader.UpdateShieldModeStatusAsync("1", "2", true))).MissingScopes);
        var other = Client(none, User("3", TwitchScopes.ModeratorManageShieldMode));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => other.UpdateShieldModeStatusAsync("1", "2", false));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => other.GetShieldModeStatusAsync("1", "2"));
        var unscoped = await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(none, User("2")).GetShieldModeStatusAsync("1", "2"));
        Assert.Equal([TwitchScopes.ModeratorReadShieldMode, TwitchScopes.ModeratorManageShieldMode], unscoped.RequiredAnyOfScopes);
        await Assert.ThrowsAsync<ArgumentException>(() => Client(none).UpdateShieldModeStatusAsync(" ", "2", true));
        await Assert.ThrowsAsync<ArgumentException>(() => Client(none).GetShieldModeStatusAsync("1", ""));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShieldModeForbiddenModeratorErrorIsPreserved(bool update)
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json("{\"error\":\"Forbidden\",\"status\":403,\"message\":\"not a moderator\"}", HttpStatusCode.Forbidden))));
        var client = Client(http);
        var error = await Assert.ThrowsAsync<TwitchApiException>(() => update ? client.UpdateShieldModeStatusAsync("1", "2", true) : client.GetShieldModeStatusAsync("1", "2"));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Equal("not a moderator", error.Message);
    }

    [Fact]
    public async Task WarningWrapsBodyInDataObjectAndKeepsIdsInQuery()
    {
        var log = new List<(HttpRequestMessage Request, string? Body)>();
        using var http = Http(log, _ => TestHttpHandler.Json(Fixture("warn-chat-user")));
        var warning = (await Client(http, User("404041", TwitchScopes.ModeratorManageWarnings))
            .WarnChatUserAsync("404040", "404041", new() { UserId = "9876", Reason = "stop doing that!" })).Data.Single();
        var (request, body) = Assert.Single(log);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/helix/moderation/warnings", request.RequestUri!.AbsolutePath);
        Assert.Equal("?broadcaster_id=404040&moderator_id=404041", request.RequestUri.Query);
        Assert.Equal("{\"data\":{\"user_id\":\"9876\",\"reason\":\"stop doing that!\"}}", body);
        Assert.Equal("404040", warning.BroadcasterId);
        Assert.Equal("9876", warning.UserId);
        Assert.Equal("404041", warning.ModeratorId);
        Assert.Equal("stop doing that!", warning.Reason);
    }

    [Fact]
    public async Task WarningValidatesReasonLengthIdsAndAuthorization()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json(Fixture("warn-chat-user"))); }));
        var client = Client(http);
        await client.WarnChatUserAsync("1", "2", new() { UserId = "3", Reason = string.Concat(Enumerable.Repeat("😀", 500)) });
        await Client(http, App).WarnChatUserAsync("1", "2", new() { UserId = "3", Reason = "x" });
        Assert.Equal(2, calls);
        await Assert.ThrowsAsync<ArgumentException>(() => client.WarnChatUserAsync("1", "2", new() { UserId = "3", Reason = new string('x', 501) }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.WarnChatUserAsync("1", "2", new() { UserId = "3", Reason = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.WarnChatUserAsync("1", "2", new() { UserId = "", Reason = "x" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.WarnChatUserAsync(" ", "2", new() { UserId = "3", Reason = "x" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.WarnChatUserAsync("1", "", new() { UserId = "3", Reason = "x" }));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.WarnChatUserAsync("1", "2", null!));
        var request = new WarnChatUserRequest { UserId = "3", Reason = "x" };
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, User("9", TwitchScopes.ModeratorManageWarnings)).WarnChatUserAsync("1", "2", request));
        var missing = await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, User("2", TwitchScopes.ModeratorReadWarnings)).WarnChatUserAsync("1", "2", request));
        Assert.Equal([TwitchScopes.ModeratorManageWarnings], missing.MissingScopes);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(409)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task WarningErrorsAreNotRetried(int status)
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(TestHttpHandler.Json("{\"error\":\"Warning error\",\"status\":" + status + ",\"message\":\"warning rejected\"}", (HttpStatusCode)status));
        }));
        var error = await Assert.ThrowsAsync<TwitchApiException>(() => Client(http).WarnChatUserAsync("1", "2", new() { UserId = "3", Reason = "x" }));
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.Equal("warning rejected", error.Message);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("RESTRICTED")]
    [InlineData("ACTIVE_MONITORING")]
    public async Task SuspiciousStatusAddSendsBodyAndIdsInQuery(string status)
    {
        var log = new List<(HttpRequestMessage Request, string? Body)>();
        using var http = Http(log, _ => TestHttpHandler.Json(Fixture("add-suspicious-status-to-chat-user")));
        var result = (await Client(http, User("12826", TwitchScopes.ModeratorManageSuspiciousUsers))
            .AddSuspiciousStatusToChatUserAsync("141981764", "12826", new() { UserId = "9876", Status = status })).Data.Single();
        var (request, body) = Assert.Single(log);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/helix/moderation/suspicious_users", request.RequestUri!.AbsolutePath);
        Assert.Equal("?broadcaster_id=141981764&moderator_id=12826", request.RequestUri.Query);
        Assert.Equal("{\"user_id\":\"9876\",\"status\":\"" + status + "\"}", body);
        Assert.Equal("RESTRICTED", result.Status);
        Assert.Equal(["MANUALLY_ADDED", "DETECTED_BAN_EVADER", "DETECTED_SUS_CHATTER", "BANNED_IN_SHARED_CHANNEL"], result.Types);
        Assert.Equal(DateTimeOffset.Parse("2025-12-01T23:08:18.1234567+00:00"), result.UpdatedAt);
    }

    [Fact]
    public async Task SuspiciousStatusRemovalUsesQueryOnlyAndReturnsNoTreatment()
    {
        var log = new List<(HttpRequestMessage Request, string? Body)>();
        using var http = Http(log, _ => TestHttpHandler.Json(Fixture("remove-suspicious-status-from-chat-user")));
        var result = (await Client(http, App).RemoveSuspiciousStatusFromChatUserAsync("141981764", "12826", "9876")).Data.Single();
        var (request, body) = Assert.Single(log);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("/helix/moderation/suspicious_users", request.RequestUri!.AbsolutePath);
        Assert.Equal("?broadcaster_id=141981764&moderator_id=12826&user_id=9876", request.RequestUri.Query);
        Assert.Null(body);
        Assert.Equal("NO_TREATMENT", result.Status);
        Assert.Equal("9876", result.UserId);
        Assert.Equal("141981764", result.BroadcasterId);
        Assert.Equal("12826", result.ModeratorId);
        Assert.Equal(["MANUALLY_ADDED"], result.Types);
        Assert.Equal(new DateTimeOffset(2025, 12, 1, 23, 8, 18, TimeSpan.Zero), result.UpdatedAt);
    }

    [Fact]
    public async Task SuspiciousStatusValidatesStatusAndChecksOnlyTheDocumentedScope()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            calls++;
            return Task.FromResult(TestHttpHandler.Json(Fixture(request.Method == HttpMethod.Post ? "add-suspicious-status-to-chat-user" : "remove-suspicious-status-from-chat-user")));
        }));
        // The reference allows app tokens and does not require moderator_id to match the user token, so Twitch decides identity.
        foreach (var token in new[] { App, User("2", TwitchScopes.ModeratorManageSuspiciousUsers), User("9", TwitchScopes.ModeratorManageSuspiciousUsers) })
        {
            await Client(http, token).AddSuspiciousStatusToChatUserAsync("1", "2", new() { UserId = "3", Status = "RESTRICTED" });
            await Client(http, token).RemoveSuspiciousStatusFromChatUserAsync("1", "2", "3");
        }
        Assert.Equal(6, calls);
        var reader = Client(http, User("2", TwitchScopes.ModeratorReadSuspiciousUsers));
        var missing = await Assert.ThrowsAsync<TwitchAuthorizationException>(() => reader.AddSuspiciousStatusToChatUserAsync("1", "2", new() { UserId = "3", Status = "RESTRICTED" }));
        Assert.Equal([TwitchScopes.ModeratorManageSuspiciousUsers], missing.MissingScopes);
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => reader.RemoveSuspiciousStatusFromChatUserAsync("1", "2", "3"));
        var client = Client(http);
        foreach (var status in new[] { "restricted", "NO_TREATMENT", "", "MANUALLY_ADDED" })
            await Assert.ThrowsAsync<ArgumentException>(() => client.AddSuspiciousStatusToChatUserAsync("1", "2", new() { UserId = "3", Status = status }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.AddSuspiciousStatusToChatUserAsync("1", "2", new() { UserId = " ", Status = "RESTRICTED" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.AddSuspiciousStatusToChatUserAsync("1", "", new() { UserId = "3", Status = "RESTRICTED" }));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.AddSuspiciousStatusToChatUserAsync("1", "2", null!));
        await Assert.ThrowsAsync<ArgumentException>(() => client.RemoveSuspiciousStatusFromChatUserAsync("1", "2", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => client.RemoveSuspiciousStatusFromChatUserAsync(" ", "2", "3"));
        Assert.Equal(6, calls);
    }

    [Theory]
    [InlineData(true, 400)]
    [InlineData(true, 403)]
    [InlineData(false, 400)]
    [InlineData(false, 403)]
    public async Task SuspiciousStatusErrorsArePreserved(bool add, int status)
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) =>
            Task.FromResult(TestHttpHandler.Json("{\"error\":\"Suspicious error\",\"status\":" + status + ",\"message\":\"status update not allowed\"}", (HttpStatusCode)status))));
        var client = Client(http);
        var error = await Assert.ThrowsAsync<TwitchApiException>(() => add
            ? client.AddSuspiciousStatusToChatUserAsync("1", "2", new() { UserId = "3", Status = "ACTIVE_MONITORING" })
            : client.RemoveSuspiciousStatusFromChatUserAsync("1", "2", "3"));
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.Equal("status update not allowed", error.Message);
    }

    private static Func<Task> RoleChange(ModerationClient client, int operation) => operation switch
    {
        0 => () => client.AddChannelModeratorAsync("1", "2"),
        1 => () => client.RemoveChannelModeratorAsync("1", "2"),
        2 => () => client.AddChannelVipAsync("1", "2"),
        _ => () => client.RemoveChannelVipAsync("1", "2")
    };

    private static async Task<int> Count<T>(IAsyncEnumerable<T> items)
    {
        var count = 0;
        await foreach (var _ in items) count++;
        return count;
    }
}
