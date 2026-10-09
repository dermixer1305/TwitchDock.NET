using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TwitchSdk.Core;
using TwitchSdk.EventSub;
using TwitchSdk.EventSub.Events;
using TwitchSdk.Helix;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Tests;

public sealed class EventSubTypedTests
{
    private const string Secret = "0123456789abcdef";
    private const string Timestamp = "2026-10-09T12:00:00.123456789Z";

    private static HelixClient Helix(HttpClient http, AccessToken token)
        => new(new(http, new StaticAccessTokenProvider(token), new() { ClientId = "client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 }));

    private static EventSubMessage Notification(string type, string version, string eventJson, string messageType = "notification") => EventSubMessage.Parse(Encoding.UTF8.GetBytes(
        $$$"""{"metadata":{"message_id":"m1","message_type":"{{{messageType}}}","message_timestamp":"{{{Timestamp}}}","subscription_type":"{{{type}}}","subscription_version":"{{{version}}}"},"payload":{"subscription":{"id":"s1","status":"enabled","type":"{{{type}}}","version":"{{{version}}}","condition":{"broadcaster_user_id":"1337"},"transport":{"method":"websocket","session_id":"x"},"created_at":"{{{Timestamp}}}","cost":0},"event":{{{eventJson}}}}}"""));

    [Fact]
    public void StreamEventContractsPreserveEveryDocumentedField()
    {
        var online = EventSubContractAssertions.Verify("eventsub-stream.json", EventSubEvents.StreamOnlineV1);
        Assert.Equal("live", online.Type);
        Assert.Equal(DateTimeOffset.Parse("2020-10-11T10:11:12.1234567Z"), online.StartedAt);
        Assert.Equal("9001", EventSubContractAssertions.Verify("eventsub-stream.json", EventSubEvents.StreamOfflineV1).Id);
    }

    [Fact]
    public void TryReadEventOnlyMatchesNotificationsOfTheSameTypeAndVersion()
    {
        var json = ContractAssertions.Fixture("eventsub-stream.json", "stream.online@1");
        Assert.True(Notification("stream.online", "1", json).TryReadEvent(EventSubEvents.StreamOnlineV1, out var online));
        Assert.Equal("1337", online.BroadcasterUserId);
        Assert.False(Notification("stream.online", "2", json).TryReadEvent(EventSubEvents.StreamOnlineV1, out _));
        Assert.False(Notification("stream.offline", "1", json).TryReadEvent(EventSubEvents.StreamOnlineV1, out _));
        Assert.False(Notification("stream.online", "1", json, "revocation").TryReadEvent(EventSubEvents.StreamOnlineV1, out _));
    }

    [Fact]
    public void RegistryResolvesDefinitionsWithoutDuplicates()
    {
        Assert.True(EventSubEvents.TryGetDefinition("stream.online", "1", out var definition));
        Assert.Same(EventSubEvents.StreamOnlineV1, definition);
        Assert.Equal(typeof(StreamOnlineEvent), definition.EventType);
        Assert.False(EventSubEvents.TryGetDefinition("stream.online", "99", out _));
        Assert.Equal(EventSubEvents.All.Count, EventSubEvents.All.Select(d => d.ToString()).Distinct().Count());
    }

    [Fact]
    public void EveryRegisteredDefinitionIsInventoried()
    {
        using var matrix = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "coverage.json")));
        var inventoried = matrix.RootElement.GetProperty("eventsub").EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToHashSet();
        foreach (var definition in EventSubEvents.All) Assert.Contains(definition.ToString(), inventoried);
    }

    [Fact]
    public async Task RouterDispatchesTypedNotificationsAndRevocations()
    {
        StreamOnlineEvent? received = null;
        EventSubSubscription? revoked = null;
        var router = new EventSubEventRouter()
            .On(EventSubEvents.StreamOnlineV1, (evt, _, _) => { received = evt; return Task.CompletedTask; })
            .OnRevocation((subscription, _) => { revoked = subscription; return Task.CompletedTask; });
        var json = ContractAssertions.Fixture("eventsub-stream.json", "stream.online@1");
        Assert.True(await router.DispatchAsync(Notification("stream.online", "1", json)));
        Assert.Equal("9001", received!.Id);
        Assert.False(await router.DispatchAsync(Notification("stream.offline", "1", json)));
        Assert.True(await router.DispatchAsync(Notification("stream.online", "1", "null", "revocation")));
        Assert.Equal("s1", revoked!.Id);
        Assert.Throws<InvalidOperationException>(() => router.On(EventSubEvents.StreamOnlineV1, (_, _, _) => Task.CompletedTask));
    }

