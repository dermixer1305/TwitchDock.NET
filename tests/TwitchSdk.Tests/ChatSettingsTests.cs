using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TwitchSdk.Core;
using TwitchSdk.Helix;
using TwitchSdk.Helix.Clients;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Tests;

public sealed class ChatSettingsTests
{
    private static readonly AccessToken AppToken = new("app", kind: TwitchTokenKind.App);

    private static ChatClient Client(HttpClient http, AccessToken? token = null) => new HelixClient(new(http,
        new StaticAccessTokenProvider(token ?? new("token")), new() { ClientId = "client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 })).Chat;
    private static string Fixture(string id) => ContractAssertions.Fixture("helix-chat-settings.json", id);
    private static AccessToken User(string userId, params string[] scopes) => new("user", scopes: scopes, kind: TwitchTokenKind.User, userId: userId);
    private static HttpClient NoContent(Action<HttpRequestMessage> inspect) => new(new TestHttpHandler((request, _) =>
    {
        inspect(request);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
    }));
    private static HttpClient Unreachable() => new(new TestHttpHandler((_, _) => throw new InvalidOperationException("No request expected")));

    public static IEnumerable<object[]> Contracts()
    {
        yield return ["get-chat-settings", HelixJsonContext.Default.HelixPageChatSettings];
        yield return ["update-chat-settings", HelixJsonContext.Default.HelixPageChatSettings];
        yield return ["get-pinned-chat-message", HelixJsonContext.Default.HelixPagePinnedChatMessage];
        yield return ["get-user-chat-color", HelixJsonContext.Default.HelixPageUserChatColor];
        yield return ["get-shared-chat-session", HelixJsonContext.Default.HelixPageSharedChatSession];
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void ResponseContractsPreserveEveryDocumentedField(string id, JsonTypeInfo type)
        => ContractAssertions.Verify("helix-chat-settings.json", id, type);

    [Fact]
    public async Task GetChatSettingsWithModeratorReturnsDelayFieldsAndPreservesZeroDuration()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/chat/settings", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1234&moderator_id=5678", request.RequestUri.Query);
            Assert.Null(request.Content);
            return Task.FromResult(TestHttpHandler.Json(Fixture("get-chat-settings")));
        }));
        var settings = (await Client(http, User("5678")).GetChatSettingsAsync("1234", "5678")).Data.Single();
        Assert.Equal("5678", settings.ModeratorId);
        Assert.True(settings.NonModeratorChatDelay);
        Assert.Equal(4, settings.NonModeratorChatDelayDuration);
        Assert.True(settings.FollowerMode);
        Assert.Equal(0, settings.FollowerModeDuration);
        Assert.False(settings.SlowMode);
        Assert.Null(settings.SlowModeWaitTime);
        Assert.True(settings.EmoteMode);
    }

    [Fact]
    public async Task GetChatSettingsWithAppTokenOmitsModeratorAndLeavesModeratorFieldsNull()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal("?broadcaster_id=1234", request.RequestUri!.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture("get-chat-settings-without-moderator")));
        }));
        var settings = (await Client(http, AppToken).GetChatSettingsAsync("1234")).Data.Single();
        Assert.Null(settings.ModeratorId);
        Assert.Null(settings.NonModeratorChatDelay);
        Assert.Null(settings.NonModeratorChatDelayDuration);
        Assert.Null(settings.FollowerModeDuration);
        Assert.Equal(30, settings.SlowModeWaitTime);
        Assert.True(settings.SubscriberMode);
        Assert.True(settings.UniqueChatMode);
    }

    [Fact]
    public async Task GetChatSettingsModeratorMustBeTheTokenUser()
    {
        using var http = Unreachable();
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, AppToken).GetChatSettingsAsync("1234", "5678"));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, User("other")).GetChatSettingsAsync("1234", "5678"));
        await Assert.ThrowsAsync<ArgumentException>(() => Client(http).GetChatSettingsAsync("1234", " "));
        await Assert.ThrowsAsync<ArgumentException>(() => Client(http).GetChatSettingsAsync(""));
    }

    [Fact]
    public async Task UpdateChatSettingsSerializesEveryFieldAndKeepsFalseAndZero()
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("/helix/chat/settings", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1234&moderator_id=5678", request.RequestUri.Query);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var body = json.RootElement;
            Assert.Equal(9, body.EnumerateObject().Count());
            Assert.False(body.GetProperty("emote_mode").GetBoolean());
            Assert.True(body.GetProperty("follower_mode").GetBoolean());
            Assert.Equal(0, body.GetProperty("follower_mode_duration").GetInt32());
            Assert.True(body.GetProperty("non_moderator_chat_delay").GetBoolean());
            Assert.Equal(2, body.GetProperty("non_moderator_chat_delay_duration").GetInt32());
            Assert.True(body.GetProperty("slow_mode").GetBoolean());
            Assert.Equal(3, body.GetProperty("slow_mode_wait_time").GetInt32());
            Assert.False(body.GetProperty("subscriber_mode").GetBoolean());
            Assert.False(body.GetProperty("unique_chat_mode").GetBoolean());
            return TestHttpHandler.Json(Fixture("update-chat-settings"));
        }));
        var result = await Client(http, User("5678", TwitchScopes.ModeratorManageChatSettings)).UpdateChatSettingsAsync("1234", "5678", new()
        {
            EmoteMode = false, FollowerMode = true, FollowerModeDuration = 0, NonModeratorChatDelay = true, NonModeratorChatDelayDuration = 2,
            SlowMode = true, SlowModeWaitTime = 3, SubscriberMode = false, UniqueChatMode = false
        });
        Assert.Equal(10, result.Data.Single().SlowModeWaitTime);
        Assert.False(result.Data.Single().NonModeratorChatDelay);
    }

    [Theory]
    [InlineData("{\"follower_mode\":false}")]
    [InlineData("{\"slow_mode\":true}")]
    [InlineData("{\"slow_mode\":true,\"slow_mode_wait_time\":120}")]
    [InlineData("{\"follower_mode\":true,\"follower_mode_duration\":129600}")]
    [InlineData("{\"non_moderator_chat_delay\":true,\"non_moderator_chat_delay_duration\":6}")]
    [InlineData("{\"non_moderator_chat_delay\":false}")]
    public async Task UpdateChatSettingsSendsOnlyRequestedFields(string expected)
    {
        var request = JsonSerializer.Deserialize(expected, HelixJsonContext.Default.UpdateChatSettingsRequest)!;
        using var http = new HttpClient(new TestHttpHandler(async (message, ct) =>
        {
            Assert.Equal(expected, await message.Content!.ReadAsStringAsync(ct));
            return TestHttpHandler.Json(Fixture("update-chat-settings"));
        }));
        await Client(http).UpdateChatSettingsAsync("1234", "5678", request);
    }

    [Fact]
    public async Task UpdateChatSettingsValidatesPairedFieldsAndDocumentedRanges()
    {
        using var http = Unreachable();
        var client = Client(http);
        Task Update(UpdateChatSettingsRequest request) => client.UpdateChatSettingsAsync("1234", "5678", request);
        await Assert.ThrowsAsync<ArgumentException>(() => Update(new() { FollowerModeDuration = 10 }));
        await Assert.ThrowsAsync<ArgumentException>(() => Update(new() { FollowerMode = false, FollowerModeDuration = 10 }));
        await Assert.ThrowsAsync<ArgumentException>(() => Update(new() { SlowModeWaitTime = 30 }));
        await Assert.ThrowsAsync<ArgumentException>(() => Update(new() { SlowMode = false, SlowModeWaitTime = 30 }));
        await Assert.ThrowsAsync<ArgumentException>(() => Update(new() { NonModeratorChatDelayDuration = 2 }));
        await Assert.ThrowsAsync<ArgumentException>(() => Update(new() { NonModeratorChatDelay = true }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Update(new() { FollowerMode = true, FollowerModeDuration = -1 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Update(new() { FollowerMode = true, FollowerModeDuration = 129601 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Update(new() { SlowMode = true, SlowModeWaitTime = 2 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Update(new() { SlowMode = true, SlowModeWaitTime = 121 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Update(new() { NonModeratorChatDelay = true, NonModeratorChatDelayDuration = 3 }));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.UpdateChatSettingsAsync("1234", "5678", null!));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateChatSettingsAsync("1234", "", new()));
    }

    [Fact]
    public async Task UpdateChatSettingsAcceptsAppTokenAndRequiresModeratorScopeOnUserTokens()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json(Fixture("update-chat-settings"))); }));
        await Client(http, AppToken).UpdateChatSettingsAsync("1234", "5678", new() { EmoteMode = true });
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, User("5678")).UpdateChatSettingsAsync("1234", "5678", new() { EmoteMode = true }));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, User("5678", TwitchScopes.ModeratorReadChatSettings)).UpdateChatSettingsAsync("1234", "5678", new() { EmoteMode = true }));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, User("other", TwitchScopes.ModeratorManageChatSettings)).UpdateChatSettingsAsync("1234", "5678", new() { EmoteMode = true }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AnnouncementPostsMessageAndColorWithIdsInQuery()
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/helix/chat/announcements", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=11111&moderator_id=44444", request.RequestUri.Query);
            Assert.Equal("{\"message\":\"Hello chat!\",\"color\":\"purple\"}", await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        await Client(http, User("44444", TwitchScopes.ModeratorManageAnnouncements))
            .SendChatAnnouncementAsync("11111", "44444", new() { Message = "Hello chat!", Color = "purple" });
    }

    [Fact]
    public async Task AnnouncementForSourceOnlyKeepsFalseAndIsRestrictedToAppTokens()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            calls++;
            Assert.Equal("{\"message\":\"Everyone\",\"for_source_only\":false}", await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        var request = new SendChatAnnouncementRequest { Message = "Everyone", ForSourceOnly = false };
        await Client(http, AppToken).SendChatAnnouncementAsync("11111", "44444", request);
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() =>
            Client(http, User("44444", TwitchScopes.ModeratorManageAnnouncements)).SendChatAnnouncementAsync("11111", "44444", request));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AnnouncementValidatesTextColorAndModeratorIdentity()
    {
        var calls = 0;
        using var http = NoContent(_ => calls++);
        var client = Client(http);
        Task Send(string message, string? color = null) => client.SendChatAnnouncementAsync("11111", "44444", new() { Message = message, Color = color });
        await Assert.ThrowsAsync<ArgumentException>(() => Send(""));
        await Assert.ThrowsAsync<ArgumentException>(() => Send("   "));
        await Assert.ThrowsAsync<ArgumentException>(() => Send(new string('x', 501)));
        await Assert.ThrowsAsync<ArgumentException>(() => Send("Hi", "Purple"));
        await Assert.ThrowsAsync<ArgumentException>(() => Send("Hi", "red"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.SendChatAnnouncementAsync("11111", "44444", null!));
        foreach (var color in new[] { "blue", "green", "orange", "purple", "primary" }) await Send("Hi", color);
        await Send(string.Concat(Enumerable.Repeat("😀", 500)));
        Assert.Equal(6, calls);
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, User("44444")).SendChatAnnouncementAsync("11111", "44444", new() { Message = "Hi" }));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() =>
            Client(http, User("other", TwitchScopes.ModeratorManageAnnouncements)).SendChatAnnouncementAsync("11111", "44444", new() { Message = "Hi" }));
        Assert.Equal(6, calls);
    }

    [Fact]
    public async Task ShoutoutSendsBodylessPostWithAllIdsAndRejectsSelfShoutout()
    {
        var calls = 0;
        using var http = NoContent(request =>
        {
            calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/helix/chat/shoutouts", request.RequestUri!.AbsolutePath);
            Assert.Equal("?from_broadcaster_id=12345&to_broadcaster_id=626262&moderator_id=98765", request.RequestUri.Query);
            Assert.Null(request.Content);
        });
        await Client(http, User("98765", TwitchScopes.ModeratorManageShoutouts)).SendShoutoutAsync("12345", "626262", "98765");
        await Client(http, AppToken).SendShoutoutAsync("12345", "626262", "98765");
        await Assert.ThrowsAsync<ArgumentException>(() => Client(http).SendShoutoutAsync("12345", "12345", "98765"));
        await Assert.ThrowsAsync<ArgumentException>(() => Client(http).SendShoutoutAsync("12345", "626262", ""));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, User("98765")).SendShoutoutAsync("12345", "626262", "98765"));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() =>
            Client(http, User("other", TwitchScopes.ModeratorManageShoutouts)).SendShoutoutAsync("12345", "626262", "98765"));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task PinnedMessageReadsEveryFragmentKindAndExpiry()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/chat/pins", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1234&moderator_id=5678", request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(++step switch
            {
                1 => Fixture("get-pinned-chat-message"),
                2 => Fixture("get-pinned-chat-message-timed"),
                _ => "{\"data\":[]}"
            }));
        }));
        var client = Client(http, User("5678", TwitchScopes.ModeratorReadChatMessages));
        var pinned = (await client.GetPinnedChatMessageAsync("1234", "5678")).Data.Single();
        Assert.Null(pinned.EndsAt);
        Assert.Equal(new DateTimeOffset(2026, 5, 6, 12, 30, 0, TimeSpan.Zero), pinned.StartsAt);
        Assert.Equal("TwitchDev", pinned.PinnedByUserName);
        var fragments = pinned.Message.Fragments;
        Assert.Equal(new[] { "text", "emote", "cheermote", "mention" }, fragments.Select(f => f.Type));
        Assert.Equal(new[] { "static", "animated" }, fragments[1].Emote!.Format);
        Assert.Equal(100, fragments[2].Cheermote!.Bits);
        Assert.Equal("twitchdev", fragments[3].Mention!.UserLogin);
        Assert.All(fragments.Where(f => f.Type == "text"), f => Assert.True(f.Cheermote is null && f.Emote is null && f.Mention is null));
        Assert.Equal(new DateTimeOffset(2026, 5, 6, 12, 35, 0, TimeSpan.Zero), (await client.GetPinnedChatMessageAsync("1234", "5678")).Data.Single().EndsAt);
        Assert.Empty((await client.GetPinnedChatMessageAsync("1234", "5678")).Data);
    }

    [Theory]
    [InlineData("PUT", 300, "?broadcaster_id=1234&moderator_id=5678&message_id=abc-def&duration_seconds=300")]
    [InlineData("PUT", null, "?broadcaster_id=1234&moderator_id=5678&message_id=abc-def")]
    [InlineData("PATCH", 1800, "?broadcaster_id=1234&moderator_id=5678&message_id=abc-def&duration_seconds=1800")]
    [InlineData("PATCH", 30, "?broadcaster_id=1234&moderator_id=5678&message_id=abc-def&duration_seconds=30")]
    [InlineData("PATCH", null, "?broadcaster_id=1234&moderator_id=5678&message_id=abc-def")]
    [InlineData("DELETE", null, "?broadcaster_id=1234&moderator_id=5678&message_id=abc-def")]
    public async Task PinChangesUseQueryParametersOnly(string method, int? duration, string query)
    {
        var calls = 0;
        using var http = NoContent(request =>
        {
            calls++;
            Assert.Equal(method, request.Method.Method);
            Assert.Equal("/helix/chat/pins", request.RequestUri!.AbsolutePath);
            Assert.Equal(query, request.RequestUri.Query);
            Assert.Null(request.Content);
        });
        var client = Client(http, User("5678", TwitchScopes.ModeratorManageChatMessages));
        await (method switch
        {
            "PUT" => client.PinChatMessageAsync("1234", "5678", "abc-def", duration),
            "PATCH" => client.UpdatePinnedChatMessageAsync("1234", "5678", "abc-def", duration),
            _ => client.UnpinChatMessageAsync("1234", "5678", "abc-def")
        });
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task PinDurationAndIdsAreValidatedBeforeSending()
    {
        using var http = Unreachable();
        var client = Client(http);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.PinChatMessageAsync("1234", "5678", "m", 29));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.PinChatMessageAsync("1234", "5678", "m", 1801));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.UpdatePinnedChatMessageAsync("1234", "5678", "m", 0));
        await Assert.ThrowsAsync<ArgumentException>(() => client.PinChatMessageAsync("1234", "5678", " "));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UnpinChatMessageAsync("1234", "", "m"));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetPinnedChatMessageAsync("", "5678"));
    }

    [Fact]
    public async Task PinReadsAcceptEitherScopeWhileChangesRequireManageScope()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            calls++;
            return Task.FromResult(request.Method == HttpMethod.Get ? TestHttpHandler.Json("{\"data\":[]}") : new HttpResponseMessage(HttpStatusCode.NoContent));
        }));
        await Client(http, User("5678", TwitchScopes.ModeratorReadChatMessages)).GetPinnedChatMessageAsync("1234", "5678");
        await Client(http, User("5678", TwitchScopes.ModeratorManageChatMessages)).GetPinnedChatMessageAsync("1234", "5678");
        await Client(http, AppToken).GetPinnedChatMessageAsync("1234", "5678");
        foreach (var call in PinChanges(Client(http, AppToken))) await call();
        Assert.Equal(6, calls);
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, User("5678")).GetPinnedChatMessageAsync("1234", "5678"));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() =>
            Client(http, User("other", TwitchScopes.ModeratorReadChatMessages)).GetPinnedChatMessageAsync("1234", "5678"));
        foreach (var token in new[] { User("5678", TwitchScopes.ModeratorReadChatMessages), User("other", TwitchScopes.ModeratorManageChatMessages) })
            foreach (var call in PinChanges(Client(http, token))) await Assert.ThrowsAsync<TwitchAuthorizationException>(call);
        Assert.Equal(6, calls);
    }

    [Fact]
    public async Task UserChatColorsQueryRepeatsIdsAndPreserveEmptyColor()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/chat/color", request.RequestUri!.AbsolutePath);
            Assert.Equal("?user_id=11111&user_id=44444", request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture("get-user-chat-color")));
        }));
        var colors = (await Client(http, AppToken).GetUserChatColorsAsync(["11111", "44444"])).Data;
        Assert.Equal("#9146FF", colors[0].Color);
        Assert.Equal("", colors[1].Color);
        Assert.Equal("speedyspeedster2", colors[1].UserLogin);
    }

    [Fact]
    public async Task UserChatColorsAcceptOneHundredIds()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"data\":[]}")); }));
        var client = Client(http, User("1"));
        var ids = Enumerable.Range(1, 100).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        await client.GetUserChatColorsAsync(ids);
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetUserChatColorsAsync([.. ids, "101"]));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetUserChatColorsAsync([]));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetUserChatColorsAsync(["1", " "]));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.GetUserChatColorsAsync(null!));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("blue", "?user_id=123&color=blue")]
    [InlineData("yellow_green", "?user_id=123&color=yellow_green")]
    [InlineData("#9146FF", "?user_id=123&color=%239146FF")]
    [InlineData("#a1b2c3", "?user_id=123&color=%23a1b2c3")]
    public async Task UpdateUserChatColorEncodesNamedAndHexColors(string color, string query)
    {
        using var http = NoContent(request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("/helix/chat/color", request.RequestUri!.AbsolutePath);
            Assert.Equal(query, request.RequestUri.Query);
            Assert.Null(request.Content);
        });
        await Client(http, User("123", TwitchScopes.UserManageChatColor)).UpdateUserChatColorAsync("123", color);
    }

    [Fact]
    public async Task UpdateUserChatColorValidatesColorAndRequiresOwnUserToken()
    {
        using var http = Unreachable();
        foreach (var color in new[] { "Blue", "purple", "#9146F", "#9146FFF", "9146FF", "#GGGGGG", "", " " })
            await Assert.ThrowsAsync<ArgumentException>(() => Client(http).UpdateUserChatColorAsync("123", color));
        await Assert.ThrowsAsync<ArgumentException>(() => Client(http).UpdateUserChatColorAsync("", "blue"));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, AppToken).UpdateUserChatColorAsync("123", "blue"));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, User("123")).UpdateUserChatColorAsync("123", "blue"));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, User("other", TwitchScopes.UserManageChatColor)).UpdateUserChatColorAsync("123", "blue"));
    }

    [Fact]
    public async Task SharedChatSessionListsParticipantsAndReturnsEmptyOutsideSessions()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/shared_chat/session", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=198704263", request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(++step == 1 ? Fixture("get-shared-chat-session") : "{\"data\":[]}"));
        }));
        var session = (await Client(http, AppToken).GetSharedChatSessionAsync("198704263")).Data.Single();
        Assert.Equal("198704263", session.HostBroadcasterId);
        Assert.Equal(new[] { "198704263", "487263401" }, session.Participants.Select(p => p.BroadcasterId));
        Assert.Equal(new DateTimeOffset(2024, 9, 29, 19, 46, 12, TimeSpan.Zero), session.UpdatedAt);
        Assert.Empty((await Client(http, User("1")).GetSharedChatSessionAsync("198704263")).Data);
        await Assert.ThrowsAsync<ArgumentException>(() => Client(http).GetSharedChatSessionAsync(" "));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "The broadcaster is not streaming live or does not have one or more viewers.")]
    [InlineData(HttpStatusCode.Forbidden, "The user in moderator_id is not one of the broadcaster's moderators.")]
    [InlineData(HttpStatusCode.NotFound, "The specified message was not found.")]
    [InlineData(HttpStatusCode.Conflict, "The message is already pinned.")]
    [InlineData(HttpStatusCode.TooManyRequests, "The broadcaster exceeded the number of Shoutouts they may send within a given window.")]
    public async Task DocumentedErrorsSurfaceAsTwitchApiExceptionsWithoutRetry(HttpStatusCode status, string message)
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(TestHttpHandler.Json($"{{\"error\":\"Error\",\"status\":{(int)status},\"message\":\"{message}\"}}", status));
        }));
        var operations = Operations(Client(http));
        foreach (var call in operations)
        {
            var error = await Assert.ThrowsAsync<TwitchApiException>(call);
            Assert.Equal(status, error.StatusCode);
            Assert.Equal("Error", error.Error);
            Assert.Equal(message, error.Message);
        }
        Assert.Equal(operations.Length, calls);
    }

    private static Func<Task>[] PinChanges(ChatClient client) =>
    [
        () => client.PinChatMessageAsync("1234", "5678", "m", 60),
        () => client.UpdatePinnedChatMessageAsync("1234", "5678", "m", 60),
        () => client.UnpinChatMessageAsync("1234", "5678", "m")
    ];

    private static Func<Task>[] Operations(ChatClient client) =>
    [
        () => client.GetChatSettingsAsync("1234"),
        () => client.UpdateChatSettingsAsync("1234", "5678", new() { SlowMode = true, SlowModeWaitTime = 10 }),
        () => client.SendChatAnnouncementAsync("1234", "5678", new() { Message = "Hello" }),
        () => client.SendShoutoutAsync("1234", "4321", "5678"),
        () => client.GetPinnedChatMessageAsync("1234", "5678"),
        .. PinChanges(client),
        () => client.GetUserChatColorsAsync(["1234"]),
        () => client.UpdateUserChatColorAsync("1234", "#9146FF"),
        () => client.GetSharedChatSessionAsync("1234")
    ];
}
