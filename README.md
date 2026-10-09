# TwitchDock.NET

**An independent .NET SDK for the Twitch API, EventSub and chat.**

[English](https://github.com/dermixer1305/TwitchDock.NET/blob/main/README.md) · [Deutsch](https://github.com/dermixer1305/TwitchDock.NET/blob/main/README.de.md) · [Tutorial](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/tutorial.md) · [Documentation](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/README.md) · [NuGet](https://www.nuget.org/packages/TwitchDock.DependencyInjection)

[![NuGet](https://img.shields.io/nuget/vpre/TwitchDock.DependencyInjection?label=NuGet)](https://www.nuget.org/packages/TwitchDock.DependencyInjection)
[![Build and test](https://github.com/dermixer1305/TwitchDock.NET/actions/workflows/ci.yml/badge.svg)](https://github.com/dermixer1305/TwitchDock.NET/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/dermixer1305/TwitchDock.NET/blob/main/LICENSE)

Build chat bots, stream alerts and channel tools in C# with typed requests, typed events and async methods. TwitchDock runs on **.NET 8 and .NET 10** and supports trimming and native AOT.

This is an independent community project, written from scratch. It is not affiliated with or endorsed by Twitch and is not a fork or successor of TwitchLib.

## Install

```sh
dotnet add package TwitchDock.DependencyInjection --prerelease
```

This one package brings in all modules. You also need a **client ID**: register an application in the [Twitch developer console](https://dev.twitch.tv/console/apps) and copy its client ID. The [tutorial](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/tutorial.md) walks through the registration step by step.

## Your first chat bot

A complete `Program.cs` for a console app. It signs in with your Twitch account, joins your own chat and answers commands:

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

Run it with `dotnet run`, open the printed link, confirm the code and type `!ping` in your channel chat. One account is enough: the bot may answer in its own channel.

Twitch requires long-running apps to validate user tokens every hour. The [chat bot sample](https://github.com/dermixer1305/TwitchDock.NET/blob/main/samples/TwitchDock.ChatBot/Program.cs) adds that with `TokenValidationLoop`, plus Ctrl+C handling and drop detection for messages Twitch rejects. Refreshing tokens is covered in [authentication](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/authentication.md).

## React to follows, subs, raids and more

Request the scopes the events need, sign in as above, then route every event to its own typed handler:

```csharp
string[] scopes =
[
    TwitchScopes.ModeratorReadFollowers, TwitchScopes.ChannelReadSubscriptions,
    TwitchScopes.ChannelReadRedemptions, TwitchScopes.BitsRead,
];
// ... sign in and create `helix` exactly as in the chat bot ...

var router = new EventSubEventRouter()
    .On(EventSubEvents.ChannelFollowV2, (follow, _, _) => Log($"{follow.UserName} followed"))
    .On(EventSubEvents.ChannelSubscribeV1, (sub, _, _) => Log($"{sub.UserName} subscribed (tier {sub.Tier})"))
    .On(EventSubEvents.ChannelCheerV1, (cheer, _, _) => Log($"{cheer.UserName ?? "Anonymous"} cheered {cheer.Bits} bits"))
    .On(EventSubEvents.ChannelRaidV1, (raid, _, _) => Log($"{raid.FromBroadcasterUserName} raided with {raid.Viewers} viewers"))
    .On(EventSubEvents.ChannelPointsCustomRewardRedemptionAddV1, (redeem, _, _) => Log($"{redeem.UserName} redeemed {redeem.Reward.Title}"))
    .On(EventSubEvents.StreamOnlineV1, (online, _, _) => Log($"{online.BroadcasterUserName} is live!"));

await new EventSubWebSocketClient().RunAsync(
    async (session, resubscribe, ct) =>
    {
        if (!resubscribe) return;
        EventSubSubscriptionSpec[] subscriptions =
        [
            EventSubSubscriptions.ChannelFollowV2(channelId, moderatorUserId: channelId),
            EventSubSubscriptions.ChannelSubscribeV1(channelId),
            EventSubSubscriptions.ChannelCheerV1(channelId),
            EventSubSubscriptions.ChannelRaidV1(toBroadcasterUserId: channelId),
            EventSubSubscriptions.ChannelPointsCustomRewardRedemptionAddV1(channelId),
            EventSubSubscriptions.StreamOnlineV1(channelId),
        ];
        foreach (var subscription in subscriptions)
            await helix.SubscribeWebSocketAsync(subscription, session.Id, ct);
    },
    router.DispatchAsync);

static Task Log(string text)
{
    Console.WriteLine(text);
    return Task.CompletedTask;
}
```

All 83 EventSub types have a factory on `EventSubSubscriptions` and a typed event on `EventSubEvents`. The SDK checks the token's scopes before subscribing and throws `TwitchAuthorizationException` if one is missing. Webhooks and conduits are supported too: see [EventSub](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/eventsub.md).

## Call the Twitch API

Server tools that only read public data can use an app token instead of a user login:

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

`HelixClient` covers all 149 Helix endpoints: channels, moderation, polls, predictions, Channel Points, clips, schedules and more. Rate limits, retries and pagination (`Enumerate*Async`) are built in. See the [documentation index](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/README.md).

## ASP.NET Core and dependency injection

```csharp
using TwitchDock.Authentication;
using TwitchDock.Core;
using TwitchDock.DependencyInjection;
using TwitchDock.Helix;

var builder = WebApplication.CreateBuilder(args);
var clientId = builder.Configuration["Twitch:ClientId"]!;
var clientSecret = builder.Configuration["Twitch:ClientSecret"]!; // user secrets or environment variables

builder.Services.AddTwitchDock(new TwitchHttpOptions { ClientId = clientId }, sp =>
{
    var oauth = sp.GetRequiredService<TwitchOAuthClient>();
    return new RefreshingTokenProvider((_, ct) => oauth.GetAppTokenAsync(clientId, clientSecret, ct));
});
// Validates the token at startup and every hour, as Twitch requires for long-running apps.
builder.Services.AddTwitchTokenValidation(new TwitchTokenValidationOptions { ExpectedClientId = clientId });

var app = builder.Build();

app.MapGet("/live/{login}", async (string login, HelixClient helix, CancellationToken ct) =>
{
    var streams = await helix.GetStreamsAsync(new() { UserLogins = [login] }, ct);
    return streams.Data is [var stream]
        ? Results.Ok(new { live = true, stream.Title, stream.GameName, stream.ViewerCount })
        : Results.Ok(new { live = false });
});

app.Run();
```

`AddTwitchDock` registers `HelixClient`, `TwitchChatClient`, `EventSubWebSocketClient` and `TwitchOAuthClient`. A ready-made EventSub webhook receiver is in the [webhook sample](https://github.com/dermixer1305/TwitchDock.NET/blob/main/samples/TwitchDock.WebhookHost/Program.cs).

## Packages

| Package | What it provides |
| --- | --- |
| `TwitchDock.DependencyInjection` | `AddTwitchDock`, hosted token validation; brings in all modules below |
| `TwitchDock.Core` | HTTP transport, rate limits, bounded retries, pagination, tokens and scope checks |
| `TwitchDock.Authentication` | OAuth flows, device login, OpenID Connect, token refresh, validation and revocation |
| `TwitchDock.Helix` | Typed REST API: users, streams, channels, moderation, chat, polls, Channel Points and more |
| `TwitchDock.EventSub` | Typed subscriptions and events, WebSocket client, webhooks, conduits and batching |
| `TwitchDock.Chat` | Chat over EventSub + Helix, plus IRC with reconnects and rate limiting |

Every operation accepts a `CancellationToken`. JSON serialization is source-generated. Runtime dependencies are limited to the Microsoft.Extensions packages for logging and hosting.

## Release status

**1.0.0-rc.1 is a release candidate.** Every Helix endpoint (149) and EventSub type (83) in the official documentation of 2026-10-09 has typed models, scope checks, tests and documentation ([coverage report](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/coverage.md)). 878 unit and contract tests per framework, Twitch CLI integration tests and native AOT checks run in CI. Sign-in, API reads and chat were verified live against Twitch. IRC, public webhooks, reconnects and token-refresh rotation still need broader live testing ([live-test report](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/live-verification.md)). Public APIs may still change before 1.0.0.

## Documentation

- [English tutorial](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/tutorial.md) / [Deutsches Tutorial](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/tutorial.de.md): app registration, first API call, chat bot, troubleshooting
- [C# quickstart](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/quickstart.md), [authentication](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/authentication.md), [EventSub](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/eventsub.md), [IRC](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/chat-irc.md)
- [Runnable samples](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/samples.md): API quickstart, chat bot and ASP.NET Core webhook receiver
- [Reference index](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/README.md), [roadmap](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/roadmap.md), [changelog](https://github.com/dermixer1305/TwitchDock.NET/blob/main/CHANGELOG.md)

## Contributing and security

Building from source, tests and guidelines are described in [CONTRIBUTING.md](https://github.com/dermixer1305/TwitchDock.NET/blob/main/CONTRIBUTING.md). Report vulnerabilities privately through [GitHub security reporting](https://github.com/dermixer1305/TwitchDock.NET/security/advisories/new); see the [security policy](https://github.com/dermixer1305/TwitchDock.NET/blob/main/SECURITY.md).

## License

[MIT](https://github.com/dermixer1305/TwitchDock.NET/blob/main/LICENSE). Twitch is a trademark of Twitch Interactive, Inc. This project has no affiliation with or endorsement from Twitch. Twitch's API terms and authorization requirements still apply.
