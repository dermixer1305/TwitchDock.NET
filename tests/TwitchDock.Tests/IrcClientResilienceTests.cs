using System.Collections.Concurrent;
using System.Security.Authentication;
using TwitchDock.Chat.Irc;
using static TwitchDock.Tests.IrcTestSupport;

namespace TwitchDock.Tests;

/// <summary>Reconnect backoff, send timeouts, teardown and join races of <see cref="TwitchIrcClient"/>.</summary>
public sealed class IrcClientResilienceTests
{
    private const string Reconnect = ":tmi.twitch.tv RECONNECT";
    private const string Chat = ":viewer!viewer@viewer.tmi.twitch.tv PRIVMSG #chan :after";

    [Fact]
    public async Task AcceptThenDropConnectionsBackOffUntilAConnectionStaysUp()
    {
        var time = new IrcTestTimeProvider();
        var created = new ConcurrentQueue<FakeIrcConnection>();
        var client = new TwitchIrcClient(Tokens(), Quiet(), () => Track(created, new FakeIrcConnection()), time);
        using var stop = new CancellationTokenSource(TestTimeout);
        var run = client.RunAsync((_, _) => Task.CompletedTask, stop.Token);

        // Each connection logs in and is dropped at once; the backoff keeps growing instead of restarting at one second.
        var delays = new[] { 1, 2, 4, 8 };
        for (var i = 0; i < delays.Length; i++)
        {
            var (count, delay) = (i + 1, delays[i]);
            await ManualTimeProvider.WaitUntilAsync(() => created.Count == count && client.IsConnected);
            var dropped = created.Last();
            dropped.CloseFromServer();
            await ManualTimeProvider.WaitUntilAsync(() => dropped.Disposed && !client.IsConnected && time.HasPendingDelays(delay));
            time.Advance(TimeSpan.FromSeconds(delay));
        }

        // A connection that stays up for 30 seconds resets the backoff.
        await ManualTimeProvider.WaitUntilAsync(() => created.Count == delays.Length + 1 && client.IsConnected);
        time.Advance(TimeSpan.FromSeconds(30));
        var stable = created.Last();
        stable.CloseFromServer();
        await ManualTimeProvider.WaitUntilAsync(() => stable.Disposed && time.HasPendingDelays(1));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task ReconnectRightAfterLoginWaitsForTheBackoff()
    {
        var time = new IrcTestTimeProvider();
        var first = new FakeIrcConnection();
        var second = new FakeIrcConnection();
        var third = new FakeIrcConnection();
        var queue = new ConcurrentQueue<FakeIrcConnection>([first, second, third]);
        var client = new TwitchIrcClient(Tokens(), Quiet(), () => Next(queue), time);
        using var stop = new CancellationTokenSource(TestTimeout);
        var run = client.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected);

        first.Enqueue(Reconnect);
        await ManualTimeProvider.WaitUntilAsync(() => first.Disposed && time.HasPendingDelays(1));
        Assert.Null(second.Uri);
        time.Advance(TimeSpan.FromSeconds(1));
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected && second.Uri is not null);

