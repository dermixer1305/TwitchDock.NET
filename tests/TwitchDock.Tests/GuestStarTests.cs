using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TwitchDock.Core;
using TwitchDock.Helix;
using TwitchDock.Helix.Clients;
using TwitchDock.Helix.Models;

namespace TwitchDock.Tests;

public sealed class GuestStarTests
{
    private const string FixtureFile = "helix-guest-star.json";
    private static GuestStarClient Client(HttpClient http, AccessToken? token = null) => new HelixClient(new(http,
        new StaticAccessTokenProvider(token ?? new("token")), new() { ClientId = "client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 })).GuestStar;
    private static string Fixture(string id) => ContractAssertions.Fixture(FixtureFile, id);
    private static AccessToken User(string userId, params string[] scopes) => new("user", scopes: scopes, kind: TwitchTokenKind.User, userId: userId);
    private static HttpClient NoHttp() => new(new TestHttpHandler((_, _) => throw new InvalidOperationException("No request expected")));

    public static IEnumerable<object[]> Contracts()
    {
        yield return ["get-channel-guest-star-settings", HelixJsonContext.Default.HelixPageGuestStarChannelSettings];
        yield return ["get-guest-star-session", HelixJsonContext.Default.HelixPageGuestStarSession];
        yield return ["create-guest-star-session", HelixJsonContext.Default.HelixPageGuestStarSession];
        yield return ["end-guest-star-session", HelixJsonContext.Default.HelixPageGuestStarSession];
        yield return ["get-guest-star-invites", HelixJsonContext.Default.HelixPageGuestStarInvite];
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void ResponseContractsPreserveEveryDocumentedField(string id, JsonTypeInfo type)
        => ContractAssertions.Verify(FixtureFile, id, type);

    [Fact]
    public async Task ChannelSettingsAreReadWithBroadcasterAndModeratorQuery()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/guest_star/channel_settings", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1&moderator_id=2", request.RequestUri.Query);
            Assert.Null(request.Content);
            return Task.FromResult(TestHttpHandler.Json(Fixture("get-channel-guest-star-settings")));
        }));
        var settings = (await Client(http, User("2", TwitchScopes.ModeratorReadGuestStar)).GetChannelGuestStarSettingsAsync("1", "2")).Data.Single();
        Assert.False(settings.IsModeratorSendLiveEnabled);
        Assert.Equal(6, settings.SlotCount);
        Assert.True(settings.IsBrowserSourceAudioEnabled);
        Assert.Equal("SCREENSHARE_LAYOUT", settings.GroupLayout);
        Assert.Equal("eihq8rew7q3hgierufhi3q", settings.BrowserSourceToken);
    }

    [Fact]
    public async Task ChannelSettingsUpdateUsesJsonBodyAndKeepsFalseValues()
    {
        var bodies = new List<string>();
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("/helix/guest_star/channel_settings", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1", request.RequestUri.Query);
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            bodies.Add(await request.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        var client = Client(http, User("1", TwitchScopes.ChannelManageGuestStar));
        await client.UpdateChannelGuestStarSettingsAsync("1", new()
        {
            IsModeratorSendLiveEnabled = false, SlotCount = 1, IsBrowserSourceAudioEnabled = false, GroupLayout = "VERTICAL_LAYOUT", RegenerateBrowserSources = false
        });
        await client.UpdateChannelGuestStarSettingsAsync("1", new() { RegenerateBrowserSources = true });
        await client.UpdateChannelGuestStarSettingsAsync("1", new());
        using (var json = JsonDocument.Parse(bodies[0]))
        {
            var body = json.RootElement;
            Assert.Equal(5, body.EnumerateObject().Count());
            Assert.False(body.GetProperty("is_moderator_send_live_enabled").GetBoolean());
            Assert.Equal(1, body.GetProperty("slot_count").GetInt32());
            Assert.False(body.GetProperty("is_browser_source_audio_enabled").GetBoolean());
            Assert.Equal("VERTICAL_LAYOUT", body.GetProperty("group_layout").GetString());
            Assert.False(body.GetProperty("regenerate_browser_sources").GetBoolean());
        }
        Assert.Equal("{\"regenerate_browser_sources\":true}", bodies[1]);
        Assert.Equal("{}", bodies[2]);
    }

    [Fact]
    public async Task SessionIsReadWithModeratorAndMapsSlotsMediaAndTimestamps()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/guest_star/session", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1&moderator_id=2", request.RequestUri.Query);
            Assert.Null(request.Content);
            return Task.FromResult(TestHttpHandler.Json(Fixture("get-guest-star-session")));
        }));
        var session = (await Client(http).GetGuestStarSessionAsync("1", "2")).Data.Single();
        Assert.Equal("2KFRQbFtpmfyD3IevNRnCzOPRJI", session.Id);
        Assert.Equal(new string?[] { "0", "1", "SCREENSHARE" }, session.Guests.Select(g => g.SlotId));
        var guest = session.Guests[1];
        Assert.False(guest.IsLive);
        Assert.Equal("144601104", guest.UserId);
        Assert.Equal("Cool_Guest", guest.UserDisplayName);
        Assert.Equal("cool_guest", guest.UserLogin);
        Assert.Equal(0, guest.Volume);
        Assert.Equal(new DateTimeOffset(2023, 1, 2, 4, 20, 59, TimeSpan.Zero).AddTicks(3251234), guest.AssignedAt);
        Assert.False(guest.AudioSettings.IsHostEnabled);
        Assert.True(guest.AudioSettings.IsGuestEnabled);
        Assert.False(guest.AudioSettings.IsAvailable);
        Assert.False(guest.VideoSettings.IsGuestEnabled);
        Assert.False(session.Guests[0].VideoSettings.IsGuestEnabled);
        Assert.True(session.Guests[0].VideoSettings.IsHostEnabled);
    }

    [Fact]
    public async Task SessionsAreCreatedAndEndedWithQueryOnlyRequests()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal("/helix/guest_star/session", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Content);
            if (++step == 1)
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("?broadcaster_id=1", request.RequestUri.Query);
                return Task.FromResult(TestHttpHandler.Json(Fixture("create-guest-star-session")));
            }
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("?broadcaster_id=1&session_id=2KFRQbFtpmfyD3IevNRnCzOPRJI", request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture("end-guest-star-session")));
        }));
        var client = Client(http, User("1", TwitchScopes.ChannelManageGuestStar));
        var created = (await client.CreateGuestStarSessionAsync("1")).Data.Single();
        var host = Assert.Single(created.Guests);
        Assert.Equal("0", host.SlotId);
        Assert.False(host.VideoSettings.IsAvailable);
        var ended = (await client.EndGuestStarSessionAsync("1", created.Id)).Data.Single();
        Assert.Equal(80, ended.Guests.Single().Volume);
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task InvitesAreListedWithSessionAndPreserveStatusAndMediaFlags()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/guest_star/invites", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1&moderator_id=2&session_id=s%2B1", request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture("get-guest-star-invites")));
        }));
        var invites = (await Client(http).GetGuestStarInvitesAsync("1", "2", "s+1")).Data;
        Assert.Equal(new[] { "INVITED", "READY" }, invites.Select(i => i.Status));
        Assert.Equal(new DateTimeOffset(2023, 1, 2, 4, 16, 53, 325, TimeSpan.Zero), invites[0].InvitedAt);
        Assert.True(invites[0].IsVideoEnabled);
        Assert.False(invites[0].IsAudioEnabled);
        Assert.True(invites[0].IsVideoAvailable);
        Assert.True(invites[0].IsAudioAvailable);
        Assert.False(invites[1].IsVideoAvailable);
    }

    [Fact]
    public async Task InvitesAreSentAndRevokedWithQueryParametersOnly()
    {
        var methods = new List<HttpMethod>();
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            methods.Add(request.Method);
            Assert.Equal("/helix/guest_star/invites", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1&moderator_id=2&session_id=s1&guest_id=g1", request.RequestUri.Query);
            Assert.Null(request.Content);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }));
        var client = Client(http, User("2", TwitchScopes.ModeratorManageGuestStar));
        var invite = new GuestStarInviteRequest { BroadcasterId = "1", ModeratorId = "2", SessionId = "s1", GuestId = "g1" };
        await client.SendGuestStarInviteAsync(invite);
        await client.DeleteGuestStarInviteAsync(invite);
        Assert.Equal(new[] { HttpMethod.Post, HttpMethod.Delete }, methods);
    }

    [Fact]
    public async Task SlotsAreAssignedMovedAndDeletedWithDocumentedQueries()
    {
        var expected = new Queue<(HttpMethod Method, string Query)>(
        [
            (HttpMethod.Post, "?broadcaster_id=1&moderator_id=2&session_id=s1&guest_id=g1&slot_id=1"),
            (HttpMethod.Patch, "?broadcaster_id=1&moderator_id=2&session_id=s1&source_slot_id=1&destination_slot_id=2"),
            (HttpMethod.Patch, "?broadcaster_id=1&moderator_id=2&session_id=s1&source_slot_id=2"),
            (HttpMethod.Delete, "?broadcaster_id=1&moderator_id=2&session_id=s1&guest_id=g1&slot_id=1&should_reinvite_guest=false"),
            (HttpMethod.Delete, "?broadcaster_id=1&moderator_id=2&session_id=s1&guest_id=g1&slot_id=1&should_reinvite_guest=true"),
            (HttpMethod.Delete, "?broadcaster_id=1&moderator_id=2&session_id=s1&guest_id=g1&slot_id=1")
        ]);
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            var (method, query) = expected.Dequeue();
            Assert.Equal(method, request.Method);
            Assert.Equal("/helix/guest_star/slot", request.RequestUri!.AbsolutePath);
            Assert.Equal(query, request.RequestUri.Query);
            Assert.Null(request.Content);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }));
        var client = Client(http, User("2", TwitchScopes.ChannelManageGuestStar));
        await client.AssignGuestStarSlotAsync(new() { BroadcasterId = "1", ModeratorId = "2", SessionId = "s1", GuestId = "g1", SlotId = "1" });
        await client.UpdateGuestStarSlotAsync(new() { BroadcasterId = "1", ModeratorId = "2", SessionId = "s1", SourceSlotId = "1", DestinationSlotId = "2" });
        await client.UpdateGuestStarSlotAsync(new() { BroadcasterId = "1", ModeratorId = "2", SessionId = "s1", SourceSlotId = "2" });
        await client.DeleteGuestStarSlotAsync(new() { BroadcasterId = "1", ModeratorId = "2", SessionId = "s1", GuestId = "g1", SlotId = "1", ShouldReinviteGuest = false });
        await client.DeleteGuestStarSlotAsync(new() { BroadcasterId = "1", ModeratorId = "2", SessionId = "s1", GuestId = "g1", SlotId = "1", ShouldReinviteGuest = true });
        await client.DeleteGuestStarSlotAsync(new() { BroadcasterId = "1", ModeratorId = "2", SessionId = "s1", GuestId = "g1", SlotId = "1" });
        Assert.Empty(expected);
    }

    [Fact]
    public async Task SlotSettingsAreQueryParametersWithExplicitFalseAndZero()
    {
        var expected = new Queue<string>(
        [
            "?broadcaster_id=1&moderator_id=2&session_id=s1&slot_id=1&is_audio_enabled=false&is_video_enabled=false&is_live=false&volume=0",
            "?broadcaster_id=1&moderator_id=2&session_id=s1&slot_id=SCREENSHARE&is_audio_enabled=true&is_video_enabled=true&is_live=true&volume=100",
            "?broadcaster_id=1&moderator_id=2&session_id=s1&slot_id=1&is_live=true",
            "?broadcaster_id=1&moderator_id=2&session_id=s1&slot_id=1&volume=0"
        ]);
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("/helix/guest_star/slot_settings", request.RequestUri!.AbsolutePath);
            Assert.Equal(expected.Dequeue(), request.RequestUri.Query);
            Assert.Null(request.Content);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }));
        var client = Client(http, User("2", TwitchScopes.ModeratorManageGuestStar));
        await client.UpdateGuestStarSlotSettingsAsync(new() { BroadcasterId = "1", ModeratorId = "2", SessionId = "s1", SlotId = "1", IsAudioEnabled = false, IsVideoEnabled = false, IsLive = false, Volume = 0 });
        await client.UpdateGuestStarSlotSettingsAsync(new() { BroadcasterId = "1", ModeratorId = "2", SessionId = "s1", SlotId = "SCREENSHARE", IsAudioEnabled = true, IsVideoEnabled = true, IsLive = true, Volume = 100 });
        await client.UpdateGuestStarSlotSettingsAsync(new() { BroadcasterId = "1", ModeratorId = "2", SessionId = "s1", SlotId = "1", IsLive = true });
        await client.UpdateGuestStarSlotSettingsAsync(new() { BroadcasterId = "1", ModeratorId = "2", SessionId = "s1", SlotId = "1", Volume = 0 });
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateGuestStarSlotSettingsAsync(new() { BroadcasterId = "1", ModeratorId = "2", SessionId = "s1", SlotId = "1" }));
        Assert.Empty(expected);
    }

    [Fact]
    public void OfficialExampleShapesWithConflictingFieldNamesStillDeserialize()
    {
        var settings = JsonSerializer.Deserialize(Fixture("official-example-channel-settings"), HelixJsonContext.Default.HelixPageGuestStarChannelSettings)!.Data.Single();
        Assert.Null(settings.GroupLayout);
        Assert.Equal(4, settings.SlotCount);
        var guest = JsonSerializer.Deserialize(Fixture("official-example-create-session"), HelixJsonContext.Default.HelixPageGuestStarSession)!.Data.Single().Guests.Single();
        Assert.Null(guest.SlotId);
        Assert.Equal("cool_user", guest.UserLogin);
    }

    [Fact]
    public async Task AppTokensAreRejectedForEveryOperation()
    {
        using var http = NoHttp();
        var client = Client(http, new("app", kind: TwitchTokenKind.App));
        foreach (var call in BroadcasterOperations(client).Concat(ReadOperations(client, "1")).Concat(ManageOperations(client, "1")))
            await Assert.ThrowsAsync<TwitchAuthorizationException>(call);
    }

    [Fact]
    public async Task BroadcasterOperationsRejectModeratorsAndAlternativeScopes()
    {
        using var http = NoHttp();
        var moderator = Client(http, User("2", TwitchScopes.ChannelManageGuestStar, TwitchScopes.ModeratorManageGuestStar));
        foreach (var call in BroadcasterOperations(moderator))
        {
            var error = await Assert.ThrowsAsync<TwitchAuthorizationException>(call);
            Assert.Empty(error.MissingScopes);
        }
        var broadcaster = Client(http, User("1", TwitchScopes.ModeratorManageGuestStar, TwitchScopes.ChannelReadGuestStar));
        foreach (var call in BroadcasterOperations(broadcaster))
        {
            var error = await Assert.ThrowsAsync<TwitchAuthorizationException>(call);
            Assert.Equal(new[] { TwitchScopes.ChannelManageGuestStar }, error.MissingScopes);
        }
    }

    [Fact]
    public async Task ModeratorOperationsRequireTokenUserToMatchModeratorId()
    {
        using var http = NoHttp();
        var client = Client(http, User("3", TwitchScopes.ChannelManageGuestStar, TwitchScopes.ModeratorManageGuestStar));
        foreach (var call in ReadOperations(client, "2").Concat(ManageOperations(client, "2")))
            await Assert.ThrowsAsync<TwitchAuthorizationException>(call);
    }

    [Theory]
    [InlineData(TwitchScopes.ChannelReadGuestStar)]
    [InlineData(TwitchScopes.ChannelManageGuestStar)]
    [InlineData(TwitchScopes.ModeratorReadGuestStar)]
    [InlineData(TwitchScopes.ModeratorManageGuestStar)]
    public async Task ReadOperationsAcceptAnyDocumentedScopeForBroadcasterOrModerator(string scope)
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"data\":[]}")); }));
        foreach (var userId in new[] { "1", "2" })
            foreach (var call in ReadOperations(Client(http, User(userId, scope)), userId)) await call();
        Assert.Equal(6, calls);
    }

    [Theory]
    [InlineData(TwitchScopes.ChannelManageGuestStar)]
    [InlineData(TwitchScopes.ModeratorManageGuestStar)]
    public async Task ManageOperationsAcceptEitherManageScopeForBroadcasterOrModerator(string scope)
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); }));
        foreach (var userId in new[] { "1", "2" })
            foreach (var call in ManageOperations(Client(http, User(userId, scope)), userId)) await call();
        Assert.Equal(12, calls);
    }

    [Fact]
    public async Task ManageOperationsRejectReadOnlyScopes()
    {
        using var http = NoHttp();
        var client = Client(http, User("2", TwitchScopes.ChannelReadGuestStar, TwitchScopes.ModeratorReadGuestStar));
        foreach (var call in ManageOperations(client, "2"))
        {
            var error = await Assert.ThrowsAsync<TwitchAuthorizationException>(call);
            Assert.Equal(new[] { TwitchScopes.ChannelManageGuestStar, TwitchScopes.ModeratorManageGuestStar }, error.RequiredAnyOfScopes);
        }
        var none = Client(http, User("2"));
        foreach (var call in ReadOperations(none, "2"))
        {
            var error = await Assert.ThrowsAsync<TwitchAuthorizationException>(call);
            Assert.Equal(4, error.RequiredAnyOfScopes.Count);
        }
    }

    [Fact]
    public async Task DocumentedConstraintsAreValidatedBeforeHttp()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); }));
        var client = Client(http);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.UpdateChannelGuestStarSettingsAsync("1", new() { SlotCount = 0 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.UpdateChannelGuestStarSettingsAsync("1", new() { SlotCount = 7 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateChannelGuestStarSettingsAsync("1", new() { GroupLayout = "tiled_layout" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateChannelGuestStarSettingsAsync("1", new() { GroupLayout = "GRID_LAYOUT" }));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.UpdateChannelGuestStarSettingsAsync("1", null!));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.UpdateGuestStarSlotSettingsAsync(SlotSettings(-1)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.UpdateGuestStarSlotSettingsAsync(SlotSettings(101)));
        foreach (var slot in new[] { "0", "00", "01", "-1", "1a", "SCREENSHARE", " ", "" })
            await Assert.ThrowsAsync<ArgumentException>(() => client.AssignGuestStarSlotAsync(new() { BroadcasterId = "1", ModeratorId = "1", SessionId = "s1", GuestId = "g1", SlotId = slot }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateGuestStarSlotAsync(new() { BroadcasterId = "1", ModeratorId = "1", SessionId = "s1", SourceSlotId = "1", DestinationSlotId = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateGuestStarSlotAsync(new() { BroadcasterId = "1", ModeratorId = "1", SessionId = "s1", SourceSlotId = "" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.DeleteGuestStarSlotAsync(new() { BroadcasterId = "1", ModeratorId = "1", SessionId = "s1", GuestId = "g1", SlotId = " " }));
        Assert.Equal("request.GuestId", (await Assert.ThrowsAsync<ArgumentException>(() => client.SendGuestStarInviteAsync(new() { BroadcasterId = "1", ModeratorId = "1", SessionId = "s1", GuestId = "" }))).ParamName);
        await Assert.ThrowsAsync<ArgumentException>(() => client.DeleteGuestStarInviteAsync(new() { BroadcasterId = "1", ModeratorId = " ", SessionId = "s1", GuestId = "g1" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetGuestStarInvitesAsync("1", "1", " "));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetChannelGuestStarSettingsAsync("1", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetGuestStarSessionAsync(" ", "1"));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateGuestStarSessionAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => client.EndGuestStarSessionAsync("1", " "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.AssignGuestStarSlotAsync(null!));
        Assert.Equal(0, calls);

        await client.UpdateChannelGuestStarSettingsAsync("1", new() { SlotCount = 1, GroupLayout = "TILED_LAYOUT" });
        await client.UpdateChannelGuestStarSettingsAsync("1", new() { SlotCount = 6, GroupLayout = "HORIZONTAL_LAYOUT" });
        await client.UpdateChannelGuestStarSettingsAsync("1", new() { GroupLayout = "SCREENSHARE_LAYOUT" });
        await client.UpdateGuestStarSlotSettingsAsync(SlotSettings(0));
        await client.UpdateGuestStarSlotSettingsAsync(SlotSettings(100));
        foreach (var slot in new[] { "1", "6", "10" })
            await client.AssignGuestStarSlotAsync(new() { BroadcasterId = "1", ModeratorId = "1", SessionId = "s1", GuestId = "g1", SlotId = slot });
        Assert.Equal(8, calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task DocumentedErrorsSurfaceAsStructuredApiExceptions(HttpStatusCode status)
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(TestHttpHandler.Json("{\"error\":\"Guest Star\",\"status\":" + (int)status + ",\"message\":\"Guest is not ready to join\"}", status));
        }));
        var client = Client(http);
        foreach (var call in BroadcasterOperations(client).Concat(ReadOperations(client, "1")).Concat(ManageOperations(client, "1")))
        {
            var error = await Assert.ThrowsAsync<TwitchApiException>(call);
            Assert.Equal(status, error.StatusCode);
            Assert.Equal("Guest Star", error.Error);
            Assert.Equal("Guest is not ready to join", error.Message);
        }
        Assert.Equal(12, calls);
    }

    [Fact]
    public async Task UnauthorizedResponsesAreNotRetriedWithAnUnchangedToken()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(TestHttpHandler.Json("{\"error\":\"Unauthorized\",\"status\":401,\"message\":\"Phone verification missing\"}", HttpStatusCode.Unauthorized));
        }));
        var error = await Assert.ThrowsAsync<TwitchApiException>(() => Client(http).CreateGuestStarSessionAsync("1"));
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Equal("Phone verification missing", error.Message);
        Assert.Equal(1, calls);
    }

    private static GuestStarUpdateSlotSettingsRequest SlotSettings(int volume)
        => new() { BroadcasterId = "1", ModeratorId = "1", SessionId = "s1", SlotId = "1", Volume = volume };

    private static Func<Task>[] BroadcasterOperations(GuestStarClient client) =>
    [
        () => client.UpdateChannelGuestStarSettingsAsync("1", new() { SlotCount = 2 }),
        () => client.CreateGuestStarSessionAsync("1"),
        () => client.EndGuestStarSessionAsync("1", "s1")
    ];

    private static Func<Task>[] ReadOperations(GuestStarClient client, string moderatorId) =>
    [
        () => client.GetChannelGuestStarSettingsAsync("1", moderatorId),
        () => client.GetGuestStarSessionAsync("1", moderatorId),
        () => client.GetGuestStarInvitesAsync("1", moderatorId, "s1")
    ];

    private static Func<Task>[] ManageOperations(GuestStarClient client, string moderatorId) =>
    [
        () => client.SendGuestStarInviteAsync(new() { BroadcasterId = "1", ModeratorId = moderatorId, SessionId = "s1", GuestId = "g1" }),
        () => client.DeleteGuestStarInviteAsync(new() { BroadcasterId = "1", ModeratorId = moderatorId, SessionId = "s1", GuestId = "g1" }),
        () => client.AssignGuestStarSlotAsync(new() { BroadcasterId = "1", ModeratorId = moderatorId, SessionId = "s1", GuestId = "g1", SlotId = "1" }),
        () => client.UpdateGuestStarSlotAsync(new() { BroadcasterId = "1", ModeratorId = moderatorId, SessionId = "s1", SourceSlotId = "1", DestinationSlotId = "2" }),
        () => client.DeleteGuestStarSlotAsync(new() { BroadcasterId = "1", ModeratorId = moderatorId, SessionId = "s1", GuestId = "g1", SlotId = "1", ShouldReinviteGuest = true }),
        () => client.UpdateGuestStarSlotSettingsAsync(new() { BroadcasterId = "1", ModeratorId = moderatorId, SessionId = "s1", SlotId = "1", IsAudioEnabled = false })
    ];
}
