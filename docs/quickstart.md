# Quickstart (.NET 8 and .NET 10)

For Ads, Analytics, Games, Search, Goals and Raids, see [the group reference and examples](helix-groups.md).
For Clips, Videos, Charity and Teams, see [the media and community reference](helix-media.md).
For the current Hype Train and channel records, see [Hype Train status](helix-hype-train.md).

The package name is provisional and packages are currently local build artifacts. Reference the projects while developing, or add `artifacts/packages` as a local NuGet source after `dotnet pack`.

## App token and users

```csharp
using Microsoft.Extensions.DependencyInjection;
using TwitchSdk.Authentication;
using TwitchSdk.Core;
using TwitchSdk.DependencyInjection;
using TwitchSdk.Helix;

var clientId = Environment.GetEnvironmentVariable("TWITCH_CLIENT_ID")
    ?? throw new InvalidOperationException("Set TWITCH_CLIENT_ID.");
var clientSecret = Environment.GetEnvironmentVariable("TWITCH_CLIENT_SECRET")
    ?? throw new InvalidOperationException("Set TWITCH_CLIENT_SECRET.");

var services = new ServiceCollection();
services.AddTwitchSdk(new TwitchHttpOptions { ClientId = clientId }, sp =>
{
    var oauth = sp.GetRequiredService<TwitchOAuthClient>();
    // App tokens are reacquired with client credentials; they have no refresh token.
    return new RefreshingTokenProvider((_, ct) => oauth.GetAppTokenAsync(clientId, clientSecret, ct));
});
using var provider = services.BuildServiceProvider();
var helix = provider.GetRequiredService<HelixClient>();
var page = await helix.GetUsersAsync(new() { Logins = ["twitchdev"] });
foreach (var user in page.Data) Console.WriteLine(user.DisplayName);
```

For a persistent OAuth session, run `TokenValidationLoop.RunAsync` alongside your application, starting before serving authenticated work. Pass the same token provider and expected client ID. It validates immediately and hourly while idle. Observe its task: on an invalid token terminate the associated application sessions, and handle transient validation failures with a bounded host policy. This is not silently started by AddTwitchSdk.

## User authorization and rotation

Use `TwitchOAuthClient.CreateState()` and save the value in the initiating browser session. Redirect to `CreateAuthorizationUri(clientId, registeredRedirect, scopes, state)`. On callback, compare state with `ValidateState`, consume it once, handle denial, and only then call `ExchangeCodeAsync`. Register the redirect URI exactly with Twitch. Do not embed client secrets in public clients.

Construct `RefreshingTokenProvider` with the returned token and an acquire delegate calling `oauth.RefreshAsync(clientId, refreshToken, clientSecret, ct)`. Supply a persistence callback to save rotated tokens in an encrypted store. Refreshes are serialized for one provider instance. Coordinate independently across processes; this library does not implement a distributed lock. If durable persistence fails, the request fails but the new token remains in memory to avoid reusing an invalidated refresh token.

## Streams and channels

```csharp
await foreach (var stream in helix.EnumerateStreamsAsync(
    new() { Languages = ["de"], First = 100 }, cancellationToken))
    Console.WriteLine($"{stream.UserName}: {stream.Title}");

var channels = await helix.GetChannelInformationAsync(["141981764"], cancellationToken);
```

The streams list is live and may contain duplicates or omit entries across pages. Pagination protects against cursor cycles but does not turn Twitch's dynamic listing into a snapshot.

## Chat and EventSub

Use a **user token** with `user:read:chat` to subscribe to WebSocket chat. Sending requires `user:write:chat`. App-token bots have additional grants/role requirements described in [Twitch chat authentication](https://dev.twitch.tv/docs/chat/authenticating/).

```csharp
var chat = provider.GetRequiredService<TwitchSdk.Chat.TwitchChatClient>();
var socket = provider.GetRequiredService<TwitchSdk.EventSub.EventSubWebSocketClient>();
await socket.RunAsync(
    async (session, resubscribe, ct) =>
    {
        if (resubscribe) await chat.SubscribeAsync(broadcasterId, botUserId, session.Id, ct);
    },
    (message, ct) =>
    {
        if (TwitchSdk.Chat.TwitchChatClient.TryReadMessage(message, out var received))
            Console.WriteLine($"{received!.ChatterUserName}: {received.Message.Text}");
        // Also handle revocations here; keep this callback brief.
        return Task.CompletedTask;
    }, cancellationToken);
```

Send with `chat.SendAsync(new() { BroadcasterId = broadcasterId, SenderId = botUserId, Message = "Hello" }, ct)`. Check each result's `IsSent` and `DropReason`, even after HTTP success. Leave `ForSourceOnly` unset with user tokens. `Pin = true` additionally requires moderator permission and cannot combine with reply or source-only options.

Subscription management is available through `CreateEventSubSubscriptionAsync`, `GetEventSubSubscriptionsAsync`, `EnumerateEventSubSubscriptionsAsync` and `DeleteEventSubSubscriptionAsync`. Listing supports status, type, user ID, subscription ID, conduit ID (mutually exclusive) and after. Webhooks/conduits require app tokens; WebSockets require user tokens. Generic condition maps do not count as dedicated typed coverage of all subscription types. See [full method examples and authorization rules](helix-foundation.md), including the separate request/response transports and conflict IDs.

## Webhooks

At an HTTPS callback on port 443, read the original bytes with a 1 MiB limit. Get headers case-insensitively and call `EventSubWebhookVerifier.VerifyAndParse(messageId, timestamp, signature, bytes)`. Reject invalid signatures/timestamps. For callback verification, return `payload.Challenge` as plain text. For notifications/revocations, persist and deduplicate by message ID before acknowledging 2xx, then process asynchronously. Do not deserialize and reserialize before verifying. Multi-instance hosting requires a shared durable inbox.
