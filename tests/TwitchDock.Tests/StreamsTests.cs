using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TwitchDock.Core;
using TwitchDock.Helix;
using TwitchDock.Helix.Clients;
using TwitchDock.Helix.Models;

namespace TwitchDock.Tests;

public sealed class StreamsTests
{
    private static StreamsClient Client(HttpClient http, AccessToken? token = null) => new HelixClient(new(http,
        new StaticAccessTokenProvider(token ?? new("token")), new() { ClientId = "client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 })).Streams;
    private static string Fixture(string id) => ContractAssertions.Fixture("helix-streams.json", id);

    public static IEnumerable<object[]> Contracts()
    {
        yield return ["get-stream-key", HelixJsonContext.Default.HelixPageStreamKeyResult];
        yield return ["get-followed-streams", HelixJsonContext.Default.HelixPageTwitchStream];
        yield return ["create-stream-marker", HelixJsonContext.Default.HelixPageCreatedStreamMarker];
        yield return ["get-stream-markers", HelixJsonContext.Default.HelixPageStreamMarkerGroup];
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void ResponseContractsPreserveEveryDocumentedField(string id, JsonTypeInfo type)
        => ContractAssertions.Verify("helix-streams.json", id, type);

    [Fact]
    public async Task KeyAndFollowedStreamsUseQueryParametersAndSourceGeneratedResponses()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            var key = ++step == 1;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Content);
            Assert.Equal(key ? "/helix/streams/key" : "/helix/streams/followed", request.RequestUri!.AbsolutePath);
            Assert.Equal(key ? "?broadcaster_id=1" : "?user_id=1&first=100&after=a%2Bb", request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture(key ? "get-stream-key" : "get-followed-streams")));
        }));
        var client = Client(http, new("token", scopes: [TwitchScopes.ChannelReadStreamKey, TwitchScopes.UserReadFollows], kind: TwitchTokenKind.User, userId: "1"));
        var key = (await client.GetStreamKeyAsync("1")).Data.Single();
        Assert.Equal("fake-stream-key-for-contract-test", key.StreamKey);
        Assert.DoesNotContain(key.StreamKey, key.ToString());
        Assert.Equal("next", (await client.GetFollowedStreamsAsync(new() { UserId = "1", First = 100, After = "a+b" })).Pagination!.Cursor);
        Assert.Equal(2, step);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Highlight")]
    public async Task MarkerCreationAcceptsEditorsAndPreservesOptionalDescription(string? description)
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/helix/streams/markers", request.RequestUri!.AbsolutePath);
            Assert.Equal("", request.RequestUri.Query);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("broadcaster", body.RootElement.GetProperty("user_id").GetString());
            if (description is null) Assert.False(body.RootElement.TryGetProperty("description", out _));
            else Assert.Equal(description, body.RootElement.GetProperty("description").GetString());
            return TestHttpHandler.Json(Fixture("create-stream-marker"));
        }));
        var marker = (await Client(http, new("token", scopes: [TwitchScopes.ChannelManageBroadcast], kind: TwitchTokenKind.User, userId: "editor"))
            .CreateStreamMarkerAsync(new() { UserId = "broadcaster", Description = description })).Data.Single();
        Assert.Equal(60, marker.PositionSeconds);
        Assert.Equal("m1", marker.Id);
    }

    [Theory]
    [InlineData(TwitchScopes.UserReadBroadcast)]
    [InlineData(TwitchScopes.ChannelManageBroadcast)]
    public async Task MarkersAcceptEitherScopeAndPreserveNestedCreatorVideoGrouping(string scope)
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/streams/markers", request.RequestUri!.AbsolutePath);
            Assert.Equal(++step == 1 ? "?user_id=broadcaster&first=1&before=previous" : "?video_id=v1&first=100&after=next", request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture("get-stream-markers")));
        }));
        var client = Client(http, new("token", scopes: [scope], kind: TwitchTokenKind.User, userId: "editor"));
        var page = await client.GetStreamMarkersAsync(new() { UserId = "broadcaster", First = 1, Before = "previous" });
        var group = Assert.Single(page.Data);
        Assert.Equal("editor", group.UserId);
        var video = Assert.Single(group.Videos);
        Assert.Equal("v1", video.VideoId);
        Assert.Equal("", Assert.Single(video.Markers).Description);
        await client.GetStreamMarkersAsync(new() { VideoId = "v1", First = 100, After = "next" });
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task StreamOperationsRejectMissingScopesAppTokensAndWrongOwnerWhenRequired()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("No request expected")));
        foreach (var token in new AccessToken[] { new("user", scopes: [], kind: TwitchTokenKind.User, userId: "1"), new("app", kind: TwitchTokenKind.App) })
        {
            var client = Client(http, token);
            Func<Task>[] calls = [() => client.GetStreamKeyAsync("1"), () => client.GetFollowedStreamsAsync(new() { UserId = "1" }),
                () => client.CreateStreamMarkerAsync(new() { UserId = "1" }), () => client.GetStreamMarkersAsync(new() { UserId = "1" })];
            foreach (var call in calls) await Assert.ThrowsAsync<TwitchAuthorizationException>(call);
        }
        var wrongOwner = Client(http, new("user", scopes: [TwitchScopes.ChannelReadStreamKey, TwitchScopes.UserReadFollows], kind: TwitchTokenKind.User, userId: "other"));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => wrongOwner.GetStreamKeyAsync("1"));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => wrongOwner.GetFollowedStreamsAsync(new() { UserId = "1" }));
    }

    [Fact]
    public async Task MarkerRequestsValidateExclusiveSelectorsDescriptionLimitAndPageBounds()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json(Fixture("create-stream-marker"))); }));
        var client = Client(http);
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetStreamMarkersAsync(new()));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetStreamMarkersAsync(new() { UserId = "1", VideoId = "v1" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetStreamMarkersAsync(new() { VideoId = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetStreamMarkersAsync(new() { UserId = "1", Before = "p", After = "n" }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetStreamMarkersAsync(new() { UserId = "1", First = 0 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetFollowedStreamsAsync(new() { UserId = "1", First = 101 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateStreamMarkerAsync(new() { UserId = "1", Description = new('x', 141) }));
        await client.CreateStreamMarkerAsync(new() { UserId = "1", Description = string.Concat(Enumerable.Repeat("😀", 140)) });
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StreamEnumeratorsPreserveInitialCursorAndFollowLaterPages(bool markers)
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            var first = ++step == 1;
            Assert.Equal((markers ? "?video_id=v1" : "?user_id=1") + "&first=5&after=" + (first ? "initial" : "next"), request.RequestUri!.Query);
            return Task.FromResult(TestHttpHandler.Json(first ? Fixture(markers ? "get-stream-markers" : "get-followed-streams") : "{\"data\":[],\"pagination\":{}}"));
        }));
        var client = Client(http);
        var count = 0;
        if (markers) await foreach (var _ in client.EnumerateStreamMarkersAsync(new() { VideoId = "v1", First = 5, After = "initial" })) count++;
        else await foreach (var _ in client.EnumerateFollowedStreamsAsync(new() { UserId = "1", First = 5, After = "initial" })) count++;
        Assert.Equal(1, count);
        Assert.Equal(2, step);
        Assert.Throws<ArgumentException>(() => client.EnumerateStreamMarkersAsync(new() { UserId = "1", Before = "previous" }));
    }

    [Fact]
    public async Task ErrorsPreserveTwitchGuidanceAndMarkerNotFound()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? TestHttpHandler.Json("{\"error\":\"Not Found\",\"message\":\"stream must be live with VOD enabled\"}", HttpStatusCode.NotFound)
            : TestHttpHandler.Json("{\"error\":\"Forbidden\",\"message\":\"additional account steps required\"}", HttpStatusCode.Forbidden))));
        var client = Client(http);
        var keyError = await Assert.ThrowsAsync<TwitchApiException>(() => client.GetStreamKeyAsync("1"));
        Assert.Equal("additional account steps required", keyError.Message);
        Assert.Equal(HttpStatusCode.Forbidden, keyError.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<TwitchApiException>(() => client.GetFollowedStreamsAsync(new() { UserId = "1" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<TwitchApiException>(() => client.GetStreamMarkersAsync(new() { VideoId = "v1" }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Assert.ThrowsAsync<TwitchApiException>(() => client.CreateStreamMarkerAsync(new() { UserId = "1" }))).StatusCode);
    }
}