        // A second rapid RECONNECT keeps growing the delay.
        second.Enqueue(Reconnect);
        await ManualTimeProvider.WaitUntilAsync(() => second.Disposed && time.HasPendingDelays(2));
        Assert.Null(third.Uri);
        time.Advance(TimeSpan.FromSeconds(2));
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected && third.Uri is not null);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task CallerCancellationDoesNotInterruptAWriteInProgress()
    {
        var time = new IrcTestTimeProvider();
        var connection = new FakeIrcConnection();
        var client = new TwitchIrcClient(Tokens(), Quiet(), () => connection, time);
        using var stop = new CancellationTokenSource(TestTimeout);
        var run = client.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.SendGate = gate;
        using var cancel = new CancellationTokenSource();
        var first = client.SendMessageAsync("chan", "first", cancellationToken: cancel.Token);
        await ManualTimeProvider.WaitUntilAsync(() => connection.Attempted.Contains("PRIVMSG #chan :first"));
        var second = client.SendMessageAsync("chan", "second");
        cancel.Cancel();

        // The write runs on the connection's token, so the caller's cancellation cannot cut a line in half.
        Assert.False(connection.LastSendToken.IsCancellationRequested);
        Assert.False(first.IsCompleted);
        connection.SendGate = null;
        gate.SetResult();
        await first.WaitAsync(TestTimeout);
        await second.WaitAsync(TestTimeout);
        Assert.Equal(new[] { "PRIVMSG #chan :first", "PRIVMSG #chan :second" }, connection.Sent.Where(IsPrivmsg));
        Assert.True(client.IsConnected);
        Assert.False(connection.Disposed);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task StuckSendTimesOutAndReplacesTheConnection()
    {
        var time = new IrcTestTimeProvider();
        var first = new FakeIrcConnection { GateIgnoresCancellation = true };
        var second = new FakeIrcConnection();
        var queue = new ConcurrentQueue<FakeIrcConnection>([first, second]);
        var client = new TwitchIrcClient(Tokens(), Quiet(), () => Next(queue), time);
        using var stop = new CancellationTokenSource(TestTimeout);
        var run = client.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected);

        first.SendGate = new TaskCompletionSource();
        var stuck = client.SendMessageAsync("chan", "stuck");
        await ManualTimeProvider.WaitUntilAsync(() => first.Attempted.Contains("PRIVMSG #chan :stuck") && time.HasPendingDelays(10));
        time.Advance(TimeSpan.FromSeconds(9));
        Assert.False(stuck.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));
        var error = await Assert.ThrowsAsync<TwitchIrcException>(() => stuck.WaitAsync(TestTimeout));
        Assert.IsType<TimeoutException>(error.InnerException);

        await ManualTimeProvider.WaitUntilAsync(() => first.Disposed && !client.IsConnected && time.HasPendingDelays(1));
        time.Advance(TimeSpan.FromSeconds(1));
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected);
        await client.SendMessageAsync("chan", "after").WaitAsync(TestTimeout);
        Assert.Contains("PRIVMSG #chan :after", second.Sent);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task StuckKeepalivePingStillDetectsTheDeadConnection()
    {
        var time = new IrcTestTimeProvider();
        var first = new FakeIrcConnection { GateIgnoresCancellation = true };
        var second = new FakeIrcConnection();
        var queue = new ConcurrentQueue<FakeIrcConnection>([first, second]);
        var client = new TwitchIrcClient(Tokens(), Keepalive(), () => Next(queue), time);
        using var stop = new CancellationTokenSource(TestTimeout);
        var run = client.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected && time.HasPendingDelays(60));

        first.SendGate = new TaskCompletionSource();
        time.Advance(TimeSpan.FromSeconds(60));
        await ManualTimeProvider.WaitUntilAsync(() => first.Attempted.Contains(Ping) && time.HasPendingDelays(10));
        time.Advance(TimeSpan.FromSeconds(10));
        await ManualTimeProvider.WaitUntilAsync(() => first.Disposed && !client.IsConnected && time.HasPendingDelays(1));
        time.Advance(TimeSpan.FromSeconds(1));
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected && second.Sent.Contains("NICK bot"));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task StuckSendDoesNotBlockKeepaliveDetection()
    {
        var time = new IrcTestTimeProvider();
        var first = new FakeIrcConnection { GateIgnoresCancellation = true };
        var second = new FakeIrcConnection();
        var queue = new ConcurrentQueue<FakeIrcConnection>([first, second]);
        var client = new TwitchIrcClient(Tokens(), Keepalive(), () => Next(queue), time);
        using var stop = new CancellationTokenSource(TestTimeout);
        var run = client.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected && time.HasPendingDelays(60));

        // A user send stalls while holding the send lock; the keepalive PING that follows has to wait for it.
        first.SendGate = new TaskCompletionSource();
        time.Advance(TimeSpan.FromSeconds(55));
        var stuck = client.SendMessageAsync("chan", "stuck");
        await ManualTimeProvider.WaitUntilAsync(() => first.Attempted.Contains("PRIVMSG #chan :stuck") && time.HasPendingDelays(5, 10));
        time.Advance(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<TwitchIrcException>(() => stuck.WaitAsync(TestTimeout));
        await ManualTimeProvider.WaitUntilAsync(() => first.Disposed && !client.IsConnected && time.HasPendingDelays(1));
        Assert.DoesNotContain(Ping, first.Attempted);
        time.Advance(TimeSpan.FromSeconds(1));
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected && second.Sent.Contains("NICK bot"));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task TransientTlsHandshakeFailuresAreRetriedButCertificateFailuresStopTheClient()
    {
        var time = new IrcTestTimeProvider();
        var transient = new FakeIrcConnection { ConnectFailure = new AuthenticationException("Authentication failed, see inner exception.", new IOException("Connection reset.")) };
        var accepted = new FakeIrcConnection();
        var queue = new ConcurrentQueue<FakeIrcConnection>([transient, accepted]);
        var client = new TwitchIrcClient(Tokens(), Quiet(), () => Next(queue), time);
        using var stop = new CancellationTokenSource(TestTimeout);
        var run = client.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => transient.Disposed && time.HasPendingDelays(1));
        time.Advance(TimeSpan.FromSeconds(1));
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected && accepted.Sent.Contains("NICK bot"));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TestTimeout));

        var certificate = new AuthenticationException("The remote certificate is invalid according to the validation procedure.");
        var rejected = new TwitchIrcClient(Tokens(), Quiet(), () => new FakeIrcConnection { ConnectFailure = certificate }, time);
        var error = await Assert.ThrowsAsync<AuthenticationException>(() => rejected.RunAsync((_, _) => Task.CompletedTask).WaitAsync(TestTimeout));
        Assert.Same(certificate, error);
    }

    [Fact]
    public async Task StoppingReportsCancellationEvenWhenTheTransportReportsAnIOException()
    {
        // The server never answers the login, so RunAsync is stopped while the handshake waits for a line.
        var connection = new FakeIrcConnection { Respond = _ => [], CancellationAsIOException = true };
        var client = new TwitchIrcClient(Tokens(), Quiet(), () => connection);
        using var stop = new CancellationTokenSource();
        var run = client.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        await connection.WaitForSentAsync(line => line == "NICK bot");
        stop.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TestTimeout));
        Assert.Equal(stop.Token, error.CancellationToken);
        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task OversizedPingOriginGetsATruncatedPongAndTheClientKeepsRunning()
    {
        var connection = new FakeIrcConnection();
        var client = new TwitchIrcClient(Tokens(), Quiet(), () => connection);
        var received = new ConcurrentQueue<string>();
        using var stop = new CancellationTokenSource(TestTimeout);
        var run = client.RunAsync((message, _) => { received.Enqueue(message.Command); return Task.CompletedTask; }, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected);

        // The longest line the parser accepts, with the origin as a middle parameter: echoing it as a trailing one would add a ':'.
        var origin = new string('o', IrcMessage.MaxLineLength - "PING ".Length);
        connection.Enqueue("PING " + origin);
        connection.Enqueue(Chat);
        await ManualTimeProvider.WaitUntilAsync(() => received.Contains("PRIVMSG"));
        var pong = Assert.Single(connection.Sent, line => line.StartsWith("PONG", StringComparison.Ordinal));
        Assert.Equal("PONG :" + origin[..256], pong);
        Assert.True(client.IsConnected);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task FailingTeardownStepsDoNotReplaceTheOriginalError()
    {
        var failure = new InvalidOperationException("consumer failure");
        var connection = new FakeIrcConnection { DisposeFailure = new IOException("dispose failed") };
        var client = new TwitchIrcClient(Tokens(), Quiet(), () => connection);
        var run = client.RunAsync((message, _) => message.Command == "PRIVMSG" ? throw failure : Task.CompletedTask);
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected);
        connection.Enqueue(Chat);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => run.WaitAsync(TestTimeout));
        Assert.Same(failure, error);

        // A lost connection whose disposal fails is still replaced.
        var time = new IrcTestTimeProvider();
        var broken = new FakeIrcConnection { DisposeFailure = new IOException("dispose failed") };
        var next = new FakeIrcConnection();
        var queue = new ConcurrentQueue<FakeIrcConnection>([broken, next]);
        var reconnecting = new TwitchIrcClient(Tokens(), Quiet(), () => Next(queue), time);
        using var stop = new CancellationTokenSource(TestTimeout);
        var running = reconnecting.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => reconnecting.IsConnected);
        broken.CloseFromServer();
        await ManualTimeProvider.WaitUntilAsync(() => broken.Disposed && time.HasPendingDelays(1));
        time.Advance(TimeSpan.FromSeconds(1));
        await ManualTimeProvider.WaitUntilAsync(() => reconnecting.IsConnected && next.Sent.Contains("NICK bot"));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task CancelledJoinKeepsAChannelAnotherCallerJoined()
    {
        var time = new IrcTestTimeProvider();
        var connection = new FakeIrcConnection();
        var options = new TwitchIrcOptions { Login = "bot", JoinRateLimit = new IrcRateLimit(1, TimeSpan.FromSeconds(10)), KeepaliveInterval = Timeout.InfiniteTimeSpan };
        var client = new TwitchIrcClient(Tokens(), options, () => connection, time);
        using var stop = new CancellationTokenSource(TestTimeout);
        var run = client.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        await ManualTimeProvider.WaitUntilAsync(() => client.IsConnected);
        await client.JoinAsync("first").WaitAsync(TestTimeout);

        using var cancel = new CancellationTokenSource();
        var cancelled = client.JoinAsync("shared", cancel.Token);
        var other = client.JoinAsync("#Shared");
        Assert.False(other.IsCompleted);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WaitAsync(TestTimeout));
        Assert.Contains("shared", client.JoinedChannels);
        // Wait for the remaining JOIN to arm its limiter delay; advancing earlier would schedule it past the new time.
        await ManualTimeProvider.WaitUntilAsync(() => time.HasPendingDelays(10));
        time.Advance(TimeSpan.FromSeconds(10));
        await other.WaitAsync(TestTimeout);
        Assert.Equal(new[] { "JOIN #first", "JOIN #shared" }, connection.Sent.Where(IsJoin));
        Assert.Equal(new[] { "first", "shared" }, client.JoinedChannels.Order());
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task PartDuringPendingJoinOrRejoinSuppressesTheStaleJoin()
    {
        var time = new IrcTestTimeProvider();
        var connection = new FakeIrcConnection();
        // The send timeout is longer than the join window, so holding the send lock below does not end the session.
        var options = new TwitchIrcOptions
        {
            Login = "bot", JoinRateLimit = new IrcRateLimit(1, TimeSpan.FromSeconds(10)), KeepaliveInterval = Timeout.InfiniteTimeSpan, KeepaliveTimeout = TimeSpan.FromSeconds(60),
        };
        var client = new TwitchIrcClient(Tokens(), options, () => connection, time);
        await client.JoinAsync("alpha").WaitAsync(TestTimeout);
        await client.JoinAsync("beta").WaitAsync(TestTimeout);
        using var stop = new CancellationTokenSource(TestTimeout);
        var run = client.RunAsync((_, _) => Task.CompletedTask, stop.Token);

        // The rejoin sends one channel and waits for a permit for the other, which is parted while a user send holds the send lock.
        await ManualTimeProvider.WaitUntilAsync(() => connection.Sent.Any(IsJoin) && time.HasPendingDelays(10));
        var joined = connection.Sent.Single(IsJoin)["JOIN #".Length..];
        var parted = joined == "alpha" ? "beta" : "alpha";
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.SendGate = gate;
        var hold = client.SendMessageAsync("chan", "hold");
        await ManualTimeProvider.WaitUntilAsync(() => connection.Attempted.Contains("PRIVMSG #chan :hold"));
        time.Advance(TimeSpan.FromSeconds(10));
        // Let the rejoin take its permit and queue for the send lock before the PART.
        await Task.Delay(50);
        var part = client.PartAsync(parted);
        var pendingJoin = client.JoinAsync("gamma");
        var partPending = client.PartAsync("gamma");
        connection.SendGate = null;
        gate.SetResult();
        await hold.WaitAsync(TestTimeout);
        await part.WaitAsync(TestTimeout);
        await partPending.WaitAsync(TestTimeout);
        await ManualTimeProvider.WaitUntilAsync(() => time.HasPendingDelays(10));
        time.Advance(TimeSpan.FromSeconds(10));
        await pendingJoin.WaitAsync(TestTimeout);

        Assert.Equal(new[] { "JOIN #" + joined }, connection.Sent.Where(IsJoin));
        Assert.Contains("PART #" + parted, connection.Sent);
        Assert.Equal(new[] { joined }, client.JoinedChannels);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TestTimeout));
    }

    private static TwitchIrcOptions Quiet() => new() { Login = "bot", KeepaliveInterval = Timeout.InfiniteTimeSpan };

    private static TwitchIrcOptions Keepalive() => new() { Login = "bot", KeepaliveInterval = TimeSpan.FromSeconds(60), KeepaliveTimeout = TimeSpan.FromSeconds(10) };

    private static FakeIrcConnection Track(ConcurrentQueue<FakeIrcConnection> created, FakeIrcConnection connection)
    {
        created.Enqueue(connection);
        return connection;
    }
}
