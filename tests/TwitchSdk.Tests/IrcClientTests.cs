using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TwitchSdk.Chat.Irc;
using TwitchSdk.Core;

namespace TwitchSdk.Tests;

public sealed class IrcClientTests
{
    private const string Token = "secret-token";
    private const string CapReq = "CAP REQ :twitch.tv/tags twitch.tv/commands twitch.tv/membership";
    private const string Ping = "PING :tmi.twitch.tv";

    [Fact]
    public async Task LoginSendsCapPassNickInOrderThenJoinsRegisteredChannels()
    {
        var connection = new FakeIrcConnection();
        var logger = new CapturingLogger();
        var client = new TwitchIrcClient(Tokens(), new TwitchIrcOptions { Login = "MyBot" }, () => connection, logger: logger);
        await client.JoinAsync("#SomeChannel");
        Assert.Equal(new[] { "somechannel" }, client.JoinedChannels);
        var commands = new ConcurrentQueue<string>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = client.RunAsync((message, _) => { commands.Enqueue(message.Command); return Task.CompletedTask; }, stop.Token);
        await connection.WaitForSentAsync(line => line == "JOIN #somechannel");
        Assert.Equal(new[] { CapReq, "PASS oauth:" + Token, "NICK mybot", "JOIN #somechannel" }, connection.Sent);
        Assert.Equal(TwitchIrcOptions.DefaultEndpoint, connection.Uri);
        await ManualTimeProvider.WaitUntilAsync(() => commands.Contains("001"));
        Assert.Equal(new[] { "CAP", "001" }, commands);
        Assert.True(client.IsConnected);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.True(connection.Disposed);
        Assert.False(client.IsConnected);
        Assert.DoesNotContain(logger.Entries, entry => entry.Contains(Token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task OauthPrefixIsNotDuplicatedAndEmptyCapabilitiesSkipCapReq()
    {
        var connection = new FakeIrcConnection();
        var tokens = new StaticAccessTokenProvider(new AccessToken("oauth:abc", scopes: [TwitchScopes.ChatRead], kind: TwitchTokenKind.User));
        var client = new TwitchIrcClient(tokens, new TwitchIrcOptions { Login = "bot", Capabilities = [] }, () => connection);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = client.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected);
        Assert.Equal(new[] { "PASS oauth:abc", "NICK bot" }, connection.Sent);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Theory]
    [InlineData("Login authentication failed")]
    [InlineData("Improperly formatted auth")]
    public async Task LoginFailureThrowsWithoutRetryingOrLeakingTheToken(string notice)
    {
        var connections = 0;
        var connection = new FakeIrcConnection { Respond = line => line.StartsWith("NICK ", StringComparison.Ordinal) ? [":tmi.twitch.tv NOTICE * :" + notice] : [] };
        var tokens = new RotatingTokenProvider(UserToken(Token), null);
        var logger = new CapturingLogger();
        var client = new TwitchIrcClient(tokens, new TwitchIrcOptions { Login = "bot" }, () => { connections++; return connection; }, logger: logger);
        var error = await Assert.ThrowsAsync<TwitchIrcAuthenticationException>(() => client.RunAsync((_, _) => Task.CompletedTask));
        Assert.Equal(notice, error.ServerNotice);
        Assert.DoesNotContain(Token, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, error.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, connections);
        Assert.Equal(notice.StartsWith("Login", StringComparison.Ordinal) ? 1 : 0, tokens.Refreshes);
        Assert.True(connection.Disposed);
        Assert.DoesNotContain(logger.Entries, entry => entry.Contains(Token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoginFailureRetriesOnceWithRefreshedToken()
    {
        var rejected = new FakeIrcConnection { Respond = line => line.StartsWith("NICK ", StringComparison.Ordinal) ? [":tmi.twitch.tv NOTICE * :Login authentication failed"] : [] };
        var accepted = new FakeIrcConnection();
        var queue = new ConcurrentQueue<FakeIrcConnection>([rejected, accepted]);
        var tokens = new RotatingTokenProvider(UserToken("expired"), UserToken("fresh"));
        var client = new TwitchIrcClient(tokens, new TwitchIrcOptions { Login = "bot" }, () => Next(queue));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = client.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected);
        Assert.Contains("PASS oauth:expired", rejected.Sent);
        Assert.Contains("PASS oauth:fresh", accepted.Sent);
        Assert.Equal(1, tokens.Refreshes);
        Assert.True(rejected.Disposed);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task ScopePreflightRejectsTokensBeforeConnecting()
    {
        var created = 0;
        IIrcConnection Factory() { created++; return new FakeIrcConnection(); }
        var noRead = new TwitchIrcClient(Tokens(TwitchScopes.ChatEdit), new TwitchIrcOptions { Login = "bot" }, Factory);
        var missing = await Assert.ThrowsAsync<TwitchAuthorizationException>(() => noRead.RunAsync((_, _) => Task.CompletedTask));
        Assert.Equal(new[] { TwitchScopes.ChatRead }, missing.MissingScopes);
        var app = new TwitchIrcClient(new StaticAccessTokenProvider(new AccessToken(Token, kind: TwitchTokenKind.App)), new TwitchIrcOptions { Login = "bot" }, Factory);
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => app.RunAsync((_, _) => Task.CompletedTask));
        Assert.Equal(0, created);
        var invalid = new TwitchIrcClient(new StaticAccessTokenProvider(new AccessToken("two words", scopes: [TwitchScopes.ChatRead])), new TwitchIrcOptions { Login = "bot" }, Factory);
        var error = await Assert.ThrowsAsync<TwitchIrcAuthenticationException>(() => invalid.RunAsync((_, _) => Task.CompletedTask));
        Assert.DoesNotContain("two words", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownScopesAreDeferredToTwitchButKnownScopesNeedChatEditToSend()
    {
        var unknown = new FakeIrcConnection();
        var unknownClient = new TwitchIrcClient(new StaticAccessTokenProvider(new AccessToken(Token, kind: TwitchTokenKind.User)), new TwitchIrcOptions { Login = "bot" }, () => unknown);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var unknownRun = unknownClient.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => unknownClient.IsConnected);
        await unknownClient.SendMessageAsync("chan", "allowed");
        Assert.Contains("PRIVMSG #chan :allowed", unknown.Sent);

        var readOnly = new FakeIrcConnection();
        var readOnlyClient = new TwitchIrcClient(Tokens(TwitchScopes.ChatRead), new TwitchIrcOptions { Login = "bot" }, () => readOnly);
        var readOnlyRun = readOnlyClient.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => readOnlyClient.IsConnected);
        var error = await Assert.ThrowsAsync<TwitchAuthorizationException>(() => readOnlyClient.SendMessageAsync("chan", "denied"));
        Assert.Equal(new[] { TwitchScopes.ChatEdit }, error.MissingScopes);
        Assert.DoesNotContain(readOnly.Sent, line => line.StartsWith("PRIVMSG", StringComparison.Ordinal));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unknownRun);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readOnlyRun);
    }

    [Fact]
    public async Task AnswersPingWithPongWithoutDispatchingItAndSkipsMalformedLines()
    {
        var connection = new FakeIrcConnection();
        var client = new TwitchIrcClient(Tokens(), new TwitchIrcOptions { Login = "bot" }, () => connection);
        var received = new ConcurrentQueue<IrcMessage>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = client.RunAsync((message, _) => { received.Enqueue(message); return Task.CompletedTask; }, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected);
        connection.Enqueue("PING :tmi.twitch.tv");
        connection.Enqueue("@broken");
        connection.Enqueue("PR1VMSG #chan :bad");
        connection.Enqueue(":tmi.twitch.tv PONG tmi.twitch.tv :tmi.twitch.tv");
        connection.Enqueue(":viewer!viewer@viewer.tmi.twitch.tv PRIVMSG #chan :after");
        await ManualTimeProvider.WaitUntilAsync(() => received.Any(m => m.Command == "PRIVMSG"));
        Assert.Contains("PONG :tmi.twitch.tv", connection.Sent);
        Assert.Equal(new[] { "CAP", "001", "PRIVMSG" }, received.Select(m => m.Command));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task HandshakeAnswersPingAndRejectsCapabilityNak()
    {
        var pinged = new FakeIrcConnection { Respond = line => line.StartsWith("NICK ", StringComparison.Ordinal) ? [Ping, ":tmi.twitch.tv 001 bot :Welcome, GLHF!"] : [] };
        var client = new TwitchIrcClient(Tokens(), new TwitchIrcOptions { Login = "bot" }, () => pinged);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = client.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected);
        Assert.Equal("PONG :tmi.twitch.tv", pinged.Sent[^1]);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        var nak = new FakeIrcConnection { Respond = line => line.StartsWith("CAP ", StringComparison.Ordinal) ? [":tmi.twitch.tv CAP * NAK :twitch.tv/unknown"] : [] };
        var nakClient = new TwitchIrcClient(Tokens(), new TwitchIrcOptions { Login = "bot", Capabilities = ["twitch.tv/unknown"] }, () => nak);
        var error = await Assert.ThrowsAsync<TwitchIrcException>(() => nakClient.RunAsync((_, _) => Task.CompletedTask));
        Assert.Contains("twitch.tv/unknown", error.Message, StringComparison.Ordinal);
        Assert.True(nak.Disposed);
    }

    [Fact]
    public async Task ReconnectCommandReconnectsImmediatelyAndRejoinsChannels()
    {
        var time = new IrcTestTimeProvider();
        var first = new FakeIrcConnection();
        var second = new FakeIrcConnection();
        var queue = new ConcurrentQueue<FakeIrcConnection>([first, second]);
        var commands = new ConcurrentQueue<string>();
        var client = new TwitchIrcClient(Tokens(), new TwitchIrcOptions { Login = "bot" }, () => Next(queue), time);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = client.RunAsync((message, _) => { commands.Enqueue(message.Command); return Task.CompletedTask; }, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected);
        await client.JoinAsync("alpha");
        await client.JoinAsync("#Beta");
        await client.JoinAsync("alpha");
        Assert.Equal(new[] { "JOIN #alpha", "JOIN #beta" }, first.Sent.Where(IsJoin));
        await client.PartAsync("beta");
        await client.JoinAsync("gamma");
        Assert.Contains("PART #beta", first.Sent);

        // Time never advances, so any backoff delay would stall this test.
        first.Enqueue(":tmi.twitch.tv RECONNECT");
        await second.WaitForSentAsync(line => second.Sent.Count(IsJoin) == 2);
        Assert.True(first.Disposed);
        Assert.Equal(new[] { "JOIN #alpha", "JOIN #gamma" }, second.Sent.Where(IsJoin).Order());
        Assert.Equal(new[] { CapReq, "PASS oauth:" + Token, "NICK bot" }, second.Sent.Take(3));
        Assert.Contains("RECONNECT", commands);
        Assert.Equal(new[] { "alpha", "gamma" }, client.JoinedChannels.Order());
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task DroppedConnectionsReconnectWithExponentialBackoff()
    {
        var time = new IrcTestTimeProvider();
        var first = new FakeIrcConnection();
        var failing = new FakeIrcConnection { ConnectFailure = new WebSocketException("refused") };
        var third = new FakeIrcConnection();
        var queue = new ConcurrentQueue<FakeIrcConnection>([first, failing, third]);
        var created = 0;
        var client = new TwitchIrcClient(Tokens(), new TwitchIrcOptions { Login = "bot" }, () => { Interlocked.Increment(ref created); return Next(queue); }, time);
        await client.JoinAsync("chan");
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = client.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected && time.HasPendingDelays(60));

        first.CloseFromServer();
        await ManualTimeProvider.WaitUntilAsync(() => first.Disposed && time.HasPendingDelays(1));
        Assert.False(client.IsConnected);
        time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal(1, Volatile.Read(ref created));
        time.Advance(TimeSpan.FromMilliseconds(1));
        await ManualTimeProvider.WaitUntilAsync(() => failing.Disposed && Volatile.Read(ref created) == 2 && time.HasPendingDelays(2));

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, Volatile.Read(ref created));
        time.Advance(TimeSpan.FromSeconds(1));
        await third.WaitForSentAsync(line => line == "JOIN #chan");
        Assert.True(client.IsConnected);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task BackoffIsBoundedByMaxReconnectDelay()
    {
        var time = new IrcTestTimeProvider();
        var created = new ConcurrentQueue<FakeIrcConnection>();
        var client = new TwitchIrcClient(Tokens(), new TwitchIrcOptions { Login = "bot", MaxReconnectDelay = TimeSpan.FromSeconds(5) }, () =>
        {
            var connection = new FakeIrcConnection { ConnectFailure = new IOException("unreachable") };
            created.Enqueue(connection);
            return connection;
        }, time);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = client.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        var expectedDelays = new[] { 1, 2, 4, 5, 5, 5 };
        for (var attempt = 0; attempt < expectedDelays.Length; attempt++)
        {
            var (count, delay) = (attempt + 1, expectedDelays[attempt]);
            await ManualTimeProvider.WaitUntilAsync(() => created.Count == count && created.Last().Disposed && time.HasPendingDelays(delay));
            time.Advance(TimeSpan.FromSeconds(delay));
        }
        await ManualTimeProvider.WaitUntilAsync(() => created.Count == expectedDelays.Length + 1);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task KeepaliveSendsPingWhenIdleAndReconnectsWithoutReply()
    {
        var time = new IrcTestTimeProvider();
        var first = new FakeIrcConnection();
        var second = new FakeIrcConnection();
        var queue = new ConcurrentQueue<FakeIrcConnection>([first, second]);
        var options = new TwitchIrcOptions { Login = "bot", KeepaliveInterval = TimeSpan.FromSeconds(60), KeepaliveTimeout = TimeSpan.FromSeconds(10) };
        var client = new TwitchIrcClient(Tokens(), options, () => Next(queue), time);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = new ConcurrentQueue<string>();
        var run = client.RunAsync((message, _) => { received.Enqueue(message.Command); return Task.CompletedTask; }, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected && time.HasPendingDelays(60));
        time.Advance(TimeSpan.FromSeconds(59));
        Assert.DoesNotContain(Ping, first.Sent);
        time.Advance(TimeSpan.FromSeconds(1));
        await ManualTimeProvider.WaitUntilAsync(() => first.Sent.Contains(Ping) && time.HasPendingDelays(10));

        // Any data counts as a reply and restarts the idle interval.
        first.Enqueue(":tmi.twitch.tv PONG tmi.twitch.tv :tmi.twitch.tv");
        first.Enqueue(":viewer!viewer@viewer.tmi.twitch.tv PRIVMSG #chan :still here");
        await ManualTimeProvider.WaitUntilAsync(() => received.Contains("PRIVMSG") && time.HasPendingDelays(60));
        time.Advance(TimeSpan.FromSeconds(59));
        Assert.Single(first.Sent, line => line == Ping);
        time.Advance(TimeSpan.FromSeconds(1));
        await ManualTimeProvider.WaitUntilAsync(() => first.Sent.Count(line => line == Ping) == 2 && time.HasPendingDelays(10));
        time.Advance(TimeSpan.FromSeconds(10));
        await ManualTimeProvider.WaitUntilAsync(() => first.Disposed && time.HasPendingDelays(1));
        Assert.False(client.IsConnected);
        time.Advance(TimeSpan.FromSeconds(1));
        await second.WaitForSentAsync(line => line == "NICK bot");
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task MessagesAreRateLimitedWithCancellableWaits()
    {
        var time = new IrcTestTimeProvider();
        var connection = new FakeIrcConnection();
        var options = new TwitchIrcOptions { Login = "bot", MessageRateLimit = new IrcRateLimit(2, TimeSpan.FromSeconds(30)), KeepaliveInterval = Timeout.InfiniteTimeSpan };
        var client = new TwitchIrcClient(Tokens(), options, () => connection, time);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = client.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected);
        await client.SendMessageAsync("chan", "one");
        time.Advance(TimeSpan.FromSeconds(10));
        await client.SendMessageAsync("#Chan", "two", "parent-id");
        var third = client.SendMessageAsync("chan", "three");
        Assert.False(third.IsCompleted);
        using var cancel = new CancellationTokenSource();
        var cancelled = client.SendMessageAsync("chan", "never", cancellationToken: cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        time.Advance(TimeSpan.FromSeconds(19));
        Assert.False(third.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));
        await third;
        Assert.Equal(new[] { "PRIVMSG #chan :one", "@reply-parent-msg-id=parent-id PRIVMSG #chan :two", "PRIVMSG #chan :three" },
            connection.Sent.Where(line => line.Contains("PRIVMSG", StringComparison.Ordinal)));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task JoinWaitsForJoinLimitAndCancellationLeavesChannelUnjoined()
    {
        var time = new IrcTestTimeProvider();
        var connection = new FakeIrcConnection();
        var options = new TwitchIrcOptions { Login = "bot", JoinRateLimit = new IrcRateLimit(1, TimeSpan.FromSeconds(10)), KeepaliveInterval = Timeout.InfiniteTimeSpan };
        var client = new TwitchIrcClient(Tokens(), options, () => connection, time);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = client.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected);
        await client.JoinAsync("first");
        using var cancel = new CancellationTokenSource();
        var cancelled = client.JoinAsync("second", cancel.Token);
        Assert.False(cancelled.IsCompleted);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(new[] { "first" }, client.JoinedChannels);
        var third = client.JoinAsync("third");
        Assert.False(third.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(10));
        await third;
        Assert.Equal(new[] { "JOIN #first", "JOIN #third" }, connection.Sent.Where(IsJoin));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task SendValidatesTextLengthLineInjectionAndConnection()
    {
        var client = new TwitchIrcClient(Tokens(), new TwitchIrcOptions { Login = "bot" }, () => new FakeIrcConnection());
        Assert.Throws<ArgumentException>(() => { _ = client.SendMessageAsync("chan", new string('a', 501)); });
        Assert.Throws<ArgumentException>(() => { _ = client.SendMessageAsync("chan", "hi\r\nJOIN #evil"); });
        Assert.Throws<ArgumentException>(() => { _ = client.SendMessageAsync("chan", "hi\nthere"); });
        Assert.Throws<ArgumentException>(() => { _ = client.SendMessageAsync("chan", "   "); });
        Assert.Throws<ArgumentNullException>(() => { _ = client.SendMessageAsync("chan", null!); });
        Assert.Throws<ArgumentException>(() => { _ = client.SendMessageAsync("bad channel", "hi"); });
        Assert.Throws<ArgumentException>(() => { _ = client.SendMessageAsync("chan", "hi", "id\r\nQUIT"); });
        Assert.Throws<ArgumentException>(() => { _ = client.SendMessageAsync("chan", "hi", " "); });
        Assert.Throws<ArgumentException>(() => { _ = client.SendRawAsync(new IrcMessage("PASS", ["oauth:x"])); });
        Assert.Throws<ArgumentException>(() => { _ = client.SendRawAsync(new IrcMessage("NICK", ["other"])); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendMessageAsync("chan", string.Concat(Enumerable.Repeat("\U0001F600", 500))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendRawAsync(new IrcMessage("JOIN", ["#chan"])));
    }

    [Fact]
    public async Task SendRawAppliesLimitsAndPreservesClientNonceTags()
    {
        var time = new IrcTestTimeProvider();
        var connection = new FakeIrcConnection();
        var options = new TwitchIrcOptions { Login = "bot", JoinRateLimit = new IrcRateLimit(2, TimeSpan.FromSeconds(10)), KeepaliveInterval = Timeout.InfiniteTimeSpan };
        var client = new TwitchIrcClient(Tokens(), options, () => connection, time);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = client.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected);
        await client.SendRawAsync(new IrcMessage("PRIVMSG", ["#chan", "hi"], [new("client-nonce", "nonce-1")], lastParameterIsTrailing: true));
        Assert.Contains("@client-nonce=nonce-1 PRIVMSG #chan :hi", connection.Sent);
        var join = client.SendRawAsync(new IrcMessage("JOIN", ["#a,#b,#c"]));
        Assert.False(join.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(10));
        await join;
        Assert.Contains("JOIN #a,#b,#c", connection.Sent);
        Assert.Empty(client.JoinedChannels);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task SendFailureOnBrokenConnectionIsReported()
    {
        var connection = new FakeIrcConnection();
        var client = new TwitchIrcClient(Tokens(), new TwitchIrcOptions { Login = "bot" }, () => connection);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = client.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected);
        connection.SendFailure = new WebSocketException("broken pipe");
        var error = await Assert.ThrowsAsync<TwitchIrcException>(() => client.SendMessageAsync("chan", "lost"));
        Assert.IsType<WebSocketException>(error.InnerException);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task CallbackExceptionsStopTheClientAndPropagate()
    {
        foreach (var failure in new Exception[] { new InvalidOperationException("consumer failure"), new WebSocketException("consumer socket") })
        {
            var connection = new FakeIrcConnection();
            var client = new TwitchIrcClient(Tokens(), new TwitchIrcOptions { Login = "bot" }, () => connection);
            var run = client.RunAsync((message, _) => message.Command == "PRIVMSG" ? throw failure : Task.CompletedTask);
            await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected);
            connection.Enqueue(":viewer!viewer@viewer.tmi.twitch.tv PRIVMSG #chan :boom");
            var error = await Assert.ThrowsAnyAsync<Exception>(() => run);
            Assert.Same(failure, error);
            Assert.True(connection.Disposed);
            Assert.False(client.IsConnected);
        }
    }

    [Fact]
    public async Task RunAsyncRejectsConcurrentRunsAndStopsDuringBackoff()
    {
        var time = new IrcTestTimeProvider();
        var client = new TwitchIrcClient(Tokens(), new TwitchIrcOptions { Login = "bot" }, () => new FakeIrcConnection { ConnectFailure = new WebSocketException("down") }, time);
        using var stop = new CancellationTokenSource();
        var run = client.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RunAsync((_, _) => Task.CompletedTask));
        await ManualTimeProvider.WaitUntilAsync(() => time.HasPendingDelays(1));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.RunAsync((_, _) => Task.CompletedTask, stop.Token));
    }

    [Fact]
    public void OptionsValidateEndpointLoginCapabilitiesAndTiming()
    {
        var tokens = Tokens();
        Assert.Throws<ArgumentException>(() => new TwitchIrcClient(tokens, new TwitchIrcOptions { Login = "bot", Endpoint = new Uri("ws://irc-ws.chat.twitch.tv/") }));
        Assert.Throws<ArgumentException>(() => new TwitchIrcClient(tokens, new TwitchIrcOptions { Login = "bot", Endpoint = new Uri("irc://irc.chat.twitch.tv:6667") }));
        Assert.Throws<ArgumentException>(() => new TwitchIrcClient(tokens, new TwitchIrcOptions { Login = "bot", Endpoint = new Uri("https://irc-ws.chat.twitch.tv/") }));
        Assert.Throws<ArgumentException>(() => new TwitchIrcClient(tokens, new TwitchIrcOptions { Login = "bot", Endpoint = new Uri("wss://user:pass@irc-ws.chat.twitch.tv/") }));
        Assert.Throws<ArgumentException>(() => new TwitchIrcClient(tokens, new TwitchIrcOptions { Login = "bad login" }));
        Assert.Throws<ArgumentException>(() => new TwitchIrcClient(tokens, new TwitchIrcOptions { Login = "" }));
        Assert.Throws<ArgumentException>(() => new TwitchIrcClient(tokens, new TwitchIrcOptions { Login = "bot", Capabilities = ["twitch.tv/tags twitch.tv/commands"] }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TwitchIrcClient(tokens, new TwitchIrcOptions { Login = "bot", KeepaliveTimeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TwitchIrcClient(tokens, new TwitchIrcOptions { Login = "bot", MaxReconnectDelay = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IrcRateLimit(0, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IrcRateLimit(1, TimeSpan.Zero));
        _ = new TwitchIrcClient(tokens, new TwitchIrcOptions { Login = "bot", Endpoint = new Uri("ws://127.0.0.1:1234/") });
        _ = new TwitchIrcClient(tokens, new TwitchIrcOptions { Login = "bot", Endpoint = TwitchIrcOptions.DefaultTcpEndpoint });
        Assert.Equal((100, TimeSpan.FromSeconds(30)), (IrcRateLimit.ModeratorMessages.PermitLimit, IrcRateLimit.ModeratorMessages.Window));
    }

    [Fact]
    public async Task SlidingWindowLimiterWaitsForOldestPermitAndHonorsCancellation()
    {
        var time = new IrcTestTimeProvider();
        var limiter = new IrcSlidingWindowRateLimiter(new IrcRateLimit(3, TimeSpan.FromSeconds(10)), time);
        await limiter.AcquireAsync();
        time.Advance(TimeSpan.FromSeconds(5));
        await limiter.AcquireAsync();
        Assert.True(limiter.TryAcquire());
        Assert.False(limiter.TryAcquire());
        Assert.Equal(0, limiter.AvailablePermits);

        using var cancel = new CancellationTokenSource();
        var cancelled = limiter.AcquireAsync(cancel.Token);
        Assert.False(cancelled.IsCompleted);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        var fourth = limiter.AcquireAsync();
        var fifth = limiter.AcquireAsync();
        time.Advance(TimeSpan.FromSeconds(4));
        Assert.False(fourth.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));
        await fourth;
        await ManualTimeProvider.WaitUntilAsync(() => time.HasPendingDelays(5));
        Assert.False(fifth.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(5));
        await fifth;
        Assert.Equal(1, limiter.AvailablePermits);
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(3, limiter.AvailablePermits);
    }

    private static bool IsJoin(string line) => line.StartsWith("JOIN ", StringComparison.Ordinal);

    private static AccessToken UserToken(string value, params string[] scopes)
        => new(value, scopes: scopes.Length == 0 ? [TwitchScopes.ChatRead, TwitchScopes.ChatEdit] : scopes, kind: TwitchTokenKind.User);

    private static StaticAccessTokenProvider Tokens(params string[] scopes) => new(UserToken(Token, scopes));

    private static FakeIrcConnection Next(ConcurrentQueue<FakeIrcConnection> queue)
        => queue.TryDequeue(out var connection) ? connection : throw new InvalidOperationException("No more fake connections.");

    private sealed class RotatingTokenProvider(AccessToken first, AccessToken? second) : IAccessTokenProvider
    {
        private AccessToken _current = first;
        public int Refreshes;
        public ValueTask<AccessToken> GetTokenAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_current);

        public ValueTask<AccessToken> RefreshTokenAsync(AccessToken rejectedToken, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Refreshes);
            if (second is not null) _current = second;
            return ValueTask.FromResult(_current);
        }
    }

    private sealed class CapturingLogger : ILogger<TwitchIrcClient>
    {
        private readonly ConcurrentQueue<string> _entries = new();
        public IReadOnlyCollection<string> Entries => _entries.ToArray();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _entries.Enqueue(formatter(state, exception) + " " + exception);
    }
}

/// <summary>A scripted in-memory IRC transport. By default it answers NICK with CAP ACK and the 001 welcome.</summary>
internal sealed class FakeIrcConnection : IIrcConnection
{
    private readonly Channel<string?> _incoming = Channel.CreateUnbounded<string?>();
    private readonly ConcurrentQueue<string> _sent = new();
    private volatile bool _disposed;
    private int _pending;

    public Func<string, IEnumerable<string>> Respond { get; init; } = line => line.StartsWith("NICK ", StringComparison.Ordinal)
        ? [":tmi.twitch.tv CAP * ACK :twitch.tv/tags twitch.tv/commands twitch.tv/membership", ":tmi.twitch.tv 001 bot :Welcome, GLHF!"]
        : [];
    public Exception? ConnectFailure { get; init; }
    public Exception? SendFailure { get; set; }
    public Uri? Uri { get; private set; }
    public bool Disposed => _disposed;
    public int Pending => Volatile.Read(ref _pending);
    public IReadOnlyList<string> Sent => _sent.ToArray();

    public void Enqueue(string line)
    {
        Interlocked.Increment(ref _pending);
        _incoming.Writer.TryWrite(line);
    }

    public void CloseFromServer()
    {
        Interlocked.Increment(ref _pending);
        _incoming.Writer.TryWrite(null);
    }

    public Task WaitForSentAsync(Func<string, bool> predicate) => ManualTimeProvider.WaitUntilAsync(() => Sent.Any(predicate));

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        Uri = uri;
        return ConnectFailure is null ? Task.CompletedTask : Task.FromException(ConnectFailure);
    }

    public async Task<string?> ReceiveLineAsync(CancellationToken cancellationToken)
    {
        try
        {
            var line = await _incoming.Reader.ReadAsync(cancellationToken);
            Interlocked.Decrement(ref _pending);
            return line;
        }
        catch (ChannelClosedException)
        {
            throw new WebSocketException("The fake connection was disposed.");
        }
    }

    public Task SendLineAsync(string line, CancellationToken cancellationToken)
    {
        if (_disposed) return Task.FromException(new WebSocketException("The fake connection was disposed."));
        if (SendFailure is { } failure) return Task.FromException(failure);
        if (line.AsSpan().IndexOfAny('\r', '\n') >= 0) throw new ArgumentException("Line injection.", nameof(line));
        _sent.Enqueue(line);
        foreach (var response in Respond(line)) Enqueue(response);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _incoming.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

/// <summary>A manual clock that exposes the remaining delay of every active timer, so tests can wait for an exact timer before advancing.</summary>
internal sealed class IrcTestTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<Timer> _timers = [];
    private DateTimeOffset _now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() { lock (_gate) return _now; }
    public override long GetTimestamp() => GetUtcNow().UtcTicks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>True when exactly these timers (remaining seconds, any order) are active.</summary>
    public bool HasPendingDelays(params double[] seconds)
    {
        lock (_gate) return _timers.Select(t => t.Due - _now).Order().SequenceEqual(seconds.Select(TimeSpan.FromSeconds).Order());
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new Timer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan duration)
    {
        List<Timer> due;
        lock (_gate)
        {
            _now += duration;
            due = _timers.Where(t => t.Due <= _now).OrderBy(t => t.Due).ToList();
            foreach (var timer in due)
            {
                if (timer.Period == Timeout.InfiniteTimeSpan) _timers.Remove(timer);
                else timer.Due = _now + timer.Period;
            }
        }
        foreach (var timer in due) timer.Fire();
    }

    private sealed class Timer(IrcTestTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private bool _disposed;
        public DateTimeOffset Due { get; set; }
        public TimeSpan Period { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                if (_disposed) return false;
                owner._timers.Remove(this);
                Period = period;
                if (dueTime != Timeout.InfiniteTimeSpan)
                {
                    Due = owner._now + dueTime;
                    owner._timers.Add(this);
                }
                return true;
            }
        }

        public void Fire()
        {
            bool disposed;
            lock (owner._gate) disposed = _disposed;
            if (!disposed) callback(state);
        }

        public void Dispose()
        {
            lock (owner._gate)
            {
                _disposed = true;
                owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}