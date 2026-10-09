# Users, streams, channels, chat and subscription management

These methods are on `HelixClient`. All accept a final `CancellationToken` and use the shared HTTP pipeline. Examples assume an initialized `helix` and `cancellationToken`; see [quickstart](quickstart.md).

## Users, streams and channels

```csharp
var users = await helix.GetUsersAsync(new()
{
    Ids = ["123"], Logins = ["example"]
}, cancellationToken);
var me = await helix.GetUsersAsync(cancellationToken: cancellationToken);
var channels = await helix.GetChannelInformationAsync(["123", "456"], cancellationToken);

var page = await helix.GetStreamsAsync(new()
{
    UserIds = ["123"], UserLogins = ["example"], GameIds = ["509658"],
    Languages = ["en"], Type = "live", First = 50, Before = "previous-cursor"
}, cancellationToken);
await foreach (var stream in helix.EnumerateStreamsAsync(new()
{
    GameIds = ["509658"], First = 100, After = "starting-cursor"
}, cancellationToken))
    Console.WriteLine(stream.Title);
```

`GetUsersAsync` accepts at most 100 IDs and login names combined. Without filters it requires a user token and returns its owner. Reading users does not require an email scope: `user:read:email` only controls the optional email field, which belongs to the authorizing user. Other users' email values may be empty. The deprecated `ViewCount` value is invalid and must not be used.

Streams accept up to 100 values for each repeated filter. `Type` accepts `all` or `live`; `First` accepts 1–100. A page may use `Before` or `After`, while the forward enumerator accepts only `After`, snapshots collection filters and detects repeated cursors. Viewer ranking changes between requests, so Twitch can return duplicate or missing streams during pagination; the SDK does not deduplicate them. Use `Tags`; deprecated `TagIds` is empty and deprecated `IsMature` is false. Unset titles and categories can be empty strings.

Channel information accepts 1–100 broadcaster IDs. Twitch ignores duplicate and unknown IDs. `Delay` is normally zero: a nonzero value requires a matching broadcaster user token and the relevant partner configuration. Content classification labels, tags and branded-content status are included. These three read endpoints accept app or user tokens, subject to the no-filter users exception.

## Sending chat

```csharp
var result = (await helix.SendChatMessageAsync(new()
{
    BroadcasterId = "123", SenderId = "456", Message = "Hello!",
    ReplyParentMessageId = "parent-message-id"
}, cancellationToken)).Data.Single();
if (!result.IsSent)
    Console.WriteLine($"{result.DropReason?.Code}: {result.DropReason?.Message}");

await helix.SendChatMessageAsync(new()
{
    BroadcasterId = "123", SenderId = "456", Message = "Shared hello!",
    ForSourceOnly = false // App tokens only; false sends into all participating channels.
}, cancellationToken);

await helix.SendChatMessageAsync(new()
{
    BroadcasterId = "123", SenderId = "456", Message = "Pinned announcement", Pin = true
}, cancellationToken);
```

User tokens need `user:write:chat`, and `SenderId` must match the token owner. App tokens require prior `user:write:chat` and `user:bot` grants for the sender, plus `channel:bot` for the broadcaster unless the sender is a moderator. Twitch enforces these prior app grants and channel roles. Known user scopes and identity are checked locally; unknown token metadata is left to Twitch.

`ForSourceOnly` is only valid with app tokens. Omitting it lets Twitch apply its current default (source-only for app tokens since May 19, 2025); user-token messages are shared across the session. Explicit false is preserved. `Pin = true` additionally needs `moderator:manage:chat_messages` and broadcaster/moderator status. It cannot be combined with a reply or any `ForSourceOnly` value. Twitch pins for 20 minutes and does not send the message if pinning fails.

Messages accept at most 500 Unicode code points; Twitch remains authoritative for content validation. A successful HTTP response may still contain `IsSent = false` and a `DropReason`. Check that result; it is not an HTTP exception. A sent message can have a null drop reason.

## EventSub subscriptions

