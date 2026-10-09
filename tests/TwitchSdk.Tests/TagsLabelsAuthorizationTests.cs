using System.Net;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using TwitchSdk.Core;
using TwitchSdk.Helix;
using TwitchSdk.Helix.Clients;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Tests;

// The stream tag endpoints are deliberately exercised although Twitch deprecated them.
#pragma warning disable CS0618
public sealed class TagsLabelsAuthorizationTests
{
    private static HelixClient Client(HttpClient http, AccessToken? token = null) => new(new(http,
        new StaticAccessTokenProvider(token ?? new("token")), new() { ClientId = "client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 }));
    private static string Fixture(string id) => ContractAssertions.Fixture("helix-tags-labels-authorization.json", id);
    private static HttpClient Respond(string path, string query, string fixture) => new(new TestHttpHandler((request, _) =>
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(path, request.RequestUri!.AbsolutePath);
        Assert.Equal(query, request.RequestUri.Query);
        Assert.Null(request.Content);
        return Task.FromResult(TestHttpHandler.Json(Fixture(fixture)));
    }));
    private static HttpClient Count(Action onCall, string json) => new(new TestHttpHandler((_, _) => { onCall(); return Task.FromResult(TestHttpHandler.Json(json)); }));

    public static IEnumerable<object[]> Contracts()
    {
        yield return ["get-all-stream-tags", HelixJsonContext.Default.HelixPageStreamTag];
        yield return ["get-stream-tags", HelixJsonContext.Default.HelixPageStreamTag];
        yield return ["get-content-classification-labels", HelixJsonContext.Default.HelixPageContentClassificationLabel];
        yield return ["get-authorization-by-user", HelixJsonContext.Default.HelixPageUserAuthorization];
        yield return ["get-custom-power-up", HelixJsonContext.Default.HelixPageCustomPowerUp];
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void ResponseContractsPreserveEveryDocumentedField(string id, JsonTypeInfo type)
        => ContractAssertions.Verify("helix-tags-labels-authorization.json", id, type);

    [Theory]
    [InlineData(nameof(TagsClient.GetAllStreamTagsAsync))]
    [InlineData(nameof(TagsClient.EnumerateAllStreamTagsAsync))]
    [InlineData(nameof(TagsClient.GetStreamTagsAsync))]
    public void StreamTagMethodsAreObsoleteWithDocumentedReason(string method)
    {
        var obsolete = typeof(TagsClient).GetMethod(method)!.GetCustomAttribute<ObsoleteAttribute>();
        Assert.NotNull(obsolete);
        Assert.Contains("February 28, 2023", obsolete.Message, StringComparison.Ordinal);
        Assert.Contains("410", obsolete.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllStreamTagsSendRepeatedIdsPageSizeAndCursorAndReadLocalizations()
    {
        using var http = Respond("/helix/tags/streams", "?tag_id=a&tag_id=a&tag_id=b&first=100&after=c%2B1", "get-all-stream-tags");
        var page = await Client(http).Tags.GetAllStreamTagsAsync(new() { TagIds = ["a", "a", "b"], First = 100, After = "c+1" });
        var tag = page.Data.Single();
        Assert.False(tag.IsAuto);
        Assert.Equal("1 Credit Clear", tag.LocalizationNames["en-us"]);
        Assert.Equal("Für Streams mit dem Ziel, ein Coin-op-Arcade-Game mit nur einem Leben abzuschließen.", tag.LocalizationDescriptions["de-de"]);
        Assert.Equal("eyJiI...", page.Pagination!.Cursor);
    }

    [Fact]
    public async Task AllStreamTagsWithoutFiltersSendNoQueryAndValidateLimits()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            calls++;
            Assert.Equal("", request.RequestUri!.Query);
            return Task.FromResult(TestHttpHandler.Json("{\"data\":[],\"pagination\":{}}"));
        }));
        var tags = Client(http, new("app", kind: TwitchTokenKind.App)).Tags;
        Assert.Empty((await tags.GetAllStreamTagsAsync()).Data);
        await Assert.ThrowsAsync<ArgumentException>(() => tags.GetAllStreamTagsAsync(new() { TagIds = Enumerable.Range(0, 101).Select(i => i.ToString()).ToArray() }));
        await Assert.ThrowsAsync<ArgumentException>(() => tags.GetAllStreamTagsAsync(new() { TagIds = [""] }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => tags.GetAllStreamTagsAsync(new() { First = 0 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => tags.GetAllStreamTagsAsync(new() { First = 101 }));
        await Assert.ThrowsAsync<ArgumentException>(() => tags.GetAllStreamTagsAsync(new() { After = " " }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AllStreamTagEnumerationSnapshotsIdsAndFollowsCursor()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal("?tag_id=a&first=1" + (++step == 1 ? "" : "&after=eyJiI..."), request.RequestUri!.Query);
            return Task.FromResult(TestHttpHandler.Json(step == 1 ? Fixture("get-all-stream-tags") : "{\"data\":[],\"pagination\":{}}"));
        }));
        var ids = new List<string> { "a" };
        var items = Client(http).Tags.EnumerateAllStreamTagsAsync(new() { TagIds = ids, First = 1 });
        ids.Add("b");
        var count = 0;
        await foreach (var _ in items) count++;
        Assert.Equal(1, count);
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task StreamTagsUseBroadcasterQueryAndAcceptAppTokens()
    {
        using var http = Respond("/helix/streams/tags", "?broadcaster_id=527115020", "get-stream-tags");
        var tag = (await Client(http, new("app", kind: TwitchTokenKind.App)).Tags.GetStreamTagsAsync("527115020")).Data.Single();
        Assert.True(tag.IsAuto);
        Assert.Equal("English", tag.LocalizationNames["en-us"]);
        await Assert.ThrowsAsync<ArgumentException>(() => Client(http).Tags.GetStreamTagsAsync(" "));
    }

    [Fact]
    public async Task RemovedStreamTagEndpointsSurfaceGoneAsApiException()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"error\":\"Gone\",\"status\":410,\"message\":\"The endpoint has been removed.\"}", HttpStatusCode.Gone)); }));
        var tags = Client(http).Tags;
        Assert.Equal(HttpStatusCode.Gone, (await Assert.ThrowsAsync<TwitchApiException>(() => tags.GetStreamTagsAsync("1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await Assert.ThrowsAsync<TwitchApiException>(() => tags.GetAllStreamTagsAsync())).StatusCode);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ContentClassificationLabelsSendOptionalLocaleAndAcceptAnyToken()
    {
        using (var http = Respond("/helix/content_classification_labels", "", "get-content-classification-labels"))
        {
            var labels = (await Client(http, new("app", kind: TwitchTokenKind.App)).ContentClassification.GetContentClassificationLabelsAsync()).Data;
            Assert.Equal(["DebatedSocialIssuesAndPolitics", "Gambling"], labels.Select(l => l.Id));
            Assert.Equal("Politics and Sensitive Social Issues", labels[0].Name);
            Assert.StartsWith("Discussions", labels[0].Description, StringComparison.Ordinal);
        }
        using (var http = Respond("/helix/content_classification_labels", "?locale=de-DE", "get-content-classification-labels"))
        {
            await Client(http, new("user", scopes: [], kind: TwitchTokenKind.User, userId: "1")).ContentClassification.GetContentClassificationLabelsAsync("de-DE");
            await Assert.ThrowsAsync<ArgumentException>(() => Client(http).ContentClassification.GetContentClassificationLabelsAsync(""));
        }
    }

    [Fact]
    public async Task AuthorizationByUserRepeatsUserIdsAndExposesGrantedScopes()
    {
        using var http = Respond("/helix/authorization/users", "?user_id=141981764&user_id=197886470", "get-authorization-by-user");
        var users = (await Client(http, new("app", kind: TwitchTokenKind.App)).Users.GetAuthorizationByUserAsync(["141981764", "197886470"])).Data;
        Assert.Equal(("141981764", "TwitchDev", "twitchdev", true), (users[0].UserId, users[0].UserName, users[0].UserLogin, users[0].HasAuthorized));
        Assert.Equal(["bits:read", "channel:bot", "channel:manage:predictions"], users[0].Scopes);
        Assert.False(users[1].HasAuthorized);
        Assert.Empty(users[1].Scopes);
    }

    [Fact]
    public async Task AuthorizationByUserRequiresAppTokenAndOneToTenIds()
    {
        var calls = 0;
        using var http = Count(() => calls++, "{\"data\":[]}");
        var users = Client(http, new("app", kind: TwitchTokenKind.App)).Users;
        var ids = Enumerable.Range(1, 10).Select(i => i.ToString()).ToArray();
        await users.GetAuthorizationByUserAsync(ids);
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, new("user", scopes: [], kind: TwitchTokenKind.User, userId: "1")).Users.GetAuthorizationByUserAsync(["1"]));
        await Assert.ThrowsAsync<ArgumentNullException>(() => users.GetAuthorizationByUserAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => users.GetAuthorizationByUserAsync([]));
        await Assert.ThrowsAsync<ArgumentException>(() => users.GetAuthorizationByUserAsync([.. ids, "11"]));
        await Assert.ThrowsAsync<ArgumentException>(() => users.GetAuthorizationByUserAsync([" "]));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CustomPowerUpsSendBroadcasterAndIdsAndExposeSettings()
    {
        using var http = Respond("/helix/bits/custom_power_ups", "?broadcaster_id=274637212&id=92af127c-7326-4483-a52b-b0da0be61c02&id=b2", "get-custom-power-up");
        var token = new AccessToken("user", scopes: [TwitchScopes.BitsRead], kind: TwitchTokenKind.User, userId: "274637212");
        var powerUps = (await Client(http, token).Bits.GetCustomPowerUpsAsync(new() { BroadcasterId = "274637212", Ids = ["92af127c-7326-4483-a52b-b0da0be61c02", "b2"] })).Data;
        var first = powerUps[0];
        Assert.Equal((100L, "Which game?", "#00E5CB"), (first.Bits, first.Prompt, first.BackgroundColor));
        Assert.Equal("https://example.org/power-up-56.png", first.Image!.Url2x);
        Assert.EndsWith("112x112.png", first.DefaultImage.Url4x, StringComparison.Ordinal);
        Assert.Equal(2147483648, first.MaxPerStreamSetting.MaxPerStream);
        Assert.Equal(3, first.MaxPerUserPerStreamSetting.MaxPerUserPerStream);
        Assert.Equal(120, first.GlobalCooldownSetting.GlobalCooldownSeconds);
        Assert.Equal(4, first.RedemptionsRedeemedCurrentStream);
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 12, 2, 0, TimeSpan.Zero), first.CooldownExpiresAt);
        var second = powerUps[1];
        Assert.Null(second.Image);
        Assert.Null(second.RedemptionsRedeemedCurrentStream);
        Assert.Null(second.CooldownExpiresAt);
        Assert.True(second.IsPaused);
        Assert.False(second.IsInStock);
    }

    [Fact]
    public async Task CustomPowerUpsRequireBroadcasterTokenWithBitsReadAndAtMostFiftyIds()
    {
        var calls = 0;
        using var http = Count(() => calls++, "{\"data\":[]}");
        var owner = Client(http, new("user", scopes: [TwitchScopes.BitsRead], kind: TwitchTokenKind.User, userId: "1")).Bits;
        var ids = Enumerable.Range(1, 50).Select(i => i.ToString()).ToArray();
        await owner.GetCustomPowerUpsAsync(new() { BroadcasterId = "1", Ids = ids });
        foreach (var token in new AccessToken[]
        {
            new("app", kind: TwitchTokenKind.App), new("user", scopes: [], kind: TwitchTokenKind.User, userId: "1"),
            new("other", scopes: [TwitchScopes.BitsRead], kind: TwitchTokenKind.User, userId: "2")
        })
            await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, token).Bits.GetCustomPowerUpsAsync(new() { BroadcasterId = "1" }));
        await Assert.ThrowsAsync<ArgumentNullException>(() => owner.GetCustomPowerUpsAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => owner.GetCustomPowerUpsAsync(new() { BroadcasterId = "" }));
        await Assert.ThrowsAsync<ArgumentException>(() => owner.GetCustomPowerUpsAsync(new() { BroadcasterId = "1", Ids = [.. ids, "51"] }));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task PowerUpEligibilityAndMissingIdErrorsArePreserved(HttpStatusCode status)
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json("{\"error\":\"Error\",\"status\":" + (int)status + ",\"message\":\"power-up error\"}", status))));
        var error = await Assert.ThrowsAsync<TwitchApiException>(() => Client(http).Bits.GetCustomPowerUpsAsync(new() { BroadcasterId = "1", Ids = ["x"] }));
        Assert.Equal(status, error.StatusCode);
        Assert.Equal("power-up error", error.Message);
    }

    [Fact]
    public async Task LabelAndAuthorizationErrorsArePreserved()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json("{\"error\":\"Bad Request\",\"status\":400,\"message\":\"malformed\"}", HttpStatusCode.BadRequest))));
        var helix = Client(http);
        Assert.Equal("malformed", (await Assert.ThrowsAsync<TwitchApiException>(() => helix.ContentClassification.GetContentClassificationLabelsAsync("xx-XX"))).Message);
        Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAsync<TwitchApiException>(() => helix.Users.GetAuthorizationByUserAsync(["1"]))).StatusCode);
    }
}
#pragma warning restore CS0618
