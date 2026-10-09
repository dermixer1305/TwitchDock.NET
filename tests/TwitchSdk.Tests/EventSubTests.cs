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
    public void DedupeIsAtomicAndExpiresWithoutEvictingUnexpiredIds()
    {
        var time = new FixedTime();
        var dedupe = new MessageDeduplicator(TimeSpan.FromMinutes(10), 1, time);
        var accepted = 0;
        Parallel.For(0, 100, _ => { if (dedupe.TryAdd("same")) Interlocked.Increment(ref accepted); });
        Assert.Equal(1, accepted);
        Assert.Throws<InvalidOperationException>(() => dedupe.TryAdd("different"));
        time.Now += TimeSpan.FromMinutes(11);
        Assert.True(dedupe.TryAdd("different"));
    }

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
             "payload":{"event":{"broadcaster_user_id":"1","broadcaster_user_login":"channel","broadcaster_user_name":"Channel",
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
        private readonly Channel<EventSubMessage> _messages = Channel.CreateUnbounded<EventSubMessage>();
        public bool Disposed { get; private set; }
        public Uri? Uri { get; private set; }
        public void Enqueue(EventSubMessage message) => _messages.Writer.TryWrite(message);
        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken) { Uri = uri; return Task.CompletedTask; }
        public async Task<EventSubMessage> ReceiveAsync(CancellationToken cancellationToken) => await _messages.Reader.ReadAsync(cancellationToken);
        public ValueTask DisposeAsync() { Disposed = true; _messages.Writer.TryComplete(); return ValueTask.CompletedTask; }
    }
}
