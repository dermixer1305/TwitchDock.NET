using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using TwitchSdk.Chat;
using TwitchSdk.EventSub;

namespace TwitchSdk.Tests;

public sealed class EventSubTests
{
    [Fact]
    public async Task KeepaliveTimeoutCreatesFreshSessionAndRequestsResubscription()
    {
        var time = new ManualTimeProvider();
        var old = new FakeConnection();
        var replacement = new FakeConnection();
        old.Enqueue(Welcome("old"));
        replacement.Enqueue(Welcome("new"));
        var connections = new Queue<FakeConnection>([old, replacement]);
        var sessions = new List<string>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var loop = new EventSubWebSocketClient(() => connections.Dequeue(), timeProvider: time).RunAsync((session, resubscribe, _) =>
        {
            Assert.True(resubscribe);
            sessions.Add(session.Id);
            if (session.Id == "new") stop.Cancel();
            return Task.CompletedTask;
        }, (_, _) => Task.CompletedTask, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => time.TimerCount == 1);
        time.Advance(TimeSpan.FromSeconds(11));
        await ManualTimeProvider.WaitUntilAsync(() => old.Disposed && time.TimerCount == 1);
        time.Advance(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loop);
        Assert.Equal(new[] { "old", "new" }, sessions);
    }

    [Fact]
    public async Task MigrationCallbackSocketExceptionPropagatesAndReleasesDuplicateReservation()
    {
        var old = new FakeConnection();
        var replacement = new FakeConnection();
        old.Enqueue(Welcome("old"));
        old.Enqueue(Message("session_reconnect", "r", new() { Session = new() { Id = "old", Status = "reconnecting", ReconnectUrl = "wss://eventsub.wss.twitch.tv/ws?r=1" } }));
        old.Enqueue(Notification("failed"));
        var connections = new Queue<FakeConnection>([old, replacement]);
        var dedupe = new MessageDeduplicator();
        var failure = new System.Net.WebSockets.WebSocketException("Consumer-owned socket failed.");
        var error = await Assert.ThrowsAsync<System.Net.WebSockets.WebSocketException>(() => new EventSubWebSocketClient(() => connections.Dequeue(), dedupe).RunAsync((_, _, _) => Task.CompletedTask, (_, _) => throw failure));
        Assert.Same(failure, error);
        Assert.True(old.Disposed);
        Assert.True(replacement.Disposed);
        Assert.True(dedupe.TryAdd("failed"));
    }

    private const string Secret = "test-secret-at-least-ten-characters";
    private sealed class FixedTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-10-09T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void WebhookAuthenticatesRawBytesAndNanosecondTimestampBeforeParsing()
    {
        var time = new FixedTime();
        var bytes = Encoding.UTF8.GetBytes("{ \"challenge\" : \"hello\" }");
        const string timestamp = "2026-10-09T12:00:00.123456789Z";
        var signature = Sign("id", timestamp, bytes);
        var verifier = new EventSubWebhookVerifier(Secret, time);
        Assert.Equal("hello", verifier.VerifyAndParse("id", timestamp, signature, bytes).Challenge);
        Assert.Throws<CryptographicException>(() => verifier.VerifyAndParse("id", timestamp, signature, Encoding.UTF8.GetBytes("{\"challenge\":\"hello\"}")));
        Assert.Throws<CryptographicException>(() => verifier.VerifyAndParse("other", timestamp, signature, bytes));
    }

    [Theory]
    [InlineData("2026-10-09T11:49:59Z")]
    [InlineData("2026-10-09T12:02:00Z")]
    [InlineData("invalid")]
    public void WebhookRejectsStaleFutureAndMalformedTimestamp(string timestamp)
    {
        var bytes = Encoding.UTF8.GetBytes("{}");
        Assert.Throws<CryptographicException>(() => new EventSubWebhookVerifier(Secret, new FixedTime()).VerifyAndParse("id", timestamp, Sign("id", timestamp, bytes), bytes));
    }

