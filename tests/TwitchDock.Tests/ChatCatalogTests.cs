using System.Net;
using System.Text.Json.Serialization.Metadata;
using TwitchDock.Core;
using TwitchDock.Helix;
using TwitchDock.Helix.Clients;
using TwitchDock.Helix.Models;

namespace TwitchDock.Tests;

public sealed class ChatCatalogTests
{
    private const string Template = "https://static-cdn.jtvnw.net/emoticons/v2/{{id}}/{{format}}/{{theme_mode}}/{{scale}}";
    private static ChatClient Client(HttpClient http, AccessToken? token = null) => new HelixClient(new(http,
        new StaticAccessTokenProvider(token ?? new("token")), new() { ClientId = "client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 })).Chat;
    private static string Fixture(string id) => ContractAssertions.Fixture("helix-chat-catalog.json", id);
    private static HttpClient Respond(string path, string query, string fixture) => new(new TestHttpHandler((request, _) =>
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(path, request.RequestUri!.AbsolutePath);
        Assert.Equal(query, request.RequestUri.Query);
        Assert.Null(request.Content);
        return Task.FromResult(TestHttpHandler.Json(Fixture(fixture)));
    }));

    public static IEnumerable<object[]> Contracts()
    {
        yield return ["get-chatters", HelixJsonContext.Default.HelixPageChatter];
        yield return ["get-channel-emotes", HelixJsonContext.Default.ChatEmotesResponseChannelEmote];
        yield return ["get-global-emotes", HelixJsonContext.Default.ChatEmotesResponseGlobalEmote];
        yield return ["get-emote-sets", HelixJsonContext.Default.ChatEmotesResponseEmoteSetEmote];
        yield return ["get-user-emotes", HelixJsonContext.Default.UserEmotesResponse];
        yield return ["get-channel-chat-badges", HelixJsonContext.Default.HelixPageChatBadgeSet];
        yield return ["get-global-chat-badges", HelixJsonContext.Default.HelixPageChatBadgeSet];
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void ResponseContractsPreserveEveryDocumentedField(string id, JsonTypeInfo type)
        => ContractAssertions.Verify("helix-chat-catalog.json", id, type);

    [Fact]
    public async Task ChattersSendAllFiltersAndExposeTotalAndCursor()
    {
        using var http = Respond("/helix/chat/chatters", "?broadcaster_id=1&moderator_id=2&first=1000&after=a%2Bb", "get-chatters");
        var page = await Client(http).GetChattersAsync(new() { BroadcasterId = "1", ModeratorId = "2", First = 1000, After = "a+b" });
        Assert.Equal(8, page.Total);
        Assert.Equal("eyJiIjpudWxsLCJhIjp7Ik9mZnNldCI6NX19", page.Pagination!.Cursor);
        var chatter = page.Data[0];
        Assert.Equal(("128393656", "smittysmithers", "SmittySmithers"), (chatter.UserId, chatter.UserLogin, chatter.UserName));
    }

    [Fact]
    public async Task ChattersAcceptModeratorUserTokenOrAppTokenAndRejectOtherUsersOrMissingScope()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json(Fixture("get-chatters"))); }));
        var request = new GetChattersRequest { BroadcasterId = "1", ModeratorId = "2" };
        await Client(http, new("user", scopes: [TwitchScopes.ModeratorReadChatters], kind: TwitchTokenKind.User, userId: "2")).GetChattersAsync(request);
        await Client(http, new("app", kind: TwitchTokenKind.App)).GetChattersAsync(request);
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, new("user", scopes: [], kind: TwitchTokenKind.User, userId: "2")).GetChattersAsync(request));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, new("user", scopes: [TwitchScopes.ModeratorReadChatters], kind: TwitchTokenKind.User, userId: "1")).GetChattersAsync(request));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ChattersValidateRequiredIdsAndThousandItemPageSize()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"data\":[],\"pagination\":{},\"total\":0}")); }));
        var client = Client(http);
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.GetChattersAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetChattersAsync(new() { BroadcasterId = " ", ModeratorId = "2" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetChattersAsync(new() { BroadcasterId = "1", ModeratorId = "" }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetChattersAsync(new() { BroadcasterId = "1", ModeratorId = "2", First = 0 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetChattersAsync(new() { BroadcasterId = "1", ModeratorId = "2", First = 1001 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetChattersAsync(new() { BroadcasterId = "1", ModeratorId = "2", After = "" }));
        Assert.Equal(0, (await client.GetChattersAsync(new() { BroadcasterId = "1", ModeratorId = "2", First = 1 })).Total);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ChatterEnumerationFollowsCursorsFromInitialAfter()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal("?broadcaster_id=1&moderator_id=1&first=2&after=" + (++step == 1 ? "initial" : "eyJiIjpudWxsLCJhIjp7Ik9mZnNldCI6NX19"), request.RequestUri!.Query);
            return Task.FromResult(TestHttpHandler.Json(step == 1 ? Fixture("get-chatters") : "{\"data\":[{\"user_id\":\"3\",\"user_login\":\"third\",\"user_name\":\"Third\"}],\"pagination\":{},\"total\":3}"));
        }));
        var logins = new List<string>();
        await foreach (var chatter in Client(http).EnumerateChattersAsync(new() { BroadcasterId = "1", ModeratorId = "1", First = 2, After = "initial" })) logins.Add(chatter.UserLogin);
        Assert.Equal(["smittysmithers", "second", "third"], logins);
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task ChannelEmotesExposeImagesTierTypeSetFormatsAndTemplate()
    {
        using var http = Respond("/helix/chat/emotes", "?broadcaster_id=141981764", "get-channel-emotes");
        var response = await Client(http).GetChannelEmotesAsync("141981764");
        Assert.Equal(Template, response.Template);
        var emote = response.Data[0];
        Assert.Equal(("304456832", "twitchdevPitchfork", "1000", "subscriptions", "301590448"), (emote.Id, emote.Name, emote.Tier, emote.EmoteType, emote.EmoteSetId));
        Assert.Equal("https://static-cdn.jtvnw.net/emoticons/v2/304456832/static/light/2.0", emote.Images.Url2x);
        Assert.Equal(["static", "animated"], emote.Format);
        Assert.Equal(["1.0", "2.0", "3.0"], emote.Scale);
        Assert.Equal(["light", "dark"], emote.ThemeMode);
        Assert.Equal("", response.Data[1].Tier);
        Assert.Equal("follower", response.Data[1].EmoteType);
    }

    [Fact]
    public async Task GlobalEmotesSendNoQuery()
    {
        using var http = Respond("/helix/chat/emotes/global", "", "get-global-emotes");
        var response = await Client(http).GetGlobalEmotesAsync();
        Assert.Equal(Template, response.Template);
        Assert.Equal("https://static-cdn.jtvnw.net/emoticons/v2/196892/static/light/3.0", response.Data.Single().Images.Url4x);
    }

    [Fact]
    public async Task EmoteSetsRepeatTheSetIdAndExposeOwner()
    {
        using var http = Respond("/helix/chat/emotes/set", "?emote_set_id=301590448&emote_set_id=1234", "get-emote-sets");
        var response = await Client(http).GetEmoteSetsAsync(["301590448", "1234"]);
        var emote = response.Data.Single();
        Assert.Equal(("141981764", "301590448", "subscriptions"), (emote.OwnerId, emote.EmoteSetId, emote.EmoteType));
        Assert.Equal(Template, response.Template);
    }

    [Fact]
    public async Task EmoteSetsRequireOneToTwentyFiveNonBlankIds()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"data\":[],\"template\":\"t\"}")); }));
        var client = Client(http);
        var ids = Enumerable.Range(1, 25).Select(i => i.ToString()).ToArray();
        await client.GetEmoteSetsAsync(ids);
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.GetEmoteSetsAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetEmoteSetsAsync([]));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetEmoteSetsAsync([.. ids, "26"]));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetEmoteSetsAsync(["1", " "]));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task UserEmotesSendFiltersAndKeepTemplateAndEmptyOwnerFields()
    {
        using var http = Respond("/helix/chat/emotes/user", "?user_id=1&after=c1&broadcaster_id=2", "get-user-emotes");
        var response = await Client(http).GetUserEmotesAsync(new() { UserId = "1", BroadcasterId = "2", After = "c1" });
        Assert.Equal(Template, response.Template);
        Assert.Equal("eyJiIjpudWxsLJxhIjoiIn0gf5", response.Pagination!.Cursor);
        Assert.Equal(("hypetrain", "", "477339272"), (response.Data[0].EmoteType, response.Data[0].EmoteSetId, response.Data[0].OwnerId));
        Assert.Equal(("smilies", ""), (response.Data[1].EmoteType, response.Data[1].OwnerId));
    }

    [Fact]
    public async Task UserEmotesRequireOwnUserTokenWithEmoteScope()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json(Fixture("get-user-emotes"))); }));
        var request = new GetUserEmotesRequest { UserId = "1" };
        await Client(http, new("user", scopes: [TwitchScopes.UserReadEmotes], kind: TwitchTokenKind.User, userId: "1")).GetUserEmotesAsync(request);
        foreach (var token in new AccessToken[]
        {
            new("app", kind: TwitchTokenKind.App), new("user", scopes: [], kind: TwitchTokenKind.User, userId: "1"),
            new("other", scopes: [TwitchScopes.UserReadEmotes], kind: TwitchTokenKind.User, userId: "2")
        })
            await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, token).GetUserEmotesAsync(request));
        var client = Client(http);
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetUserEmotesAsync(new() { UserId = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetUserEmotesAsync(new() { UserId = "1", BroadcasterId = "" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetUserEmotesAsync(new() { UserId = "1", After = "" }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task UserEmoteEnumerationKeepsBroadcasterFilterAndFollowsCursor()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(++step == 1 ? "?user_id=1&broadcaster_id=2" : "?user_id=1&after=eyJiIjpudWxsLJxhIjoiIn0gf5&broadcaster_id=2", request.RequestUri!.Query);
            return Task.FromResult(TestHttpHandler.Json(step == 1 ? Fixture("get-user-emotes") : "{\"data\":[],\"template\":\"" + Template + "\",\"pagination\":{}}"));
        }));
        var names = new List<string>();
        await foreach (var emote in Client(http).EnumerateUserEmotesAsync(new() { UserId = "1", BroadcasterId = "2" })) names.Add(emote.Name);
        Assert.Equal(["HypeLol", ":)"], names);
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task ChannelBadgesExposeVersionImagesAndNullableClickTargets()
    {
        using var http = Respond("/helix/chat/badges", "?broadcaster_id=135093069", "get-channel-chat-badges");
        var sets = (await Client(http).GetChannelChatBadgesAsync("135093069")).Data;
        Assert.Equal(["bits", "subscriber"], sets.Select(s => s.SetId));
        var bits = sets[0].Versions.Single();
        Assert.Equal(("1", "cheer 1", "cheer 1", "visit_url", "https://bits.twitch.tv"), (bits.Id, bits.Title, bits.Description, bits.ClickAction, bits.ClickUrl));
        Assert.EndsWith("/1", bits.ImageUrl1x, StringComparison.Ordinal);
        Assert.EndsWith("/2", bits.ImageUrl2x, StringComparison.Ordinal);
        Assert.EndsWith("/3", bits.ImageUrl4x, StringComparison.Ordinal);
        Assert.Equal("subscribe_to_channel", sets[1].Versions[0].ClickAction);
        Assert.Null(sets[1].Versions[0].ClickUrl);
        Assert.Null(sets[1].Versions[1].ClickAction);
    }

    [Fact]
    public async Task GlobalBadgesSendNoQuery()
    {
        using var http = Respond("/helix/chat/badges/global", "", "get-global-chat-badges");
        var set = (await Client(http).GetGlobalChatBadgesAsync()).Data.Single();
        Assert.Equal("vip", set.SetId);
        Assert.Equal("VIP", set.Versions.Single().Title);
    }

    [Fact]
    public async Task CatalogReadsAcceptAppAndUnscopedUserTokens()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            calls++;
            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(TestHttpHandler.Json(Fixture(path switch
            {
                "/helix/chat/emotes" => "get-channel-emotes",
                "/helix/chat/emotes/global" => "get-global-emotes",
                "/helix/chat/emotes/set" => "get-emote-sets",
                "/helix/chat/badges" => "get-channel-chat-badges",
                _ => "get-global-chat-badges"
            })));
        }));
        foreach (var token in new AccessToken[] { new("app", kind: TwitchTokenKind.App), new("user", scopes: [], kind: TwitchTokenKind.User, userId: "9") })
        {
            var client = Client(http, token);
            await client.GetChannelEmotesAsync("1");
            await client.GetGlobalEmotesAsync();
            await client.GetEmoteSetsAsync(["1"]);
            await client.GetChannelChatBadgesAsync("1");
            await client.GetGlobalChatBadgesAsync();
        }
        Assert.Equal(10, calls);
        await Assert.ThrowsAsync<ArgumentException>(() => Client(http).GetChannelEmotesAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => Client(http).GetChannelChatBadgesAsync(" "));
        Assert.Equal(10, calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task StructuredErrorsArePreserved(HttpStatusCode status)
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"error\":\"Error\",\"status\":" + (int)status + ",\"message\":\"rejected\"}", status)); }));
        var client = Client(http);
        Func<Task>[] operations =
        [
            () => client.GetChattersAsync(new() { BroadcasterId = "1", ModeratorId = "2" }),
            () => client.GetChannelEmotesAsync("1"),
            () => client.GetGlobalEmotesAsync(),
            () => client.GetEmoteSetsAsync(["1"]),
            () => client.GetUserEmotesAsync(new() { UserId = "1" }),
            () => client.GetChannelChatBadgesAsync("1"),
            () => client.GetGlobalChatBadgesAsync()
        ];
        foreach (var call in operations)
        {
            var error = await Assert.ThrowsAsync<TwitchApiException>(call);
            Assert.Equal(status, error.StatusCode);
            Assert.Equal("rejected", error.Message);
        }
        Assert.Equal(7, calls);
    }
}
