# Quickstart (.NET 8 and .NET 10)

This page gets you from zero to API calls, chat and EventSub. The [documentation index](README.md) links the reference for every API group.

## Install

The packages are on [nuget.org](https://www.nuget.org/packages/TwitchDock.DependencyInjection). Use `--prerelease` (or `--version 1.0.0-rc.2`) while 1.0.0 is a release candidate:

```sh
dotnet new console -n MyFirstBot -f net10.0
dotnet add MyFirstBot package TwitchDock.DependencyInjection --prerelease
```

`TwitchDock.DependencyInjection` pulls in the other modules: `TwitchDock.Core`, `TwitchDock.Authentication`, `TwitchDock.Helix`, `TwitchDock.EventSub` and `TwitchDock.Chat`. Building from source needs the .NET 10 SDK; consumers can target net8.0 or net10.0.

## App token and users

```csharp
using Microsoft.Extensions.DependencyInjection;
using TwitchDock.Authentication;
using TwitchDock.Core;
using TwitchDock.DependencyInjection;
using TwitchDock.Helix;

var clientId = Environment.GetEnvironmentVariable("TWITCH_CLIENT_ID")
    ?? throw new InvalidOperationException("Set TWITCH_CLIENT_ID.");
var clientSecret = Environment.GetEnvironmentVariable("TWITCH_CLIENT_SECRET")
    ?? throw new InvalidOperationException("Set TWITCH_CLIENT_SECRET.");

var services = new ServiceCollection();
services.AddTwitchDock(new TwitchHttpOptions { ClientId = clientId }, sp =>
{
    var oauth = sp.GetRequiredService<TwitchOAuthClient>();
    // App tokens are reacquired with client credentials; they have no refresh token.
    return new RefreshingTokenProvider((_, ct) => oauth.GetAppTokenAsync(clientId, clientSecret, ct));
});
using var provider = services.BuildServiceProvider();
var helix = provider.GetRequiredService<HelixClient>();
var page = await helix.GetUsersAsync(new() { Logins = ["twitchdev"] });
foreach (var user in page.Data) Console.WriteLine($"{user.Id}: {user.DisplayName}");
```

The runnable version is [samples/TwitchDock.Quickstart](samples.md#quickstart). Every API group hangs off `HelixClient` (`helix.Users`, `helix.Channels`, `helix.Streams`, `helix.Chat`, `helix.Moderation`, `helix.ChannelPoints` and more); see the [index](README.md#helix-rest-api). For user tokens (chat, moderation, channel management) pick a flow in [authentication](authentication.md).

## Streams and channels

```csharp
await foreach (var stream in helix.EnumerateStreamsAsync(new() { Languages = ["de"], First = 100 }, cancellationToken))
    Console.WriteLine($"{stream.UserName}: {stream.Title}");

var channels = await helix.GetChannelInformationAsync(["141981764"], cancellationToken);
```

`Enumerate*Async` methods follow pagination cursors with cycle protection. The streams list is live and can repeat or skip entries across pages; it is not a snapshot. More in [streams](helix-streams.md) and [channels](helix-channels.md).

## Chat over EventSub

Twitch recommends EventSub plus Helix for chat bots. Register `AddTwitchDock` with the **bot's user token** (scopes `user:read:chat` and `user:write:chat`), for example a `RefreshingTokenProvider` from the [device code flow](authentication.md#device-code).

```csharp
using TwitchDock.Chat;
using TwitchDock.EventSub;

var chat = provider.GetRequiredService<TwitchChatClient>();
var socket = provider.GetRequiredService<EventSubWebSocketClient>();
await socket.RunAsync(
    async (session, resubscribe, ct) =>
    {
        // false means Twitch migrated the session and kept its subscriptions.
        if (resubscribe) await chat.SubscribeAsync(broadcasterId, botUserId, session.Id, ct);
    },
    async (message, ct) =>
    {
        if (!TwitchChatClient.TryReadMessage(message, out var received)) return; // other events and revocations
        Console.WriteLine($"{received.ChatterUserName}: {received.Message.Text}");
        if (received.Message.Text == "!hello")
        {
            var results = await chat.SendAsync(new()
            {
                BroadcasterId = broadcasterId, SenderId = botUserId, Message = $"Hello {received.ChatterUserName}",
                ReplyParentMessageId = received.MessageId,
            }, ct);
            if (results.Data is [{ IsSent: false, DropReason: { } reason }]) Console.WriteLine($"Dropped: {reason.Code}");
        }
    },
    cancellationToken);
```

`TryReadMessage` returns the typed `ChannelChatMessageEvent`. `SubscribeAsync` checks the token's scopes and user before sending and throws `TwitchAuthorizationException` for a wrong token. A successful send can still be dropped by Twitch: check `IsSent` and `DropReason`. Callbacks run sequentially, so keep them short. The complete bot with Ctrl+C handling and hourly validation is [samples/TwitchDock.ChatBot](samples.md#chat-bot); IRC is covered in [chat over IRC](chat-irc.md).

## Typed EventSub events

For more than chat, route typed events:

```csharp
var router = new EventSubEventRouter()
    .On(EventSubEvents.StreamOnlineV1, (online, _, _) => { Console.WriteLine($"{online.BroadcasterUserName} is live"); return Task.CompletedTask; })
    .On(EventSubEvents.ChannelFollowV2, (follow, _, _) => { Console.WriteLine($"{follow.UserName} followed"); return Task.CompletedTask; })
    .OnRevocation((subscription, _) => { Console.WriteLine($"{subscription.Type} revoked: {subscription.Status}"); return Task.CompletedTask; });

await socket.RunAsync(
    async (session, resubscribe, ct) =>
    {
        if (!resubscribe) return;
        await helix.SubscribeWebSocketAsync(EventSubSubscriptions.StreamOnlineV1(broadcasterId), session.Id, ct);
        await helix.SubscribeWebSocketAsync(EventSubSubscriptions.ChannelFollowV2(broadcasterId, moderatorId), session.Id, ct);
    },
    (message, ct) => router.DispatchAsync(message, ct),
    cancellationToken);
```

Every subscription type has a factory on `EventSubSubscriptions` (condition plus required scopes) and a definition with the same name on `EventSubEvents`. See [EventSub](eventsub.md) for authorization rules, the WebSocket lifecycle, conduits and batching.

## Webhooks

Webhook subscriptions use an app token and an HTTPS callback on port 443:

```csharp
await helix.CreateEventSubSubscriptionAsync(EventSubSubscriptions.StreamOnlineV1(broadcasterId),
    new() { Method = "webhook", Callback = "https://example.com/eventsub", Secret = webhookSecret }, cancellationToken);

var handler = new EventSubWebhookHandler(new EventSubWebhookVerifier(webhookSecret), router);
// In the HTTP endpoint: pass the headers and the exact raw body bytes, then write the result back.
var result = await handler.HandleAsync(EventSubWebhookRequest.FromHeaders(name => headers[name], rawBody), cancellationToken);
```

The handler verifies the signature, answers the challenge, suppresses duplicates and dispatches to the router. The ASP.NET Core version is [samples/TwitchDock.WebhookHost](samples.md#webhook-host).

## Dependency injection and hosting

`AddTwitchDock(options, tokenProviderFactory)` registers one authorization:

| Service | Lifetime | Notes |
| --- | --- | --- |
| `TwitchHttpOptions`, `IAccessTokenProvider` | Singleton | The provider comes from your factory and is owned by the container |
| `TwitchOAuthClient` | Typed `HttpClient` | Redirects disabled, pooled connections recycled every 5 minutes |
| `TwitchHttpClient`, `HelixClient`, `TwitchChatClient` | Singleton | Share rate-limit state and one refresh gate |
| `EventSubWebSocketClient` | Transient | One instance per receive loop |
| `TimeProvider` | Singleton | `TimeProvider.System` unless registered before |

Further registrations:

- `AddTwitchTokenValidation(new TwitchTokenValidationOptions { ExpectedClientId = clientId })` adds a hosted service that validates the token at startup and hourly; details in [authentication](authentication.md#validation-startup-and-hourly).
- `AddTwitchIrc(new TwitchIrcOptions { Login = "mybot" })` adds a singleton `TwitchIrcClient` that uses the registered token provider; start it with `RunAsync`, for example from a `BackgroundService` ([chat over IRC](chat-irc.md)).

A container holds one authorization. Use separate containers or construct `TwitchHttpClient`/`HelixClient` pairs yourself for independent authorizations, for example an app token for webhooks next to a bot's user token.

## Errors, retries and limits

- `TwitchApiException` carries `StatusCode`, Twitch's `Error` and message, the `RequestId` trace ID and, for duplicate EventSub subscriptions, `ExistingSubscriptionId`.
- `TwitchAuthorizationException` reports a local preflight failure (missing scope, wrong token kind or user); nothing was sent.
- HTTP 401 triggers one token refresh and retry. HTTP 429 waits for the rate-limit reset and retries up to `MaxRateLimitRetries` (default 2) when the wait is at most `MaxRetryDelay` (default 2 minutes). Only GET and HEAD retry 500, 502, 503 and 504 (`MaxTransientRetries`, default 2); mutations are never retried after an ambiguous failure.
- Request lists that are null mean "no filter", like empty lists. Absent response fields keep the model's declared default (for example an empty list) instead of becoming null.
- `TwitchHttpOptions.BaseAddress` and the EventSub WebSocket endpoint accept plain `http://` and `ws://` only on loopback hosts, which lets you point the SDK at the [Twitch CLI](testing.md#integration-tests-twitch-cli) mock servers.

## Where to go next

- Helix: [foundation](helix-foundation.md), [users and whispers](helix-users-whispers.md), [channels](helix-channels.md), [streams](helix-streams.md), [chat catalog](helix-chat-catalog.md), [chat settings](helix-chat-settings.md), [moderation enforcement](helix-moderation-enforcement.md), [moderation roles](helix-moderation-roles.md), [Channel Points](helix-channel-points.md), [Bits and subscriptions](helix-bits-subscriptions.md), [polls and predictions](helix-polls-predictions.md), [schedule](helix-schedule.md), [Hype Train](helix-hype-train.md), [ads, analytics, games, search, goals, raids](helix-groups.md), [clips, videos, charity, teams](helix-media.md), [extensions](helix-extensions.md), [entitlements](helix-entitlements.md), [Guest Star](helix-guest-star.md), [tags, labels, authorization, Power-ups](helix-tags-labels-authorization.md), [conduits](helix-conduits.md)
- EventSub: [overview](eventsub.md), [chat and AutoMod](eventsub-chat-automod.md), [channel and moderation](eventsub-moderation-channel.md), [monetization and interaction](eventsub-monetization-interaction.md), [community and system](eventsub-community-system.md)
- Chat: [IRC](chat-irc.md); authentication: [all flows](authentication.md); [samples](samples.md); [testing](testing.md)
