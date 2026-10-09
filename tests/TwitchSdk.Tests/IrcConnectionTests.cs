using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using TwitchSdk.Chat.Irc;
using TwitchSdk.Core;

namespace TwitchSdk.Tests;

/// <summary>Exercises the real transports against loopback servers.</summary>
public sealed class IrcConnectionTests
{
    [Fact]
    public async Task WebSocketConnectionSplitsFramesIntoLinesAndCapsLineSize()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = timeout.Token;
        using var listener = StartListener(out var port);
        await using var connection = new WebSocketIrcConnection(maxLineBytes: 512);
        var accept = AcceptWebSocketAsync(listener, ct);
        await connection.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), ct);
        var (client, server) = await accept;
        using var serverClient = client;
        using var serverSocket = server;

        await SendTextAsync(server, "PING :a\r\nPRIVMSG #c :hi\r\n\r\n", ct);
        Assert.Equal("PING :a", await connection.ReceiveLineAsync(ct));
        Assert.Equal("PRIVMSG #c :hi", await connection.ReceiveLineAsync(ct));

        await SendTextAsync(server, "PRIVMSG #c :par", ct);
        await SendTextAsync(server, "tial\r\n", ct);
        Assert.Equal("PRIVMSG #c :partial", await connection.ReceiveLineAsync(ct));

        // A multi-byte character split across two frames of one message.
        var bytes = Encoding.UTF8.GetBytes("PRIVMSG #c :\U0001F600!\n");
        var split = Array.IndexOf(bytes, (byte)0xF0) + 2;
        await server.SendAsync(bytes.AsMemory(0, split), WebSocketMessageType.Text, endOfMessage: false, ct);
        await server.SendAsync(bytes.AsMemory(split), WebSocketMessageType.Text, endOfMessage: true, ct);
        Assert.Equal("PRIVMSG #c :\U0001F600!", await connection.ReceiveLineAsync(ct));

        await SendTextAsync(server, new string('x', 600) + "\r\nPING :after\r\n", ct);
        Assert.Equal("PING :after", await connection.ReceiveLineAsync(ct));

        await connection.SendLineAsync("PONG :a", ct);
        Assert.Equal("PONG :a\r\n", await ReceiveTextAsync(server, ct));
        await Assert.ThrowsAsync<ArgumentException>(() => connection.SendLineAsync("PRIVMSG #c :x\r\nJOIN #evil", ct));

        await server.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", ct);
        Assert.Null(await connection.ReceiveLineAsync(ct));
        Assert.Null(await connection.ReceiveLineAsync(ct));
    }

    [Fact]
    public async Task TcpConnectionSplitsLinesAndRejectsPlainTextOutsideLoopback()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = timeout.Token;
        using var listener = StartListener(out var port);
        await using var connection = new TcpIrcConnection(maxLineBytes: 512);
        var accept = listener.AcceptTcpClientAsync(ct).AsTask();
        await connection.ConnectAsync(new Uri($"irc://127.0.0.1:{port}"), ct);
        using var server = await accept;
        var stream = server.GetStream();

        await stream.WriteAsync("PING :x\r\nPRIVMSG #c :a"u8.ToArray(), ct);
        Assert.Equal("PING :x", await connection.ReceiveLineAsync(ct));
        await stream.WriteAsync(Encoding.UTF8.GetBytes("b\r\n" + new string('y', 700) + "\nNOTICE * :ok\r\n"), ct);
        Assert.Equal("PRIVMSG #c :ab", await connection.ReceiveLineAsync(ct));
        Assert.Equal("NOTICE * :ok", await connection.ReceiveLineAsync(ct));

        await connection.SendLineAsync("PONG :x", ct);
        var buffer = new byte[64];
        var read = 0;
        while (read < 9) read += await stream.ReadAsync(buffer.AsMemory(read), ct);
        Assert.Equal("PONG :x\r\n", Encoding.UTF8.GetString(buffer, 0, read));

        server.Client.Shutdown(SocketShutdown.Send);
        Assert.Null(await connection.ReceiveLineAsync(ct));

        await using var remote = new TcpIrcConnection();
        await Assert.ThrowsAsync<ArgumentException>(() => remote.ConnectAsync(new Uri("irc://irc.chat.twitch.tv:6667"), ct));
        await Assert.ThrowsAsync<ArgumentException>(() => remote.ConnectAsync(new Uri("wss://irc-ws.chat.twitch.tv"), ct));
        await using var socket = new WebSocketIrcConnection();
        await Assert.ThrowsAsync<ArgumentException>(() => socket.ConnectAsync(new Uri("ircs://irc.chat.twitch.tv:6697"), ct));
    }

    [Fact]
    public async Task ClientLogsInAndReceivesChatOverLoopbackWebSocket()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = timeout.Token;
        using var listener = StartListener(out var port);
        var tokens = new StaticAccessTokenProvider(new AccessToken("secret-token", scopes: [TwitchScopes.ChatRead, TwitchScopes.ChatEdit], kind: TwitchTokenKind.User));
        var client = new TwitchIrcClient(tokens, new TwitchIrcOptions { Login = "Bot", Endpoint = new Uri($"ws://127.0.0.1:{port}/") });
        await client.JoinAsync("chan");
        var chat = new TaskCompletionSource<IrcChatMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var run = client.RunAsync((message, _) =>
        {
            if (IrcChatMessage.TryCreate(message, out var view)) chat.TrySetResult(view);
            return Task.CompletedTask;
        }, stop.Token);

        var (tcp, server) = await AcceptWebSocketAsync(listener, ct);
        using var serverClient = tcp;
        using var serverSocket = server;
        Assert.Equal("CAP REQ :twitch.tv/tags twitch.tv/commands twitch.tv/membership\r\n", await ReceiveTextAsync(server, ct));
        Assert.Equal("PASS oauth:secret-token\r\n", await ReceiveTextAsync(server, ct));
        Assert.Equal("NICK bot\r\n", await ReceiveTextAsync(server, ct));
        await SendTextAsync(server, ":tmi.twitch.tv CAP * ACK :twitch.tv/tags twitch.tv/commands twitch.tv/membership\r\n:tmi.twitch.tv 001 bot :Welcome, GLHF!\r\n", ct);
        Assert.Equal("JOIN #chan\r\n", await ReceiveTextAsync(server, ct));
        await SendTextAsync(server, "@display-name=Viewer;id=m1 :viewer!viewer@viewer.tmi.twitch.tv PRIVMSG #chan :hello\r\n", ct);
        var received = await chat.Task.WaitAsync(ct);
        Assert.Equal(("chan", "hello", "Viewer", "m1"), (received.Channel, received.Text, received.DisplayName, received.MessageId));

        await client.SendMessageAsync("chan", "hi back", received.MessageId, ct);
        Assert.Equal("@reply-parent-msg-id=m1 PRIVMSG #chan :hi back\r\n", await ReceiveTextAsync(server, ct));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task ClientUsesTcpTransportForIrcEndpoints()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = timeout.Token;
        using var listener = StartListener(out var port);
        var tokens = new StaticAccessTokenProvider(new AccessToken("secret-token", scopes: [TwitchScopes.ChatRead], kind: TwitchTokenKind.User));
        var client = new TwitchIrcClient(tokens, new TwitchIrcOptions { Login = "bot", Endpoint = new Uri($"irc://127.0.0.1:{port}") });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var run = client.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        using var server = await listener.AcceptTcpClientAsync(ct);
        using var reader = new StreamReader(server.GetStream(), Encoding.UTF8, leaveOpen: true);
        Assert.StartsWith("CAP REQ", await reader.ReadLineAsync(ct), StringComparison.Ordinal);
        Assert.Equal("PASS oauth:secret-token", await reader.ReadLineAsync(ct));
        Assert.Equal("NICK bot", await reader.ReadLineAsync(ct));
        await server.GetStream().WriteAsync(":tmi.twitch.tv 001 bot :Welcome, GLHF!\r\nPING :tmi.twitch.tv\r\n"u8.ToArray(), ct);
        Assert.Equal("PONG :tmi.twitch.tv", await reader.ReadLineAsync(ct));
        Assert.True(client.IsConnected);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task TransportsRejectNulAndPlainTextOutsideLoopbackAddresses()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = timeout.Token;
        await using var tcp = new TcpIrcConnection();
        await Assert.ThrowsAsync<ArgumentException>(() => tcp.SendLineAsync("PRIVMSG #c :a\0b", ct));
        // Only IP literal loopback addresses and localhost are trusted for plain text, not names a hosts file may map to loopback.
        await Assert.ThrowsAsync<ArgumentException>(() => tcp.ConnectAsync(new Uri("irc://localhost.localdomain:6667"), ct));
        await Assert.ThrowsAsync<ArgumentException>(() => tcp.ConnectAsync(new Uri("irc://ip6-localhost:6667"), ct));
        await using var socket = new WebSocketIrcConnection();
        await Assert.ThrowsAsync<ArgumentException>(() => socket.SendLineAsync("PRIVMSG #c :a\0b", ct));
        await Assert.ThrowsAsync<ArgumentException>(() => socket.ConnectAsync(new Uri("ws://localhost.localdomain/"), ct));
        // The transport enforces this itself, also when used without TwitchIrcOptions.
        await Assert.ThrowsAsync<ArgumentException>(() => socket.ConnectAsync(new Uri("ws://irc-ws.chat.twitch.tv/"), ct));
    }

    [Fact]
    public async Task TlsHandshakeBrokenByTheServerIsReportedAsATransientIOException()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = timeout.Token;
        using var listener = StartListener(out var port);
        var server = CloseAfterClientHelloAsync(listener, ct);
        await using var connection = new TcpIrcConnection();
        await Assert.ThrowsAnyAsync<IOException>(() => connection.ConnectAsync(new Uri($"ircs://127.0.0.1:{port}"), ct));
        await server;
    }

    [Fact]
    public async Task ClientWarnsWhenTheTransportDiscardsOversizedLines()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = timeout.Token;
        using var listener = StartListener(out var port);
        var logger = new CapturingLogger();
        var tokens = new StaticAccessTokenProvider(new AccessToken("secret-token", scopes: [TwitchScopes.ChatRead], kind: TwitchTokenKind.User));
        var options = new TwitchIrcOptions { Login = "bot", Endpoint = new Uri($"irc://127.0.0.1:{port}") };
        var client = new TwitchIrcClient(tokens, options, () => new TcpIrcConnection(maxLineBytes: 512), logger: logger);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var run = client.RunAsync((_, _) => Task.CompletedTask, stop.Token);
        using var server = await listener.AcceptTcpClientAsync(ct);
        using var reader = new StreamReader(server.GetStream(), Encoding.UTF8, leaveOpen: true);
        for (var i = 0; i < 3; i++) Assert.NotNull(await reader.ReadLineAsync(ct));
        await server.GetStream().WriteAsync(Encoding.UTF8.GetBytes(":tmi.twitch.tv 001 bot :Welcome, GLHF!\r\n" + new string('x', 600) + "\r\nPING :tmi.twitch.tv\r\n"), ct);
        Assert.Equal("PONG :tmi.twitch.tv", await reader.ReadLineAsync(ct));
        await ManualTimeProvider.WaitUntilAsync(() => logger.Entries.Any(entry => entry.StartsWith("Warning: Twitch IRC discarded 1 ", StringComparison.Ordinal)));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    private static async Task CloseAfterClientHelloAsync(TcpListener listener, CancellationToken ct)
    {
        using var client = await listener.AcceptTcpClientAsync(ct);
        var buffer = new byte[16 * 1024];
        // Read the ClientHello, then close without answering it.
        if (await client.GetStream().ReadAsync(buffer, ct) == 0) return;
    }

    private static TcpListener StartListener(out int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return listener;
    }

    /// <summary>Performs the RFC 6455 server handshake by hand so the real ClientWebSocket can be tested without an HTTP server.</summary>
    private static async Task<(TcpClient Client, WebSocket Socket)> AcceptWebSocketAsync(TcpListener listener, CancellationToken ct)
    {
        var client = await listener.AcceptTcpClientAsync(ct);
        var stream = client.GetStream();
        var request = new List<byte>();
        var one = new byte[1];
        while (request.Count < 4 || !(request[^4] == '\r' && request[^3] == '\n' && request[^2] == '\r' && request[^1] == '\n'))
        {
            if (await stream.ReadAsync(one, ct) == 0) throw new IOException("The client closed during the WebSocket handshake.");
            request.Add(one[0]);
        }
        var key = Encoding.ASCII.GetString(request.ToArray()).Split("\r\n")
            .First(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase)).Split(':', 2)[1].Trim();
        var acceptKey = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        var response = $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {acceptKey}\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), ct);
        return (client, WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.Zero }));
    }

    private static Task SendTextAsync(WebSocket socket, string text, CancellationToken ct)
        => socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, ct);

    private static async Task<string> ReceiveTextAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[4096];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(), ct);
            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) return Encoding.UTF8.GetString(message.ToArray());
        }
    }
}