    [Theory]
    [InlineData("sha256=bad")]
    [InlineData("sha256=zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void WebhookRejectsInvalidSignatureFormat(string signature)
    {
        Assert.Throws<CryptographicException>(() => new EventSubWebhookVerifier(Secret, new FixedTime()).VerifyAndParse("id", "2026-10-09T12:00:00Z", signature, "{}"u8));
    }

    [Fact]
    public void DedupeIsAtomicAndEvictsTheOldestIdWhenFull()
    {
        var time = new FixedTime();
        var dedupe = new MessageDeduplicator(TimeSpan.FromMinutes(10), 2, time);
        var accepted = 0;
        Parallel.For(0, 100, _ => { if (dedupe.TryAdd("same")) Interlocked.Increment(ref accepted); });
        Assert.Equal(1, accepted);
        Assert.True(dedupe.TryAdd("second"));
        Assert.True(dedupe.TryAdd("third"));
        Assert.Equal(1, dedupe.EvictedCount);
        // The oldest ID was evicted; the newer ones are still suppressed.
        Assert.False(dedupe.TryAdd("third"));
        Assert.False(dedupe.TryAdd("second"));
        Assert.True(dedupe.TryAdd("same"));
        Assert.Equal(2, dedupe.EvictedCount);
    }

    [Fact]
    public void FailClosedDedupeThrowsWhenFullUntilIdsExpire()
    {
        var time = new FixedTime();
        var dedupe = new MessageDeduplicator(TimeSpan.FromMinutes(10), 1, time, throwWhenFull: true);
        Assert.True(dedupe.TryAdd("first"));
        Assert.IsAssignableFrom<InvalidOperationException>(Assert.Throws<EventSubDeduplicationException>(() => dedupe.TryAdd("different")));
        Assert.Equal(0, dedupe.EvictedCount);
        time.Now += TimeSpan.FromMinutes(10);
        Assert.True(dedupe.TryAdd("different"));
    }

    [Fact]
    public void DedupeDefaultsCoverTheWebhookFreshnessWindowAndHighThroughput()
    {
        Assert.Equal(100_000, MessageDeduplicator.DefaultCapacity);
        Assert.Equal(TimeSpan.FromMinutes(11), MessageDeduplicator.DefaultRetention);
        var time = new FixedTime();
        var dedupe = new MessageDeduplicator(timeProvider: time);
        Assert.True(dedupe.TryAdd("id"));
        // A webhook timestamp may be 10 minutes old or 1 minute in the future, so a replay is possible for 11 minutes.
        time.Now += TimeSpan.FromMinutes(11) - TimeSpan.FromTicks(1);
        Assert.False(dedupe.TryAdd("id"));
        time.Now += TimeSpan.FromTicks(1);
        Assert.True(dedupe.TryAdd("id"));
        // About 150 messages per second for 11 minutes fit without eviction (the old default failed at about 17 per second).
        for (var i = 0; i < 99_999; i++) dedupe.TryAdd("m" + i);
        Assert.Equal(0, dedupe.EvictedCount);
    }

    [Fact]
    public async Task FullDefaultDedupeEvictsInsteadOfStoppingTheWebSocketClient()
    {
        var connection = new FakeConnection();
        connection.Enqueue(Welcome("s"));
        foreach (var id in new[] { "n1", "n2", "n3" }) connection.Enqueue(Notification(id));
        var dedupe = new MessageDeduplicator(capacity: 2);
        var received = new List<string>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new EventSubWebSocketClient(() => connection, dedupe).RunAsync((_, _, _) => Task.CompletedTask, (message, _) =>
        {
            received.Add(message.Metadata.MessageId);
            if (received.Count == 3) stop.Cancel();
            return Task.CompletedTask;
        }, stop.Token));
        Assert.Equal(["n1", "n2", "n3"], received);
        Assert.Equal(1, dedupe.EvictedCount);
    }

    [Fact]
    public async Task FailClosedDedupeStopsTheWebSocketClientWithATypedException()
    {
        var connection = new FakeConnection();
        connection.Enqueue(Welcome("s"));
        connection.Enqueue(Notification("first"));
        connection.Enqueue(Notification("second"));
        var received = new List<string>();
        var dedupe = new MessageDeduplicator(capacity: 1, throwWhenFull: true);
        await Assert.ThrowsAsync<EventSubDeduplicationException>(() => new EventSubWebSocketClient(() => connection, dedupe)
            .RunAsync((_, _, _) => Task.CompletedTask, (message, _) => { received.Add(message.Metadata.MessageId); return Task.CompletedTask; }));
        Assert.Equal(["first"], received);
        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task NotificationWithoutAnIdIsDeliveredInsteadOfStoppingTheClient()
    {
        var connection = new FakeConnection();
        connection.Enqueue(Welcome("s"));
        connection.Enqueue(Notification(""));
        connection.Enqueue(Notification(" "));
        var received = 0;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new EventSubWebSocketClient(() => connection).RunAsync((_, _, _) => Task.CompletedTask, (_, _) =>
        {
            if (++received == 2) stop.Cancel();
            return Task.CompletedTask;
        }, stop.Token));
        Assert.Equal(2, received);
    }

    [Fact]
    public async Task PoisonEventIsReportedThroughTheRouterAndTheWebSocketClientKeepsRunning()
    {
        var connection = new FakeConnection();
        connection.Enqueue(Welcome("s"));
        connection.Enqueue(StreamOnline("poison", "\"not an object\""));
        connection.Enqueue(StreamOnline("valid", ContractAssertions.Fixture("eventsub-stream.json", "stream.online@1")));
        var errors = new List<string>();
        var online = new List<string>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var router = new EventSubEventRouter()
            .On(EventSubEvents.StreamOnlineV1, (evt, _, _) => { online.Add(evt.Id); stop.Cancel(); return Task.CompletedTask; })
            .OnDeserializationError((payload, error, _) => { errors.Add(payload.Subscription!.Id + ": " + error.GetType().Name); return Task.CompletedTask; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new EventSubWebSocketClient(() => connection)
            .RunAsync((_, _, _) => Task.CompletedTask, (message, ct) => router.DispatchAsync(message, ct), stop.Token));
        Assert.Equal(["s1: JsonException"], errors);
        Assert.Equal(["9001"], online);
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("keepalive-timeout")]
    [InlineData("invalid-frame")]
    public async Task MigrationKeepsTheReplacementWhenTheOldSocketFailsBeforeTheWelcome(string failure)
    {
        var time = new ManualTimeProvider();
        var old = new FakeConnection();
        var replacement = new FakeConnection();
        old.Enqueue(Welcome("old"));
        old.Enqueue(Message("session_reconnect", "reconnect", new() { Session = new() { Id = "old", Status = "reconnecting", ReconnectUrl = "wss://eventsub.wss.twitch.tv/ws?session=opaque" } }));
        if (failure == "closed") old.Fail(new System.Net.WebSockets.WebSocketException("EventSub connection closed (1000)."));
        if (failure == "invalid-frame") old.Fail(new JsonException("Expected an EventSub text message."));
        var connections = new Queue<FakeConnection>([old, replacement]);
        var sessions = new List<(string Id, bool Resubscribe)>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = new EventSubWebSocketClient(() => connections.Dequeue(), timeProvider: time).RunAsync((session, resubscribe, _) =>
        {
            sessions.Add((session.Id, resubscribe));
            if (session.Id == "new") stop.Cancel();
            return Task.CompletedTask;
        }, (_, _) => Task.CompletedTask, stop.Token);
        if (failure == "keepalive-timeout")
        {
            // The migration read on the old socket (keepalive 10 s) and the replacement connect timeout (30 s) are pending.
            await ManualTimeProvider.WaitUntilAsync(() => old.Receives == 3 && time.TimerCount == 2);
            time.Advance(TimeSpan.FromSeconds(11));
        }
        await ManualTimeProvider.WaitUntilAsync(() => old.FailureObserved);
        replacement.Enqueue(Welcome("new"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal([("old", true), ("new", false)], sessions);
        Assert.True(old.Disposed);
        Assert.True(replacement.Disposed);
    }

    [Fact]
    public async Task MigrationDisposesTheReplacementWhenTheOldReadFailsUnexpectedly()
    {
        var old = new FakeConnection { FailureOnCancel = new InvalidOperationException("broken connection") };
        var replacement = new FakeConnection();
        old.Enqueue(Welcome("old"));
        old.Enqueue(Message("session_reconnect", "reconnect", new() { Session = new() { Id = "old", Status = "reconnecting", ReconnectUrl = "wss://eventsub.wss.twitch.tv/ws?r=1" } }));
        var connections = new Queue<FakeConnection>([old, replacement]);
        var run = new EventSubWebSocketClient(() => connections.Dequeue()).RunAsync((_, _, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);
        // Welcome, reconnect, then the migration read that is still pending when the replacement welcome arrives.
        await ManualTimeProvider.WaitUntilAsync(() => old.Receives == 3);
        replacement.Enqueue(Welcome("new"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => run);
        Assert.Equal("broken connection", error.Message);
        Assert.True(old.Disposed);
        Assert.Equal(1, replacement.DisposeCount);
    }

    [Fact]
    public async Task FailingDisposeNeitherMasksACallbackFailureNorStopsAMigration()
    {
        var connection = new FakeConnection { DisposeFailure = new IOException("dispose failed") };
        connection.Enqueue(Welcome("s"));
        connection.Enqueue(Notification("m"));
        var failure = new InvalidOperationException("consumer failure");
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => new EventSubWebSocketClient(() => connection).RunAsync((_, _, _) => Task.CompletedTask, (_, _) => throw failure)));

        var old = new FakeConnection { DisposeFailure = new IOException("dispose failed") };
        var replacement = new FakeConnection();
        old.Enqueue(Welcome("old"));
        old.Enqueue(Message("session_reconnect", "reconnect", new() { Session = new() { Id = "old", Status = "reconnecting", ReconnectUrl = "wss://eventsub.wss.twitch.tv/ws?r=1" } }));
        replacement.Enqueue(Welcome("new"));
        var connections = new Queue<FakeConnection>([old, replacement]);
        var sessions = new List<(string Id, bool Resubscribe)>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new EventSubWebSocketClient(() => connections.Dequeue()).RunAsync((session, resubscribe, _) =>
        {
            sessions.Add((session.Id, resubscribe));
            if (session.Id == "new") stop.Cancel();
            return Task.CompletedTask;
        }, (_, _) => Task.CompletedTask, stop.Token));
        Assert.Equal([("old", true), ("new", false)], sessions);
        Assert.Equal((1, 1), (old.DisposeCount, replacement.DisposeCount));
    }

    [Fact]
    public async Task MigrationDisposesTheReplacementWhenTheOldSocketFailsWithAnUnexpectedException()
    {
        var old = new FakeConnection();
        var replacement = new FakeConnection();
        old.Enqueue(Welcome("old"));
        old.Enqueue(Message("session_reconnect", "reconnect", new() { Session = new() { Id = "old", Status = "reconnecting", ReconnectUrl = "wss://eventsub.wss.twitch.tv/ws?r=1" } }));
        old.Fail(new InvalidOperationException("broken connection"));
        var connections = new Queue<FakeConnection>([old, replacement]);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new EventSubWebSocketClient(() => connections.Dequeue())
            .RunAsync((_, _, _) => Task.CompletedTask, (_, _) => Task.CompletedTask));
        Assert.Equal("broken connection", error.Message);
        Assert.True(old.Disposed);
        Assert.True(replacement.Disposed);
    }

    [Fact]
    public async Task ReconnectBackoffIsJitteredWithinEachExponentialStep()
    {
        var time = new ManualTimeProvider();
        var attempts = 0;
        using var stop = new CancellationTokenSource();
        var run = new EventSubWebSocketClient(() =>
        {
            Interlocked.Increment(ref attempts);
            throw new System.Net.WebSockets.WebSocketException("Connection refused.");
        }, timeProvider: time).RunAsync((_, _, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, stop.Token);
        foreach (var step in new[] { 1, 2, 4, 8, 16, 30, 30 })
        {
            await ManualTimeProvider.WaitUntilAsync(() => time.TimerCount == 1);
            var attempt = Volatile.Read(ref attempts);
            // Each delay lies in [step / 2, step): never shorter than half the step, never longer than the step.
            time.Advance(TimeSpan.FromSeconds(step / 2.0) - TimeSpan.FromMilliseconds(1));
            Assert.Equal((attempt, 1), (Volatile.Read(ref attempts), time.TimerCount));
            time.Advance(TimeSpan.FromSeconds(step / 2.0) + TimeSpan.FromMilliseconds(1));
            await ManualTimeProvider.WaitUntilAsync(() => Volatile.Read(ref attempts) == attempt + 1);
        }
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Theory]
    [InlineData("ws://example.org/ws")]
    [InlineData("ws://localhost.example.org/ws")]
    [InlineData("ws://user@127.0.0.1/ws")]
    [InlineData("wss://user@eventsub.wss.twitch.tv/ws")]
    [InlineData("http://127.0.0.1/ws")]
    public async Task WebSocketConnectionRejectsInsecureOrNonLoopbackEndpoints(string uri)
    {
        await using var connection = new ClientWebSocketConnection();
        await Assert.ThrowsAsync<ArgumentException>(() => connection.ConnectAsync(new Uri(uri), new CancellationToken(true)));
        Assert.Throws<ArgumentException>(() => new EventSubWebSocketClient(endpoint: new Uri(uri)));
    }

    [Theory]
    [InlineData("wss://eventsub.wss.twitch.tv/ws")]
    [InlineData("ws://127.0.0.1:8080/ws")]
    [InlineData("ws://[::1]:8080/ws")]
    [InlineData("ws://localhost:8080/ws")]
    public async Task WebSocketConnectionAcceptsTlsAndLoopbackEndpoints(string uri)
    {
        await using var connection = new ClientWebSocketConnection();
        // The endpoint passes validation; the cancelled token then stops the connect attempt.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection.ConnectAsync(new Uri(uri), new CancellationToken(true)));
        Assert.NotNull(new EventSubWebSocketClient(endpoint: new Uri(uri)));
    }

    [Fact]
    public void ReadingAMissingOrNullEventFailsCleanly()
    {
        foreach (var payload in new[] { $$"""{"subscription":{{SubscriptionObject}}}""", $$"""{"subscription":{{SubscriptionObject}},"event":null}""" })
        {
            var message = EventSubMessage.Parse(Encoding.UTF8.GetBytes(
                $$"""{"metadata":{"message_id":"m","message_type":"notification","message_timestamp":"2026-10-09T12:00:00Z"},"payload":{{payload}}}"""));
            Assert.False(message.TryReadEvent(EventSubEvents.StreamOnlineV1, out _));
            Assert.False(message.Payload.TryReadEvent(EventSubEvents.StreamOnlineV1, out _));
            Assert.Contains("no event", Assert.Throws<JsonException>(() => message.ReadEvent(EventSubEventsJsonContext.Default.StreamOnlineEvent)).Message);
        }
    }

    private const string SubscriptionObject = """{"id":"s1","status":"enabled","type":"stream.online","version":"1","condition":{},"transport":{"method":"websocket","session_id":"x"},"created_at":"2026-10-09T12:00:00Z","cost":0}""";

    private static EventSubMessage StreamOnline(string id, string eventJson) => EventSubMessage.Parse(Encoding.UTF8.GetBytes(
        $$$"""{"metadata":{"message_id":"{{{id}}}","message_type":"notification","message_timestamp":"2026-10-09T12:00:00Z","subscription_type":"stream.online","subscription_version":"1"},"payload":{"subscription":{{{SubscriptionObject}}},"event":{{{eventJson}}}}}"""));

    [Fact]
    public async Task WebSocketSuppressesDuplicatesAndSurfacesRevocations()
    {
        var connection = new FakeConnection();
        connection.Enqueue(Welcome("s1"));
        connection.Enqueue(Notification("duplicate"));
        connection.Enqueue(Notification("duplicate"));
        connection.Enqueue(Message("revocation", "revoked"));
        var received = new List<string>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var client = new EventSubWebSocketClient(() => connection);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.RunAsync((session, resubscribe, _) =>
        {
            Assert.Equal("s1", session.Id);
            Assert.True(resubscribe);
            return Task.CompletedTask;
        }, (message, _) =>
        {
            received.Add(message.Metadata.MessageId);
            if (received.Count == 2) stop.Cancel();
            return Task.CompletedTask;
        }, stop.Token));
        Assert.Equal(new[] { "duplicate", "revoked" }, received);
        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task MigrationDrainsOldConnectionUntilReplacementWelcomeWithoutResubscribing()
    {
        var old = new FakeConnection();
        var replacement = new FakeConnection();
        old.Enqueue(Welcome("old"));
        old.Enqueue(Message("session_reconnect", "reconnect", new() { Session = new() { Id = "old", Status = "reconnecting", ReconnectUrl = "wss://eventsub.wss.twitch.tv/ws?session=opaque" } }));
        old.Enqueue(Notification("during-migration"));
        var connections = new Queue<FakeConnection>([old, replacement]);
        var sessions = new List<bool>();
        var received = new List<string>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var client = new EventSubWebSocketClient(() => connections.Dequeue());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.RunAsync((session, resubscribe, _) =>
        {
            sessions.Add(resubscribe);
            if (!resubscribe)
            {
                Assert.True(old.Disposed);
                Assert.Equal("new", session.Id);
                stop.Cancel();
            }
            return Task.CompletedTask;
        }, (message, _) =>
        {
            Assert.False(old.Disposed);
            received.Add(message.Metadata.MessageId);
            replacement.Enqueue(Welcome("new"));
            return Task.CompletedTask;
        }, stop.Token));
        Assert.Equal(new[] { true, false }, sessions);
        Assert.Equal("during-migration", Assert.Single(received));
        Assert.Equal("wss://eventsub.wss.twitch.tv/ws?session=opaque", replacement.Uri!.AbsoluteUri);
        Assert.True(replacement.Disposed);
    }

    [Fact]
    public async Task CallbackFailureStopsLoopAndDisposesConnection()
    {
        var connection = new FakeConnection();
        connection.Enqueue(Welcome("s"));
        connection.Enqueue(Notification("m"));
        var failure = new InvalidOperationException("consumer failure");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new EventSubWebSocketClient(() => connection).RunAsync((_, _, _) => Task.CompletedTask, (_, _) => throw failure));
        Assert.Same(failure, error);
        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task RejectsReconnectToAnotherOrigin()
    {
        var connection = new FakeConnection();
        connection.Enqueue(Welcome("s"));
        connection.Enqueue(Message("session_reconnect", "bad", new() { Session = new() { Id = "s", Status = "reconnecting", ReconnectUrl = "wss://example.org/" } }));
        await Assert.ThrowsAsync<JsonException>(() => new EventSubWebSocketClient(() => connection).RunAsync((_, _, _) => Task.CompletedTask, (_, _) => Task.CompletedTask));
        Assert.True(connection.Disposed);
    }

    [Fact]
    public void ChatReadsSharedChatMetadataAndPreservesUnknownMessageKinds()
    {
        var message = EventSubMessage.Parse("""
            {"metadata":{"message_id":"event","message_type":"notification","message_timestamp":"2026-10-09T12:00:00.123456789Z",
             "subscription_type":"channel.chat.message","subscription_version":"1"},
             "payload":{"subscription":{"id":"s1","status":"enabled","type":"channel.chat.message","version":"1","condition":{"broadcaster_user_id":"1","user_id":"2"},
             "transport":{"method":"websocket","session_id":"x"},"created_at":"2026-10-09T12:00:00Z","cost":0},
             "event":{"broadcaster_user_id":"1","broadcaster_user_login":"channel","broadcaster_user_name":"Channel",
             "chatter_user_id":"2","chatter_user_login":"person","chatter_user_name":"Person","message_id":"chat",
             "message":{"text":"hi","fragments":[{"type":"gif","text":"hi","gif":{"id":"gif-1","url":"https://example.org/gif"}}]},"message_type":"future_kind",
             "source_broadcaster_user_id":"3","source_message_id":"source","source_badges":[],"is_source_only":false}}}
            """u8);
        Assert.True(TwitchChatClient.TryReadMessage(message, out var chat));
        Assert.Equal("future_kind", chat!.MessageType);
        Assert.Equal("3", chat.SourceBroadcasterUserId);
        Assert.False(chat.IsSourceOnly);
        Assert.Equal("gif-1", Assert.Single(chat.Message.Fragments).Gif!.Id);
    }

    private static string Sign(string id, string timestamp, byte[] body) => "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.ASCII.GetBytes(Secret), Encoding.UTF8.GetBytes(id + timestamp).Concat(body).ToArray())).ToLowerInvariant();
    private static EventSubMessage Welcome(string id) => Message("session_welcome", "welcome-" + id, new() { Session = new() { Id = id, Status = "connected", KeepaliveTimeoutSeconds = 10 } });
    private static EventSubMessage Notification(string id) => Message("notification", id);
    private static EventSubMessage Message(string type, string id, EventSubPayload? payload = null) => new()
    {
        Metadata = new() { MessageId = id, MessageType = type, MessageTimestamp = "2026-10-09T12:00:00Z" }, Payload = payload ?? new()
    };

    private sealed class FakeConnection : IEventSubConnection
    {
        private readonly Channel<object> _messages = Channel.CreateUnbounded<object>();
        private int _receives;
        private volatile bool _failureObserved;
        public bool Disposed { get; private set; }
        public Uri? Uri { get; private set; }
        /// <summary>Thrown instead of OperationCanceledException when a read is cancelled, like a misbehaving connection.</summary>
        public Exception? FailureOnCancel { get; init; }
        public int Receives => Volatile.Read(ref _receives);
        public bool FailureObserved => _failureObserved;
        public void Enqueue(EventSubMessage message) => _messages.Writer.TryWrite(message);
        /// <summary>Makes the next read throw, for example a WebSocketException when Twitch closes the socket.</summary>
        public void Fail(Exception error) => _messages.Writer.TryWrite(error);
        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken) { Uri = uri; return Task.CompletedTask; }

        public async Task<EventSubMessage> ReceiveAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _receives);
            object item;
            try { item = await _messages.Reader.ReadAsync(cancellationToken); }
            catch (OperationCanceledException)
            {
                _failureObserved = true;
                if (FailureOnCancel is not null) throw FailureOnCancel;
                throw;
            }
            if (item is Exception error) { _failureObserved = true; throw error; }
            return (EventSubMessage)item;
        }

        /// <summary>Thrown by DisposeAsync, like a misbehaving custom connection.</summary>
        public Exception? DisposeFailure { get; init; }
        public int DisposeCount => Volatile.Read(ref _disposals);
        private int _disposals;

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposals);
            Disposed = true;
            _messages.Writer.TryComplete();
            return DisposeFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(DisposeFailure);
        }
    }
}
