using System.Net.Sockets;
using System.Threading.Channels;
using TwitchSdk.EventSub;

namespace TwitchSdk.IntegrationTests;

public sealed class WebSocketCliTests
{
    [TwitchCliFact]
    public async Task MockServerDeliversWelcomeTypedNotificationsAndReconnects()
    {
        using var cliLock = await TwitchCli.LockAsync();
        var port = TwitchCli.FreePort();
        using var server = TwitchCli.StartServer("event", "websocket", "start-server", "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await WaitForPortAsync(port, server);

        var sessions = Channel.CreateUnbounded<(EventSubSession Session, bool Resubscribe)>();
        var messages = Channel.CreateUnbounded<EventSubMessage>();
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var client = new EventSubWebSocketClient(endpoint: new Uri($"ws://127.0.0.1:{port}/ws"));
        var run = client.RunAsync((session, resubscribe, _) => { sessions.Writer.TryWrite((session, resubscribe)); return Task.CompletedTask; },
            (message, _) => { messages.Writer.TryWrite(message); return Task.CompletedTask; }, stop.Token);

        var (welcome, fresh) = await sessions.Reader.ReadAsync(stop.Token);
        Assert.True(fresh);
        // CLI 1.1.24 can panic in its own update check after finishing, so the delivered messages are the assertion, not the exit code.
        await TwitchCli.RunAsync(TimeSpan.FromSeconds(60), "event", "trigger", "channel.follow", "-v", "2", "--transport=websocket", "--session", welcome.Id);
        var notification = await messages.Reader.ReadAsync(stop.Token);
        Assert.True(notification.TryReadEvent(EventSubEvents.ChannelFollowV2, out var follow), notification.Metadata.SubscriptionType);
        Assert.False(string.IsNullOrEmpty(follow.UserId));

        await TwitchCli.RunAsync(TimeSpan.FromSeconds(60), "event", "websocket", "reconnect");
        var (migrated, resubscribe) = await sessions.Reader.ReadAsync(stop.Token);
        Assert.False(resubscribe);
        Assert.NotEqual(welcome.Id, migrated.Id);

        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    internal static async Task WaitForPortAsync(int port, CliServer server)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (true)
        {
            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync("127.0.0.1", port, deadline.Token);
                return;
            }
            catch (SocketException) { await Task.Delay(200, deadline.Token); }
            catch (OperationCanceledException) { throw new TimeoutException("The Twitch CLI server did not start: " + server.Output); }
        }
    }
}