    [Fact]
    public void ConditionsRequireValuesAndOmitUnsetOptionalFields()
    {
        Assert.Throws<ArgumentException>(() => EventSubSubscriptions.StreamOnlineV1(" "));
        var spec = EventSubSubscriptions.StreamOfflineV1("1337");
        Assert.Equal("stream.offline@1", spec.ToString());
        Assert.Equal("1337", Assert.Single(spec.Condition).Value);
        Assert.Equal(EventSubTransports.All, spec.Transports);
    }

    [Fact]
    public async Task TypedWebSocketSubscriptionSendsConditionAndPreflightsScopesAndUser()
    {
        var spec = new EventSubSubscriptionSpec
        {
            Type = "channel.follow", Version = "2", Condition = new Dictionary<string, string> { ["broadcaster_user_id"] = "1", ["moderator_user_id"] = "2" },
            RequiredScopes = [TwitchScopes.ModeratorReadFollowers], AuthorizingUserId = "2",
        };
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("channel.follow", body.RootElement.GetProperty("type").GetString());
            Assert.Equal("2", body.RootElement.GetProperty("condition").GetProperty("moderator_user_id").GetString());
            Assert.Equal("session", body.RootElement.GetProperty("transport").GetProperty("session_id").GetString());
            return TestHttpHandler.Json("""{"data":[],"total":1,"total_cost":0,"max_total_cost":10}""", System.Net.HttpStatusCode.Accepted);
        }));
        await Helix(http, new("user", scopes: [TwitchScopes.ModeratorReadFollowers], kind: TwitchTokenKind.User, userId: "2")).SubscribeWebSocketAsync(spec, "session");
        foreach (var token in new AccessToken[] { new("user", scopes: [], kind: TwitchTokenKind.User, userId: "2"), new("user", scopes: [TwitchScopes.ModeratorReadFollowers], kind: TwitchTokenKind.User, userId: "3"), new("app", kind: TwitchTokenKind.App) })
            await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Helix(http, token).SubscribeWebSocketAsync(spec, "session"));
    }

    [Fact]
    public async Task TypedWebhookSubscriptionRequiresAppTokenAndSupportedTransport()
    {
        var spec = new EventSubSubscriptionSpec { Type = "user.authorization.grant", Version = "1", Condition = new Dictionary<string, string> { ["client_id"] = "client" }, Transports = EventSubTransports.Webhook | EventSubTransports.Conduit };
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("""{"data":[],"total":1,"total_cost":0,"max_total_cost":10}""", System.Net.HttpStatusCode.Accepted)); }));
        var webhook = new EventSubTransportRequest { Method = "webhook", Callback = "https://example.com/callback", Secret = Secret };
        await Helix(http, new("app", kind: TwitchTokenKind.App)).CreateEventSubSubscriptionAsync(spec, webhook);
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Helix(http, new("user", kind: TwitchTokenKind.User)).CreateEventSubSubscriptionAsync(spec, webhook));
        await Assert.ThrowsAsync<ArgumentException>(() => Helix(http, new("user", kind: TwitchTokenKind.User)).SubscribeWebSocketAsync(spec, "session"));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task BatchedEventsPropertyIsReadByTryReadEventAndRouter()
    {
        var batch = ContractAssertions.Fixture("eventsub-community-system.json", "drop.entitlement.grant@1");
        var payload = JsonSerializer.Deserialize(Encoding.UTF8.GetBytes($$"""{"subscription":{"id":"s1","status":"enabled","type":"drop.entitlement.grant","version":"1","condition":{"organization_id":"9001"},"transport":{"method":"webhook","callback":"https://example.com"},"created_at":"2026-10-09T12:00:00Z","cost":0},"events":{{batch}}}"""), EventSubJsonContext.Default.EventSubPayload)!;
        Assert.True(payload.TryReadEvent(EventSubEvents.DropEntitlementGrantV1, out var drops));
        Assert.NotEmpty(drops);
        IReadOnlyList<DropEntitlementGrantEvent>? routed = null;
        var router = new EventSubEventRouter().On(EventSubEvents.DropEntitlementGrantV1, (evt, _, _) => { routed = evt; return Task.CompletedTask; });
        Assert.True(await router.DispatchAsync("notification", payload));
        Assert.Equal(drops.Count, routed!.Count);
    }

    [Fact]
    public async Task BatchedSubscriptionsSendIsBatchingEnabledAndOthersOmitIt()
    {
        var bodies = new List<JsonElement>();
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            bodies.Add(body.RootElement.Clone());
            return TestHttpHandler.Json("""{"data":[],"total":1,"total_cost":0,"max_total_cost":10}""", System.Net.HttpStatusCode.Accepted);
        }));
        var helix = Helix(http, new("app", kind: TwitchTokenKind.App));
        var webhook = new EventSubTransportRequest { Method = "webhook", Callback = "https://example.com/callback", Secret = Secret };
        await helix.CreateEventSubSubscriptionAsync(EventSubSubscriptions.DropEntitlementGrantV1("9001"), webhook);
        await helix.CreateEventSubSubscriptionAsync(EventSubSubscriptions.StreamOnlineV1("1"), webhook);
        Assert.True(bodies[0].GetProperty("is_batching_enabled").GetBoolean());
        Assert.False(bodies[1].TryGetProperty("is_batching_enabled", out _));
    }

    [Fact]
    public async Task WebhookHandlerAnswersChallengeDispatchesOnceAndRejectsForgeries()
    {
        var received = 0;
        var router = new EventSubEventRouter().On(EventSubEvents.StreamOnlineV1, (_, _, _) => { received++; return Task.CompletedTask; });
        var handler = new EventSubWebhookHandler(new EventSubWebhookVerifier(Secret, new FixedTime()), router);

        var challenge = await handler.HandleAsync(Request("webhook_callback_verification", """{"challenge":"pogchamp-kappa-360noscope-vohiyo","subscription":{"id":"s1","status":"webhook_callback_verification_pending","type":"stream.online","version":"1","condition":{},"transport":{"method":"webhook","callback":"https://example.com"},"created_at":"2026-10-09T12:00:00Z","cost":0}}"""));
        Assert.Equal((200, "text/plain", "pogchamp-kappa-360noscope-vohiyo"), (challenge.StatusCode, challenge.ContentType, challenge.Body));

        var notification = Request("notification", WebhookNotification());
        Assert.Equal(204, (await handler.HandleAsync(notification)).StatusCode);
        Assert.Equal(204, (await handler.HandleAsync(notification)).StatusCode);
        Assert.Equal(1, received);

        var forged = new EventSubWebhookRequest { MessageId = "m2", MessageType = "notification", MessageTimestamp = Timestamp, MessageSignature = notification.MessageSignature, Body = notification.Body };
        Assert.Equal(403, (await handler.HandleAsync(forged)).StatusCode);
        Assert.Equal(1, received);
    }

    [Fact]
    public async Task WebhookHandlerFailureReleasesMessageIdSoTheRetryIsProcessed()
    {
        var attempts = 0;
        var router = new EventSubEventRouter().On(EventSubEvents.StreamOnlineV1, (_, _, _) => ++attempts == 1 ? throw new InvalidOperationException("transient") : Task.CompletedTask);
        var handler = new EventSubWebhookHandler(new EventSubWebhookVerifier(Secret, new FixedTime()), router);
        var notification = Request("notification", WebhookNotification());
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(notification));
        Assert.Equal(204, (await handler.HandleAsync(notification)).StatusCode);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task RouterReportsPoisonEventsInsteadOfThrowingAndKeepsHandlerExceptions()
    {
        var reported = new List<(string Type, string Message)>();
        var handled = 0;
        var router = new EventSubEventRouter()
            .On(EventSubEvents.StreamOnlineV1, (_, _, _) => { handled++; return Task.CompletedTask; })
            .On(EventSubEvents.StreamOfflineV1, (_, _, _) => throw new InvalidOperationException("handler failure"))
            .OnDeserializationError((payload, error, _) => { reported.Add((payload.Subscription!.Type, error.Message)); return Task.CompletedTask; });
        Assert.True(await router.DispatchAsync(Notification("stream.online", "1", """{"id":42}""")));
        Assert.True(await router.DispatchAsync(Notification("stream.online", "1", "null")));
        Assert.Equal(0, handled);
        Assert.Equal(["stream.online", "stream.online"], reported.Select(r => r.Type));
        Assert.Throws<InvalidOperationException>(() => router.OnDeserializationError((_, _, _) => Task.CompletedTask));
        // Exceptions from typed handlers are not deserialization errors and still propagate.
        await Assert.ThrowsAsync<InvalidOperationException>(() => router.DispatchAsync(Notification("stream.offline", "1", ContractAssertions.Fixture("eventsub-stream.json", "stream.offline@1"))));
        // Without the hook a poison event is dropped rather than thrown.
        Assert.True(await new EventSubEventRouter().On(EventSubEvents.StreamOnlineV1, (_, _, _) => Task.CompletedTask).DispatchAsync(Notification("stream.online", "1", "[]")));
    }

    [Fact]
    public async Task WebhookAcknowledgesPoisonEventsSoTwitchStopsRetrying()
    {
        var reported = 0;
        var router = new EventSubEventRouter()
            .On(EventSubEvents.StreamOnlineV1, (_, _, _) => throw new InvalidOperationException("must not run"))
            .OnDeserializationError((_, _, _) => { reported++; return Task.CompletedTask; });
        var handler = new EventSubWebhookHandler(new EventSubWebhookVerifier(Secret, new FixedTime()), router);
        var poison = Request("notification", """{"subscription":{"id":"s1","status":"enabled","type":"stream.online","version":"1","condition":{},"transport":{"method":"webhook","callback":"https://example.com"},"created_at":"2026-10-09T12:00:00Z","cost":0},"event":{"id":42}}""");
        Assert.Equal(204, (await handler.HandleAsync(poison)).StatusCode);
        Assert.Equal(204, (await handler.HandleAsync(poison)).StatusCode);
        Assert.Equal(1, reported);
    }

    [Theory]
    // The message type header is not signed; it must agree with the signed body.
    [InlineData("notification", """{"challenge":"c","subscription":{"id":"s1","status":"enabled","type":"stream.online","version":"1","condition":{},"transport":{"method":"webhook","callback":"https://example.com"},"created_at":"2026-10-09T12:00:00Z","cost":0},"event":{}}""")]
    [InlineData("notification", """{"subscription":{"id":"s1","status":"enabled","type":"stream.online","version":"1","condition":{},"transport":{"method":"webhook","callback":"https://example.com"},"created_at":"2026-10-09T12:00:00Z","cost":0}}""")]
    [InlineData("notification", """{"subscription":{"id":"s1","status":"enabled","type":"stream.online","version":"1","condition":{},"transport":{"method":"webhook","callback":"https://example.com"},"created_at":"2026-10-09T12:00:00Z","cost":0},"event":null}""")]
    [InlineData("notification", """{"challenge":"c"}""")]
    [InlineData("revocation", """{"subscription":{"id":"s1","status":"enabled","type":"stream.online","version":"1","condition":{},"transport":{"method":"webhook","callback":"https://example.com"},"created_at":"2026-10-09T12:00:00Z","cost":0},"event":{}}""")]
    [InlineData("revocation", """{"challenge":"c","subscription":{"id":"s1","status":"authorization_revoked","type":"stream.online","version":"1","condition":{},"transport":{"method":"webhook","callback":"https://example.com"},"created_at":"2026-10-09T12:00:00Z","cost":0}}""")]
    [InlineData("revocation", """{}""")]
    [InlineData("webhook_callback_verification", """{"subscription":{"id":"s1","status":"enabled","type":"stream.online","version":"1","condition":{},"transport":{"method":"webhook","callback":"https://example.com"},"created_at":"2026-10-09T12:00:00Z","cost":0},"event":{}}""")]
    public async Task WebhookRejectsAMessageTypeThatContradictsTheSignedBody(string messageType, string body)
    {
        var calls = 0;
        var router = new EventSubEventRouter()
            .On(EventSubEvents.StreamOnlineV1, (_, _, _) => { calls++; return Task.CompletedTask; })
            .OnRevocation((_, _) => { calls++; return Task.CompletedTask; });
        var dedupe = new MessageDeduplicator(timeProvider: new FixedTime());
        var handler = new EventSubWebhookHandler(new EventSubWebhookVerifier(Secret, new FixedTime()), router, dedupe);
        Assert.Equal(400, (await handler.HandleAsync(Request(messageType, body))).StatusCode);
        Assert.Equal(0, calls);
        // A rejected request does not consume its message ID.
        Assert.True(dedupe.TryAdd("m1"));
    }

    [Fact]
    public async Task WebhookDispatchesRevocationsWhoseSubscriptionIsNoLongerEnabled()
    {
        EventSubSubscription? revoked = null;
        var router = new EventSubEventRouter().OnRevocation((subscription, _) => { revoked = subscription; return Task.CompletedTask; });
        var handler = new EventSubWebhookHandler(new EventSubWebhookVerifier(Secret, new FixedTime()), router);
        var response = await handler.HandleAsync(Request("revocation", """{"subscription":{"id":"s1","status":"authorization_revoked","type":"stream.online","version":"1","condition":{},"transport":{"method":"webhook","callback":"https://example.com"},"created_at":"2026-10-09T12:00:00Z","cost":0}}"""));
        Assert.Equal(204, response.StatusCode);
        Assert.Equal("authorization_revoked", revoked!.Status);
    }

    [Fact]
    public async Task FullFailClosedDeduplicatorAnswersServiceUnavailable()
    {
        var received = 0;
        var router = new EventSubEventRouter().On(EventSubEvents.StreamOnlineV1, (_, _, _) => { received++; return Task.CompletedTask; });
        var dedupe = new MessageDeduplicator(capacity: 1, timeProvider: new FixedTime(), throwWhenFull: true);
        dedupe.TryAdd("earlier");
        var handler = new EventSubWebhookHandler(new EventSubWebhookVerifier(Secret, new FixedTime()), router, dedupe);
        Assert.Equal(503, (await handler.HandleAsync(Request("notification", WebhookNotification()))).StatusCode);
        Assert.Equal(0, received);
    }

    [Fact]
    public void WebhookRequestReadsTwitchHeadersThroughHostLookup()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["twitch-eventsub-message-id"] = "id", ["Twitch-Eventsub-Message-Type"] = "notification", ["Twitch-Eventsub-Message-Timestamp"] = Timestamp, ["Twitch-Eventsub-Message-Signature"] = "sha256=00" };
        var request = EventSubWebhookRequest.FromHeaders(name => headers.GetValueOrDefault(name), new byte[] { 1 });
        Assert.Equal(("id", "notification", Timestamp, "sha256=00"), (request.MessageId, request.MessageType, request.MessageTimestamp, request.MessageSignature));
        Assert.Equal("", EventSubWebhookRequest.FromHeaders(_ => null, default).MessageId);
    }

    private static string WebhookNotification() => $$"""{"subscription":{"id":"s1","status":"enabled","type":"stream.online","version":"1","condition":{"broadcaster_user_id":"1337"},"transport":{"method":"webhook","callback":"https://example.com"},"created_at":"2026-10-09T12:00:00Z","cost":0},"event":{{ContractAssertions.Fixture("eventsub-stream.json", "stream.online@1")}}}""";

    private static EventSubWebhookRequest Request(string type, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var signature = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.ASCII.GetBytes(Secret), Encoding.UTF8.GetBytes("m1" + Timestamp).Concat(bytes).ToArray())).ToLowerInvariant();
        return new() { MessageId = "m1", MessageType = type, MessageTimestamp = Timestamp, MessageSignature = signature, Body = bytes };
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-10-09T12:00:00Z");
    }
}