```csharp
var page = await helix.GetEventSubSubscriptionsAsync(new()
{
    Type = "stream.online", After = "starting-cursor"
}, cancellationToken);
await foreach (var subscription in helix.EnumerateEventSubSubscriptionsAsync(new()
{
    UserId = "123"
}, cancellationToken))
    Console.WriteLine($"{subscription.Id}: {subscription.Status}");
await helix.DeleteEventSubSubscriptionAsync("subscription-id", cancellationToken);
```

Listing supports exactly one optional filter among `Status`, `Type`, `UserId`, `SubscriptionId` and `ConduitId`, plus an optional `After` cursor. Status and type remain strings to accept future Twitch values. Manual pages expose `Total`, `TotalCost`, `MaxTotalCost` and pagination; enumeration yields subscriptions in Twitch's oldest-first order. Use the same token kind as the transport: user token for WebSockets, app token for webhooks/conduits. Listing and deleting cannot infer the transport from an opaque subscription ID, so Twitch enforces that relationship. Deletion returns no value for HTTP 204 and throws for HTTP 404.

The low-level creation method is available, but its inventory entry remains **partial** until the dedicated typed conditions and scope requirements are implemented for all subscription types:

```csharp
var created = await helix.CreateEventSubSubscriptionAsync(new()
{
    Type = "stream.online", Version = "1",
    Condition = new Dictionary<string, string> { ["broadcaster_user_id"] = "123" },
    Transport = new EventSubTransportRequest { Method = "websocket", SessionId = "welcome-session-id" }
}, cancellationToken);
```

`EventSubTransportRequest` contains only request fields. Select `websocket` with `SessionId`, `webhook` with `Callback` and `Secret`, or `conduit` with `ConduitId`. Fields from other methods are rejected. The webhook callback must use HTTPS port 443; the secret must contain 10–100 ASCII characters and is redacted by `ToString()`. The response's separate `EventSubTransport` includes connection timestamps and never contains the secret. Explicitly typed initializers in older local alpha code must change from `EventSubTransport` to `EventSubTransportRequest` when creating subscriptions; target-typed `new()` still works.

Creation checks the token kind locally when known: user for WebSockets, app for webhooks/conduits. Generic condition maps currently defer subscription-specific scopes, required condition keys and actor identities to Twitch. HTTP 202 accepts the request; webhook subscriptions still require challenge verification before becoming enabled. Each creation response contains the newly created subscription. The synthetic contract fixture groups all three transport variants to check their different response fields; it is not a recording of one live creation call.

## Errors and verification

Non-success responses throw `TwitchApiException` with the HTTP status, Twitch error/message and optional trace ID. A duplicate EventSub subscription's HTTP 409 also exposes `ExistingSubscriptionId`, allowing callers to reconcile the existing subscription explicitly. The SDK never silently deletes or replaces it. HTTP 410 represents a removed subscription type/version. Rate-limit and refresh handling are shared; ambiguous mutations are not retried after a server error.

`HelixFoundationTests`, the earlier `HelixTests`, authorization tests and transport tests verify wire contracts, parameter encoding/bounds, scope and identity checks, pagination, bodyless deletion, dropped messages, response variants and failures on both frameworks. Contract fixtures include all documented response fields and exercise source-generated JSON with reflection disabled in the separate package-consumer check. Credentialed Twitch integration remains a release requirement.

Sources: pinned official [Users](https://dev.twitch.tv/docs/api/reference/#get-users), [Streams](https://dev.twitch.tv/docs/api/reference/#get-streams), [Channels](https://dev.twitch.tv/docs/api/reference/#get-channel-information), [Chat](https://dev.twitch.tv/docs/api/reference/#send-chat-message), [Create EventSub](https://dev.twitch.tv/docs/api/reference/#create-eventsub-subscription), [Get EventSub](https://dev.twitch.tv/docs/api/reference/#get-eventsub-subscriptions), [Delete EventSub](https://dev.twitch.tv/docs/api/reference/#delete-eventsub-subscription).
