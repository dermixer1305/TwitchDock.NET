using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TwitchSdk.Core;
using TwitchSdk.Helix;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Tests;

public sealed class HelixFoundationTests
{
    private static HelixClient Client(HttpClient http, AccessToken? token = null) => new(new(http,
        new StaticAccessTokenProvider(token ?? new("test-token")), new() { ClientId = "test-client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 }));
    private static string Fixture(string id) => ContractAssertions.Fixture("helix-foundation.json", id);
    private static CreateEventSubSubscriptionRequest Subscription(EventSubTransportRequest transport) => new()
    {
        Type = "stream.online", Version = "1", Condition = new Dictionary<string, string> { ["broadcaster_user_id"] = "1" }, Transport = transport
    };

    public static IEnumerable<object[]> Contracts()
    {
        var context = HelixJsonContext.Default;
        yield return ["get-users", context.HelixPageTwitchUser];
        yield return ["get-streams", context.HelixPageTwitchStream];
        yield return ["get-channel-information", context.HelixPageChannelInformation];
        yield return ["send-chat-message", context.HelixPageSendChatMessageResult];
        yield return ["create-eventsub-subscription", context.EventSubSubscriptionsResponse];
        yield return ["get-eventsub-subscriptions", context.EventSubSubscriptionsResponse];
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void ResponseContractsPreserveEveryDocumentedField(string id, JsonTypeInfo type)
        => ContractAssertions.Verify("helix-foundation.json", id, type);

    [Fact]
    public async Task UsersAndChannelsEncodeRepeatedFiltersAndDeserializeOptionalMetadata()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Content);
            var users = ++step == 1;
            Assert.Equal(users ? "/helix/users" : "/helix/channels", request.RequestUri!.AbsolutePath);
            Assert.Equal(users ? "?id=1&id=2&login=a%2Bb" : "?broadcaster_id=1&broadcaster_id=2", request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture(users ? "get-users" : "get-channel-information")));
        }));
        var client = Client(http);
        Assert.Equal("tester@example.org", (await client.GetUsersAsync(new() { Ids = ["1", "2"], Logins = ["a+b"] })).Data.Single().Email);
        Assert.True((await client.GetChannelInformationAsync(["1", "2"])).Data.Single().IsBrandedContent);
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task UsersWithoutFiltersRequireUserTokenButNoEmailScope()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            calls++;
            Assert.Equal("", request.RequestUri!.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture("get-users")));
        }));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, new("app", kind: TwitchTokenKind.App)).GetUsersAsync());
        await Client(http, new("user", scopes: [], kind: TwitchTokenKind.User)).GetUsersAsync();
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FilterBoundsFailBeforeSendingAndAcceptCombinedLimit()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"data\":[]}")); }));
        var client = Client(http);
        var hundred = Enumerable.Range(1, 100).Select(i => i.ToString()).ToArray();
        await client.GetUsersAsync(new() { Ids = hundred[..50], Logins = hundred[50..] });
        await client.GetChannelInformationAsync(hundred);
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetUsersAsync(new() { Ids = hundred, Logins = ["extra"] }));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.GetUsersAsync(new() { Ids = null! }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetChannelInformationAsync([]));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetChannelInformationAsync([.. hundred, "extra"]));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetStreamsAsync(new() { UserIds = [.. hundred, "extra"] }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetStreamsAsync(new() { UserLogins = [" "] }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetStreamsAsync(new() { First = 101 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetStreamsAsync(new() { Type = "invalid" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetStreamsAsync(new() { Before = "a", After = "b" }));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task StreamsEnumerationSnapshotsFiltersAndPreservesInitialCursor()
    {
        var ids = new List<string> { "1" };
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal("/helix/streams", request.RequestUri!.AbsolutePath);
            Assert.Equal(++step == 1 ? "?user_id=1&first=1&after=initial" : "?user_id=1&first=1&after=next", request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(step == 1 ? Fixture("get-streams") : "{\"data\":[],\"pagination\":{}}"));
        }));
        var enumerable = Client(http).EnumerateStreamsAsync(new() { UserIds = ids, First = 1, After = "initial" });
        ids[0] = "changed";
        var results = new List<TwitchStream>();
        await foreach (var item in enumerable) results.Add(item);
        Assert.Equal(2147483648L, Assert.Single(results).ViewerCount);
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task ChatSendsReplyAndFalseValuesAndReturnsDropWithoutThrowing()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            step++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/helix/chat/messages", request.RequestUri!.AbsolutePath);
            Assert.Equal("", request.RequestUri.Query);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var body = json.RootElement;
            Assert.Equal("1", body.GetProperty("broadcaster_id").GetString());
            Assert.Equal("2", body.GetProperty("sender_id").GetString());
            Assert.Equal("Hello", body.GetProperty("message").GetString());
            Assert.Equal(step == 2, body.GetProperty("pin").GetBoolean());
            if (step == 1)
            {
                Assert.Equal("parent", body.GetProperty("reply_parent_message_id").GetString());
                Assert.False(body.GetProperty("for_source_only").GetBoolean());
            }
            else Assert.False(body.TryGetProperty("reply_parent_message_id", out _));
            return TestHttpHandler.Json(step == 1 ? Fixture("send-chat-message") : "{\"data\":[{\"message_id\":\"m1\",\"is_sent\":true,\"drop_reason\":null}]}");
        }));
        var client = Client(http, new("app", kind: TwitchTokenKind.App));
        var result = (await client.SendChatMessageAsync(new() { BroadcasterId = "1", SenderId = "2", Message = "Hello", ReplyParentMessageId = "parent", ForSourceOnly = false, Pin = false })).Data.Single();
        Assert.False(result.IsSent);
        Assert.Equal("automod_held", result.DropReason!.Code);
        var sent = (await client.SendChatMessageAsync(new() { BroadcasterId = "1", SenderId = "2", Message = "Hello", Pin = true })).Data.Single();
        Assert.True(sent.IsSent);
        Assert.Null(sent.DropReason);
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task ChatValidatesCodePointLimitPinScopeAndTokenIdentity()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json(Fixture("send-chat-message"))); }));
        var client = Client(http, new("user", scopes: [TwitchScopes.UserWriteChat], kind: TwitchTokenKind.User, userId: "2"));
        await client.SendChatMessageAsync(new() { BroadcasterId = "1", SenderId = "2", Message = string.Concat(Enumerable.Repeat("😀", 500)) });
        await Assert.ThrowsAsync<ArgumentException>(() => client.SendChatMessageAsync(new() { BroadcasterId = "1", SenderId = "2", Message = new('x', 501) }));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => client.SendChatMessageAsync(new() { BroadcasterId = "1", SenderId = "2", Message = "hi", Pin = true }));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => client.SendChatMessageAsync(new() { BroadcasterId = "1", SenderId = "wrong-user", Message = "hi" }));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => client.SendChatMessageAsync(new() { BroadcasterId = "1", SenderId = "2", Message = "hi", ForSourceOnly = false }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.SendChatMessageAsync(new() { BroadcasterId = "1", SenderId = "2", Message = "hi", ReplyParentMessageId = "p", Pin = true }));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("webhook")]
    [InlineData("websocket")]
    [InlineData("conduit")]
    public async Task SubscriptionCreationSerializesOnlyRequestFieldsForSelectedTransport(string method)
    {
        var transport = method switch
        {
            "webhook" => new EventSubTransportRequest { Method = method, Callback = "https://example.org/events", Secret = "secret-12345" },
            "websocket" => new EventSubTransportRequest { Method = method, SessionId = "session1" },
            _ => new EventSubTransportRequest { Method = method, ConduitId = "conduit1" }
        };
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/helix/eventsub/subscriptions", request.RequestUri!.AbsolutePath);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("stream.online", json.RootElement.GetProperty("type").GetString());
            Assert.Equal("1", json.RootElement.GetProperty("version").GetString());
            Assert.Equal("1", json.RootElement.GetProperty("condition").GetProperty("broadcaster_user_id").GetString());
            var actual = json.RootElement.GetProperty("transport");
            Assert.Equal(method, actual.GetProperty("method").GetString());
            Assert.Equal(method == "webhook" ? 3 : 2, actual.EnumerateObject().Count());
            Assert.False(actual.TryGetProperty("connected_at", out _));
            if (method == "webhook") Assert.Equal(transport.Secret, actual.GetProperty("secret").GetString());
            else Assert.Equal(method == "websocket" ? "session1" : "conduit1", actual.GetProperty(method == "websocket" ? "session_id" : "conduit_id").GetString());
            return TestHttpHandler.Json(Fixture("create-eventsub-subscription"), HttpStatusCode.Accepted);
        }));
        await Client(http, new("token", kind: method == "websocket" ? TwitchTokenKind.User : TwitchTokenKind.App)).CreateEventSubSubscriptionAsync(Subscription(transport));
        Assert.DoesNotContain("secret-12345", transport.ToString());
    }

    [Fact]
    public async Task InvalidTransportFieldsAndCredentialsFailBeforeSending()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("No request expected")));
        var client = Client(http);
        EventSubTransportRequest[] invalid =
        [
            new() { Method = "websocket", SessionId = "s", Secret = "secret-12345" },
            new() { Method = "webhook", Callback = "https://example.org", Secret = "secret-12345", ConduitId = "c" },
            new() { Method = "webhook", Callback = "http://example.org", Secret = "secret-12345" },
            new() { Method = "webhook", Callback = "https://example.org:8443", Secret = "secret-12345" },
            new() { Method = "webhook", Callback = "https://example.org", Secret = "short" },
            new() { Method = "webhook", Callback = "https://example.org", Secret = "secret-😀12345" },
            new() { Method = "conduit", ConduitId = "c", SessionId = "s" },
            new() { Method = "invalid" }
        ];
        foreach (var transport in invalid) await Assert.ThrowsAsync<ArgumentException>(() => client.CreateEventSubSubscriptionAsync(Subscription(transport)));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, new("app", kind: TwitchTokenKind.App)).CreateEventSubSubscriptionAsync(Subscription(new() { Method = "websocket", SessionId = "s" })));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, new("user", kind: TwitchTokenKind.User)).CreateEventSubSubscriptionAsync(Subscription(new() { Method = "conduit", ConduitId = "c" })));
    }

    [Fact]
    public async Task SubscriptionListingSupportsEveryExclusiveFilterAndCursor()
    {
        GetEventSubSubscriptionsRequest[] requests = [new() { Status = "future-status" }, new() { Type = "stream.online" }, new() { UserId = "1" }, new() { SubscriptionId = "sub1" }, new() { ConduitId = "c1" }];
        string[] queries = ["status=future-status", "type=stream.online", "user_id=1", "subscription_id=sub1", "conduit_id=c1"];
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/eventsub/subscriptions", request.RequestUri!.AbsolutePath);
            Assert.Equal("?" + queries[step++] + "&after=a%2Bb", request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture("get-eventsub-subscriptions")));
        }));
        var client = Client(http);
        foreach (var request in requests) Assert.Equal(3, (await client.GetEventSubSubscriptionsAsync(request with { After = "a+b" })).Data.Count);
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetEventSubSubscriptionsAsync(new() { Type = "stream.online", UserId = "1" }));
        Assert.Equal(5, step);
    }

    [Fact]
    public async Task SubscriptionEnumerationPreservesFilterAndFollowsEmptyPage()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(++step == 1 ? "?type=stream.online&after=initial" : "?type=stream.online&after=next", request.RequestUri!.Query);
            return Task.FromResult(TestHttpHandler.Json(step == 1 ? "{\"data\":[],\"pagination\":{\"cursor\":\"next\"}}" : Fixture("get-eventsub-subscriptions").Replace("\"cursor\":\"next\"", "\"cursor\":null")));
        }));
        var results = new List<EventSubSubscription>();
        await foreach (var item in Client(http).EnumerateEventSubSubscriptionsAsync(new() { Type = "stream.online", After = "initial" })) results.Add(item);
        Assert.Equal(3, results.Count);
        Assert.NotNull(results[0].Transport.DisconnectedAt);
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task DuplicateSubscriptionConflictRetainsExistingIdAfterResponseDisposal()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) =>
        {
            var response = TestHttpHandler.Json("{\"error\":\"Conflict\",\"status\":409,\"message\":\"subscription already exists\",\"id\":\"existing-sub\"}", HttpStatusCode.Conflict);
            response.Headers.Add("Twitch-Trace-Id", "trace1");
            return Task.FromResult(response);
        }));
        var error = await Assert.ThrowsAsync<TwitchApiException>(() => Client(http).CreateEventSubSubscriptionAsync(Subscription(new() { Method = "websocket", SessionId = "s" })));
        Assert.Equal("existing-sub", error.ExistingSubscriptionId);
        Assert.Equal("trace1", error.RequestId);
        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
    }

    [Fact]
    public async Task AllFoundationEndpointsPreserveHttpErrorsIncludingDeleteNotFound()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json("{\"error\":\"Not Found\",\"status\":404,\"message\":\"missing\"}", HttpStatusCode.NotFound))));
        var client = Client(http);
        Func<Task>[] requests = [() => client.GetUsersAsync(new() { Ids = ["1"] }), () => client.GetStreamsAsync(), () => client.GetChannelInformationAsync(["1"]),
            () => client.SendChatMessageAsync(new() { BroadcasterId = "1", SenderId = "2", Message = "hi" }),
            () => client.CreateEventSubSubscriptionAsync(Subscription(new() { Method = "conduit", ConduitId = "c" })),
            () => client.GetEventSubSubscriptionsAsync(), () => client.DeleteEventSubSubscriptionAsync("sub1")];
        foreach (var request in requests)
        {
            var error = await Assert.ThrowsAsync<TwitchApiException>(request);
            Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
            Assert.Equal("missing", error.Message);
            Assert.Null(error.ExistingSubscriptionId);
        }
    }
}
