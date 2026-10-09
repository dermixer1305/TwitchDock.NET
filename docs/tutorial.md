# Tutorial: your first Twitch bot

**English** · [Deutsch](tutorial.de.md) · [Project home](../README.md)

In about 15 minutes you register a Twitch application, create your own C# project, run a chat bot with commands, add follow/sub/raid alerts and read data from the Twitch API. Everything comes from [nuget.org](https://www.nuget.org/packages/TwitchDock.DependencyInjection); you do not need to download or build this repository. Use a test channel for experiments.

## 1. What you need

- The [.NET SDK](https://dotnet.microsoft.com/download) 8 or 10. Check with `dotnet --version`.
- A Twitch account with a verified email address and two-factor authentication (required for the developer console).
- Any editor: Visual Studio, Rider, VS Code or a plain text editor.

## 2. Register your Twitch application

1. Open the [Twitch developer console](https://dev.twitch.tv/console/apps) and sign in.
2. Choose **Register Your Application**. Use your own unique name, such as `MyChannel Bot` with a personal suffix if needed.
3. Add `http://localhost:3000` as an OAuth redirect URL and click **Add**. Select a category, such as **Chat Bot**.
4. Select the **Confidential** client type. You only need its secret for the API example in step 6; keep it on your own machine or server and never ship it inside an application.
5. Create the application, then choose **Manage** and copy the **Client ID**.

The client ID is not a secret. The bot signs in with a device code and needs nothing else. The redirect URL is not used by that flow; it is there for a later authorization-code login.

Sources: [register an app](https://dev.twitch.tv/docs/authentication/register-app/), [OAuth flows](https://dev.twitch.tv/docs/authentication/getting-tokens-oauth/).

## 3. Create your project

```sh
dotnet new console -n MyTwitchBot
cd MyTwitchBot
dotnet add package TwitchDock.DependencyInjection --prerelease
```

`--prerelease` is needed while 1.0.0 is a release candidate. The package brings in all TwitchDock modules; [which package do I need?](../README.md#which-package-do-i-need) explains how to pick single ones later.

## 4. Your first chat bot

Replace the content of `Program.cs` with the following and put your client ID into the `ClientId` line:

```csharp
using TwitchDock.Authentication;
using TwitchDock.Core;
using TwitchDock.EventSub;
using TwitchDock.Helix;
using TwitchDock.Helix.Models;

const string ClientId = "your-client-id"; // from dev.twitch.tv/console/apps – not a secret

using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
var oauth = new TwitchOAuthClient(http);

// 1. Sign in: Twitch shows a code, you confirm it in the browser. No client secret needed.
string[] scopes = [TwitchScopes.UserReadChat, TwitchScopes.UserWriteChat];
var device = await oauth.StartDeviceAuthorizationAsync(ClientId, scopes);
Console.WriteLine($"Open {device.VerificationUri} and confirm the code {device.UserCode}");
var grant = await oauth.WaitForDeviceAuthorizationAsync(ClientId, device, scopes);

// 2. Validate the token once: tells the SDK who signed in and which scopes were granted.
var me = await oauth.ValidateAsync(grant.AccessToken);
var botId = me.UserId!;
var channelId = botId; // your own channel; use another broadcaster ID to join a different chat
var tokens = new StaticAccessTokenProvider(me.ToAccessToken(grant.AccessToken));
var helix = new HelixClient(new TwitchHttpClient(http, tokens, new TwitchHttpOptions { ClientId = ClientId }));

// 3. Commands: every chat message arrives as a typed event.
var router = new EventSubEventRouter()
    .On(EventSubEvents.ChannelChatMessageV1, async (chat, _, ct) =>
    {
        Console.WriteLine($"{chat.ChatterUserName}: {chat.Message.Text}");
        var reply = chat.Message.Text.Trim().ToLowerInvariant() switch
        {
            "!ping" => "pong",
            "!hello" => $"Hello @{chat.ChatterUserName}!",
            "!dice" => $"You rolled a {Random.Shared.Next(1, 7)}",
            _ => null,
        };
        if (reply is null) return;

        await helix.SendChatMessageAsync(new SendChatMessageRequest
        {
            BroadcasterId = channelId, SenderId = botId, Message = reply, ReplyParentMessageId = chat.MessageId,
        }, ct);
    });

// 4. Connect: EventSub over WebSocket, with reconnects handled by the client.
var socket = new EventSubWebSocketClient();
await socket.RunAsync(
    async (session, resubscribe, ct) =>
    {
        if (!resubscribe) return; // Twitch moved the session and kept the subscription
        await helix.SubscribeWebSocketAsync(EventSubSubscriptions.ChannelChatMessageV1(channelId, botId), session.Id, ct);
        Console.WriteLine("Bot is online – type !ping in your chat.");
    },
    router.DispatchAsync);
```

Start it:

```sh
dotnet run
```

1. Open the Twitch URL printed in the terminal and confirm the displayed code.
2. Sign in with the account the bot should use and approve **reading and sending chat messages**.
3. Wait for **Bot is online**, then open that account's channel chat.
4. Type **`!ping`**, **`!hello`** or **`!dice`**. The bot prints every message and replies to the commands. One account is enough; a separate bot account is optional.
5. Press **Ctrl+C** to stop it.

**What happens here:** the device login returns a user token, and `ValidateAsync` tells the SDK which user and scopes it belongs to, so a wrong token fails with a clear error before anything is sent. `EventSubWebSocketClient` keeps a WebSocket open to Twitch and reconnects on its own. Whenever a session starts, the bot subscribes to `channel.chat.message`, and the router hands each message to your handler as a typed `ChannelChatMessageEvent`. Replies go out through the Helix API.

Tokens stay in memory and are never printed or saved, so you sign in again at every start. You can remove the app's access at any time in your [Twitch connections](https://www.twitch.tv/settings/connections).

## 5. Add alerts for follows, subs and raids

Events work exactly like chat messages: request the scope, add a handler, subscribe. Replace `Program.cs` again; the lines marked **New** are the changes:

```csharp
using TwitchDock.Authentication;
using TwitchDock.Core;
using TwitchDock.EventSub;
using TwitchDock.Helix;
using TwitchDock.Helix.Models;

const string ClientId = "your-client-id"; // from dev.twitch.tv/console/apps – not a secret

using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
var oauth = new TwitchOAuthClient(http);

// New: the bot also needs to see followers and subscribers.
string[] scopes =
[
    TwitchScopes.UserReadChat, TwitchScopes.UserWriteChat,
    TwitchScopes.ModeratorReadFollowers, TwitchScopes.ChannelReadSubscriptions,
];
var device = await oauth.StartDeviceAuthorizationAsync(ClientId, scopes);
Console.WriteLine($"Open {device.VerificationUri} and confirm the code {device.UserCode}");
var grant = await oauth.WaitForDeviceAuthorizationAsync(ClientId, device, scopes);

var me = await oauth.ValidateAsync(grant.AccessToken);
var botId = me.UserId!;
var channelId = botId;
var tokens = new StaticAccessTokenProvider(me.ToAccessToken(grant.AccessToken));
var helix = new HelixClient(new TwitchHttpClient(http, tokens, new TwitchHttpOptions { ClientId = ClientId }));

// New: sends a chat message in the channel; used by commands and alerts.
Task Say(string message, CancellationToken ct, string? replyTo = null) =>
    helix.SendChatMessageAsync(new SendChatMessageRequest
    {
        BroadcasterId = channelId, SenderId = botId, Message = message, ReplyParentMessageId = replyTo,
    }, ct);

var router = new EventSubEventRouter()
    .On(EventSubEvents.ChannelChatMessageV1, async (chat, _, ct) =>
    {
        Console.WriteLine($"{chat.ChatterUserName}: {chat.Message.Text}");
        var reply = chat.Message.Text.Trim().ToLowerInvariant() switch
        {
            "!ping" => "pong",
            "!hello" => $"Hello @{chat.ChatterUserName}!",
            "!dice" => $"You rolled a {Random.Shared.Next(1, 7)}",
            _ => null,
        };
        if (reply is not null) await Say(reply, ct, replyTo: chat.MessageId);
    })
    // New: alerts in chat.
    .On(EventSubEvents.ChannelFollowV2, (follow, _, ct) => Say($"Thanks for the follow, {follow.UserName}!", ct))
    .On(EventSubEvents.ChannelSubscribeV1, (sub, _, ct) => Say($"Welcome to the team, {sub.UserName}!", ct))
    .On(EventSubEvents.ChannelRaidV1, (raid, _, ct) => Say($"{raid.FromBroadcasterUserName} is raiding with {raid.Viewers} viewers!", ct));

var socket = new EventSubWebSocketClient();
await socket.RunAsync(
    async (session, resubscribe, ct) =>
    {
        if (!resubscribe) return;
        EventSubSubscriptionSpec[] subscriptions =
        [
            EventSubSubscriptions.ChannelChatMessageV1(channelId, botId),
            EventSubSubscriptions.ChannelFollowV2(channelId, moderatorUserId: botId), // New
            EventSubSubscriptions.ChannelSubscribeV1(channelId),                    // New
            EventSubSubscriptions.ChannelRaidV1(toBroadcasterUserId: channelId),    // New
        ];
        foreach (var subscription in subscriptions)
            await helix.SubscribeWebSocketAsync(subscription, session.Id, ct);
        Console.WriteLine("Bot is online – type !ping in your chat.");
    },
    router.DispatchAsync);
```

Run `dotnet run` again and approve the additional permissions. Follow your channel from a second account to see the alert.

- Follows need `moderator:read:followers`. The bot reads its own channel, so it counts as its own moderator.
- Subscriptions need `channel:read:subscriptions` and only happen on Affiliate or Partner channels.
- Raids need no scope.
- If a scope is missing, `SubscribeWebSocketAsync` throws `TwitchAuthorizationException` naming it, before anything is sent.

All 83 EventSub types work this way: Channel Points redemptions, cheers, polls, Hype Trains, stream online/offline and more. Find the name on `EventSubEvents` / `EventSubSubscriptions` and the required scopes in [EventSub](eventsub.md).

## 6. Read data from the Twitch API

Tools that only read public data (user profiles, live streams, clips) can use an **app token** instead of a user login. It needs the client secret: in the developer console choose **Manage → New Secret**. Generating a new secret invalidates the previous one. Create a second project:

```sh
dotnet new console -n MyTwitchApi
cd MyTwitchApi
dotnet add package TwitchDock.DependencyInjection --prerelease
```

`Program.cs`:

```csharp
using TwitchDock.Authentication;
using TwitchDock.Core;
using TwitchDock.Helix;

var clientId = Environment.GetEnvironmentVariable("TWITCH_CLIENT_ID")!;
var clientSecret = Environment.GetEnvironmentVariable("TWITCH_CLIENT_SECRET")!; // keep secrets out of source code

using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
var oauth = new TwitchOAuthClient(http);
// App token: fetched with client ID + secret and renewed automatically when it expires.
using var tokens = new RefreshingTokenProvider((_, ct) => oauth.GetAppTokenAsync(clientId, clientSecret, ct));
var helix = new HelixClient(new TwitchHttpClient(http, tokens, new TwitchHttpOptions { ClientId = clientId }));

var users = await helix.GetUsersAsync(new() { Logins = ["twitchdev"] });
Console.WriteLine($"{users.Data[0].DisplayName}: {users.Data[0].Description}");

var streams = await helix.GetStreamsAsync(new() { Languages = ["de"], First = 5 });
foreach (var stream in streams.Data)
    Console.WriteLine($"{stream.UserName} plays {stream.GameName} for {stream.ViewerCount} viewers");
```

Enter the credentials at prompts instead of writing them into a file or your command history. In **PowerShell**:

```powershell
$env:TWITCH_CLIENT_ID = Read-Host 'Client ID'
$secretInput = Read-Host 'Client secret' -AsSecureString
$env:TWITCH_CLIENT_SECRET = [System.Net.NetworkCredential]::new('', $secretInput).Password
dotnet run
Remove-Item Env:TWITCH_CLIENT_SECRET
```

In **Bash**:

```bash
read -r -p 'Client ID: ' TWITCH_CLIENT_ID
read -r -s -p 'Client secret: ' TWITCH_CLIENT_SECRET
export TWITCH_CLIENT_ID TWITCH_CLIENT_SECRET
dotnet run
unset TWITCH_CLIENT_SECRET
```

Expected: the `twitchdev` profile and five live German streams. Every Helix group hangs off `helix`: `helix.Channels`, `helix.Moderation`, `helix.Polls`, `helix.Clips`, `helix.ChannelPoints` and more; see the [documentation index](README.md).

## 7. Keep the bot running for hours

Twitch requires long-running apps to validate user tokens every hour, and a bot should stop cleanly on Ctrl+C. In the bot from step 5, replace everything from `var socket = new EventSubWebSocketClient();` to the end with:

```csharp
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); }; // New: Ctrl+C stops the bot cleanly

var socket = new EventSubWebSocketClient();
var chatLoop = socket.RunAsync(
    async (session, resubscribe, ct) =>
    {
        if (!resubscribe) return;
        EventSubSubscriptionSpec[] subscriptions =
        [
            EventSubSubscriptions.ChannelChatMessageV1(channelId, botId),
            EventSubSubscriptions.ChannelFollowV2(channelId, moderatorUserId: botId),
            EventSubSubscriptions.ChannelSubscribeV1(channelId),
            EventSubSubscriptions.ChannelRaidV1(toBroadcasterUserId: channelId),
        ];
        foreach (var subscription in subscriptions)
            await helix.SubscribeWebSocketAsync(subscription, session.Id, ct);
        Console.WriteLine("Bot is online – type !ping in your chat.");
    },
    router.DispatchAsync, stop.Token);

// New: validates the token now and every hour; the task fails when Twitch no longer accepts the token.
var validationLoop = TokenValidationLoop.RunAsync(oauth, tokens, ClientId, (_, _) => Task.CompletedTask, cancellationToken: stop.Token);

await Task.WhenAny(chatLoop, validationLoop); // whichever ends first stops the bot
await stop.CancelAsync();
try { await Task.WhenAll(chatLoop, validationLoop); }
catch (OperationCanceledException) { Console.WriteLine("Bot stopped."); }
```

A user token expires after a few hours. To keep the bot running without signing in again, use a `RefreshingTokenProvider` with the refresh token and store rotated tokens securely; see [token providers and refresh](authentication.md#token-providers-and-refresh). The complete [chat bot sample](../samples/TwitchDock.ChatBot/Program.cs) also reports messages that Twitch drops.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| `Invalid client name` during registration | Choose a unique app name; do not copy another app's name. |
| `dotnet add package` finds no version | Add `--prerelease`, or use `--version 1.0.0-rc.2`. |
| Empty client ID | Put the client ID into `Program.cs` (bot), or set the variable in the same terminal that runs `dotnet run` (API example). |
| `401` or invalid token | Check the client ID and secret, or sign in again. App tokens and user tokens serve different operations. |
| `TwitchAuthorizationException` | The token lacks a scope. Add it to `scopes`, restart and approve the new permission. |
| Device code expired | Restart the program and use the new URL and code. |
| No reply in chat | Wait for **Bot is online**, write in the signed-in account's channel and send exactly `!ping`. |
| No follow alert | Follow from a **different** account; you cannot follow yourself. |
| Target framework error | Use .NET SDK 8 or 10; the packages support `net8.0` and `net10.0`. |

## Where to go next

- [Which package do I need?](../README.md#which-package-do-i-need) with an example for every package.
- [Authentication](authentication.md): token refresh, hourly validation, authorization code and OpenID Connect.
- [EventSub](eventsub.md): all event types, webhooks, conduits and reconnect behavior.
- [Runnable samples](samples.md), including an ASP.NET Core webhook receiver, and the [API reference index](README.md).

Keep secrets out of source control and logs. This tutorial is an introduction; production applications also need secure token storage, error handling and the practices in [SECURITY.md](../SECURITY.md).
