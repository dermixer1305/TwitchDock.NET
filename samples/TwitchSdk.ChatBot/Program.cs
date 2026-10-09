// A minimal chat bot: reads chat through an EventSub WebSocket and answers "!ping" through Helix Send Chat Message.
// Credentials come only from environment variables. The user token must belong to TWITCH_BOT_USER_ID and carry
// user:read:chat (receive) and user:write:chat (send).
using TwitchSdk.Authentication;
using TwitchSdk.Core;
using TwitchSdk.EventSub;
using TwitchSdk.Helix;
using TwitchSdk.Helix.Models;

var clientId = RequireEnvironment("TWITCH_CLIENT_ID");
var accessToken = RequireEnvironment("TWITCH_ACCESS_TOKEN");
var botUserId = RequireEnvironment("TWITCH_BOT_USER_ID");
var broadcasterId = RequireEnvironment("TWITCH_BROADCASTER_ID");

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};

// Twitch's OAuth and Helix hosts never need redirects; following one could leak the token.
using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
var oauth = new TwitchOAuthClient(http);

// Validate first: the result carries the granted scopes and the user ID, so the SDK can reject a wrong token
// (missing scope, other user, app token) before it sends a subscription or chat message.
var validation = await oauth.ValidateAsync(accessToken, stop.Token);
if (validation.ClientId != clientId) throw new InvalidOperationException("The token was issued to a different client ID.");
if (validation.UserId != botUserId) throw new InvalidOperationException("The token does not belong to TWITCH_BOT_USER_ID.");
var tokens = new StaticAccessTokenProvider(validation.ToAccessToken(accessToken));
var helix = new HelixClient(new TwitchHttpClient(http, tokens, new TwitchHttpOptions { ClientId = clientId }));

var router = new EventSubEventRouter()
    .On(EventSubEvents.ChannelChatMessageV1, async (chat, _, ct) =>
    {
        Console.WriteLine($"{chat.ChatterUserName}: {chat.Message.Text}");
        if (chat.ChatterUserId == botUserId || !string.Equals(chat.Message.Text.Trim(), "!ping", StringComparison.OrdinalIgnoreCase)) return;
        // Callbacks run sequentially; a busy bot should hand work to a bounded queue instead of awaiting here.
        var results = await helix.SendChatMessageAsync(new SendChatMessageRequest
        {
            BroadcasterId = broadcasterId, SenderId = botUserId, Message = "pong", ReplyParentMessageId = chat.MessageId,
        }, ct);
        // HTTP success does not mean the message was delivered; Twitch reports drops per message.
        foreach (var result in results.Data.Where(r => !r.IsSent))
            Console.WriteLine($"Reply dropped: {result.DropReason?.Code} {result.DropReason?.Message}");
    })
    .OnRevocation((subscription, _) =>
    {
        Console.WriteLine($"Subscription {subscription.Type} was revoked: {subscription.Status}");
        return Task.CompletedTask;
    });

var socket = new EventSubWebSocketClient();
var chatLoop = socket.RunAsync(async (session, resubscribe, ct) =>
{
    // false means Twitch migrated the session and the subscriptions moved with it.
    if (!resubscribe) return;
    await helix.SubscribeWebSocketAsync(EventSubSubscriptions.ChannelChatMessageV1(broadcasterId, botUserId), session.Id, ct);
    Console.WriteLine($"Connected as {validation.Login}. Send !ping in the channel; press Ctrl+C to stop.");
}, (message, ct) => router.DispatchAsync(message, ct), stop.Token);

// Twitch requires long-running applications to validate user tokens hourly. The loop throws when the token
// becomes invalid; a static token cannot be refreshed, so the bot then stops.
var validationLoop = TokenValidationLoop.RunAsync(oauth, tokens, clientId, (_, _) => Task.CompletedTask, cancellationToken: stop.Token);

await Task.WhenAny(chatLoop, validationLoop);
await stop.CancelAsync();
try
{
    await Task.WhenAll(chatLoop, validationLoop);
}
catch (OperationCanceledException) when (stop.IsCancellationRequested)
{
    Console.WriteLine("Stopped.");
}

static string RequireEnvironment(string name)
    => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new InvalidOperationException($"Set the {name} environment variable.");
