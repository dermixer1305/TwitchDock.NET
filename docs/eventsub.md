# EventSub

`TwitchDock.EventSub` covers the whole EventSub workflow: typed subscription specs, creating subscriptions through Helix, typed events with a registry and router, a WebSocket client with migration and reconnects, webhook verification and a framework-independent webhook handler. Conduits are managed through `helix.Conduits` ([conduits](helix-conduits.md)).

Every subscription type and version in the pinned official documentation (83 pairs, 2026-10-09) has a typed factory and event definition. The group references list conditions, authorization and transports per type:

| Group | Reference |
| --- | --- |
| Chat, shared chat, AutoMod, suspicious users, warnings | [eventsub-chat-automod.md](eventsub-chat-automod.md) |
| Channel updates, follows, ads, raids, bans, unban requests, moderation, moderators, VIPs, Shield Mode, Shoutouts | [eventsub-moderation-channel.md](eventsub-moderation-channel.md) |
| Bits, subscriptions, Channel Points, custom Power-ups, polls, predictions | [eventsub-monetization-interaction.md](eventsub-monetization-interaction.md) |
| Charity, goals, Hype Train, users and authorizations, conduits, drops, extensions, Guest Star (beta), stream online/offline | [eventsub-community-system.md](eventsub-community-system.md) |

## Typed subscriptions

`EventSubSubscriptions.<Type>V<version>(...)` returns an `EventSubSubscriptionSpec` with the documented `Type`, `Version` and `Condition`, plus the authorization Twitch checks: `RequiredScopes` (all of them), `AnyOfScopes` (at least one), `AuthorizingUserId` (the user whose token must create a WebSocket subscription), the accepted `Transports` and `IsBatchingEnabled`. Beta Guest Star factories end in `Beta`. Required condition values must be nonblank; optional values that are null are omitted.

```csharp
var follows = EventSubSubscriptions.ChannelFollowV2(broadcasterUserId: "123", moderatorUserId: "456");
// channel.follow@2, RequiredScopes = [moderator:read:followers], AuthorizingUserId = "456"
```

Create subscriptions with the extension methods from `TwitchDock.EventSub` on `HelixClient`:

```csharp
// WebSocket: after the session's welcome message, with the authorizing user's token.
await helix.SubscribeWebSocketAsync(EventSubSubscriptions.StreamOnlineV1(broadcasterId), session.Id, cancellationToken);

// Webhook or conduit: with an app token.
await helix.CreateEventSubSubscriptionAsync(EventSubSubscriptions.StreamOnlineV1(broadcasterId),
    new EventSubTransportRequest { Method = "webhook", Callback = "https://example.com/eventsub", Secret = webhookSecret }, cancellationToken);
await helix.CreateEventSubSubscriptionAsync(EventSubSubscriptions.ChannelFollowV2(broadcasterId, moderatorId),
    new EventSubTransportRequest { Method = "conduit", ConduitId = conduitId }, cancellationToken);
```

A transport the type does not support throws `ArgumentException` before any request. The response's `Data` holds the created subscription; `Total`, `TotalCost` and `MaxTotalCost` report your usage. A duplicate subscription fails with HTTP 409 as `TwitchApiException` whose `ExistingSubscriptionId` identifies the existing one; the SDK never deletes or replaces it for you. Webhook callbacks must use HTTPS on port 443 and secrets 10 to 100 ASCII characters. Listing and deleting subscriptions are described in [foundation endpoints](helix-foundation.md#eventsub-subscriptions). The untyped `HelixClient.CreateEventSubSubscriptionAsync(CreateEventSubSubscriptionRequest)` remains available for raw requests; it checks only the token kind for the transport.

## Authorization rules

| Transport | Token | Checked before the request | Checked by Twitch |
| --- | --- | --- | --- |
| WebSocket | User token | Token kind, `RequiredScopes`, `AnyOfScopes`, token user = `AuthorizingUserId` (when the metadata is known) | Everything else, such as moderator status |
| Webhook, conduit | App token | Token kind (user tokens are rejected) | That the authorizing user granted the scopes to your client ID, ownership of organizations, extensions and conduits |

Failed preflight checks throw `TwitchAuthorizationException` with `MissingScopes` or `RequiredAnyOfScopes`; see [authentication](authentication.md#scope-preflight-and-twitchauthorizationexception). WebSocket subscriptions always need a user token, also for types without scope requirements such as `stream.online`. `user.authorization.grant`, `user.authorization.revoke`, `drop.entitlement.grant`, `extension.bits_transaction.create` and `conduit.shard.disabled` are limited to webhooks and conduits.

## Reading events

`EventSubEvents.<Type>V<version>` is an `EventSubEventDefinition<TEvent>` that binds the type and version to its event class in `TwitchDock.EventSub.Events` and its source-generated JSON metadata.

```csharp
if (message.TryReadEvent(EventSubEvents.StreamOnlineV1, out var online))   // WebSocket message
    Console.WriteLine($"{online.BroadcasterUserName} is live since {online.StartedAt:O}");
if (payload.TryReadEvent(EventSubEvents.ChannelFollowV2, out var follow))   // verified webhook payload
    Console.WriteLine($"{follow.UserName} followed");

// Registry lookup for dynamic handling, for example logging every known event:
if (EventSubEvents.TryGetDefinition(subscriptionType, subscriptionVersion, out var definition))
    Console.WriteLine($"{definition.EventType.Name}: {definition.Deserialize(eventElement)}");
```

`TryReadEvent` returns false for other types, versions and message types. `EventSubEvents.All` enumerates every definition. Evolving discriminators (statuses, actions, message types) stay strings so values Twitch adds later are preserved. Models accept documented payload variants, for example charity events with `broadcaster_*` or `broadcaster_user_*` fields. Properties with a declared default, such as empty lists, keep it when Twitch omits the field instead of becoming null.

## Router

```csharp
var router = new EventSubEventRouter()
    .On(EventSubEvents.StreamOnlineV1, (online, subscription, ct) => NotifyAsync($"{online.BroadcasterUserName} went live", ct))
    .On(EventSubEvents.ChannelFollowV2, (follow, _, ct) => NotifyAsync($"{follow.UserName} followed", ct))
    .OnRevocation((subscription, ct) => NotifyAsync($"{subscription.Type} revoked: {subscription.Status}", ct));

await router.DispatchAsync(message, cancellationToken);                       // WebSocket
await router.DispatchAsync(messageTypeHeader, verifiedPayload, cancellationToken); // webhook
```

Each type/version can have one handler; registering a second one throws `InvalidOperationException`. `DispatchAsync` returns false when no handler applies. Register all handlers first; dispatching is then safe from several threads.

## WebSocket client

```csharp
var socket = new EventSubWebSocketClient(); // or provider.GetRequiredService<EventSubWebSocketClient>()
await socket.RunAsync(
    async (session, resubscribe, ct) =>
    {
        if (!resubscribe) return; // migrated session: subscriptions moved with it
        await helix.SubscribeWebSocketAsync(EventSubSubscriptions.StreamOnlineV1(broadcasterId), session.Id, ct);
    },
    (message, ct) => router.DispatchAsync(message, ct),
    cancellationToken);
```

Lifecycle of `RunAsync`:

- **Welcome.** It connects to `wss://eventsub.wss.twitch.tv/ws`, waits up to 30 seconds for `session_welcome` and calls `onSession(session, resubscribe: true)`. Create the subscriptions promptly there; Twitch closes sessions that stay without subscriptions.
- **Keepalive.** Every receive waits at most the session's `keepalive_timeout_seconds`. Silence beyond that counts as a lost connection.
- **Migration.** On `session_reconnect` the client opens the reconnect URL (only on the configured scheme, host and port), keeps reading the old connection until the new welcome arrives, delivers messages from both, and then calls `onSession(newSession, resubscribe: false)`.
- **Reconnect.** A closed or silent connection is replaced by a fresh session after a backoff of 1, 2, 4, 8, 16 and then 30 seconds, followed by `onSession(..., resubscribe: true)`. Twitch does not replay events missed while disconnected.
- **Messages.** `onMessage` receives notifications and revocations; keepalives are consumed internally. Callbacks run sequentially, so hand slow work to a bounded queue.
- **Errors.** An exception from a callback stops `RunAsync` and propagates (the message ID is released first). Protocol violations, such as an unexpected first message or a forged reconnect URL, end it with `JsonException`. Cancellation ends it with `OperationCanceledException`.

One instance runs one loop at a time; `AddTwitchDock` registers the client as transient. The `endpoint` constructor parameter accepts `wss://` URIs, and `ws://` only on loopback hosts for the Twitch CLI mock server.

## Webhooks

`EventSubWebhookHandler` combines verification, the callback challenge, deduplication and routing without depending on a web framework:

```csharp
var handler = new EventSubWebhookHandler(new EventSubWebhookVerifier(webhookSecret), router);

app.MapPost("/eventsub", async (HttpRequest request, HttpResponse response, CancellationToken ct) =>
{
    using var body = new MemoryStream();
    await request.Body.CopyToAsync(body, ct); // the exact bytes Twitch signed
    var result = await handler.HandleAsync(EventSubWebhookRequest.FromHeaders(name => request.Headers[name].ToString(), body.ToArray()), ct);
    response.StatusCode = result.StatusCode;
    if (result.ContentType is not null) response.ContentType = result.ContentType;
    if (result.Body is not null) await response.WriteAsync(result.Body, ct);
});
```

The complete ASP.NET Core host is [samples/TwitchDock.WebhookHost](samples.md#webhook-host). Responses: 200 with the challenge as `text/plain` for `webhook_callback_verification`, 204 for notifications, revocations, duplicates and unknown message types, 403 for invalid or stale signatures, 400 for malformed requests. A handler exception propagates so the host answers 5xx and Twitch retries the delivery.

`EventSubWebhookVerifier.VerifyAndParse` checks the HMAC-SHA256 signature over message ID, timestamp and raw body in constant time, rejects bodies over 1 MiB and timestamps older than ten minutes or more than one minute in the future, and only then parses the JSON. Never parse and re-serialize the body before verification. Limit the request body size in the host as well.

## Conduits

Conduits deliver an application's subscriptions through shards (WebSocket sessions or webhooks) with an app token. Create the conduit, assign shards with `helix.Conduits.UpdateConduitShardsAsync`, then create subscriptions with the conduit transport as shown above. For a WebSocket shard, run an `EventSubWebSocketClient` and assign `session.Id` to the shard in `onSession`; check `Errors` in the response because HTTP 202 can contain per-shard failures. Subscriptions belong to the conduit, so a new session needs a shard update, not new subscriptions. Details: [conduits](helix-conduits.md).

## Batching

Batched types deliver an array in the payload's `events` property. `drop.entitlement.grant` is the batched type in the pinned documentation: `EventSubSubscriptions.DropEntitlementGrantV1` sets `IsBatchingEnabled`, so the create request sends `"is_batching_enabled": true`, and its definition reads `IReadOnlyList<DropEntitlementGrantEvent>`. `TryReadEvent` and the router handle `event` and `events` transparently; see [community and system events](eventsub-community-system.md#batching).

## Delivery and deduplication

Twitch delivers at least once. The WebSocket client and the webhook handler suppress repeated message IDs with a `MessageDeduplicator`: process-local, eleven minutes of retention (the ten-minute webhook freshness window plus one minute of clock skew) and 100,000 IDs by default. When it is full it evicts the oldest ID and counts it in `EvictedCount`, so a busy channel cannot stop the client; pass `throwWhenFull: true` for fail-closed behavior, which raises `EventSubDeduplicationException` (the webhook handler then answers 503). A failed callback releases its ID so a redelivery is processed again.

An event that does not match its typed model (for example after a Twitch schema change) never reaches the typed handler: register `router.OnDeserializationError(...)` to observe it. The message counts as handled, so it neither stops the WebSocket client nor makes a webhook fail until Twitch revokes the subscription. The webhook handler also checks the unsigned `Twitch-Eventsub-Message-Type` header against the signed body (challenge, event, revoked status) and answers 400 on a mismatch. This is not exactly-once processing: restarts and multiple replicas do not share the memory. For restart-safe or multi-instance webhooks, verify with `EventSubWebhookVerifier`, record the message ID in a shared durable inbox before acknowledging, and dispatch with `router.DispatchAsync(messageType, payload)` from there. Make handlers idempotent, for example by event ID where Twitch provides one.

## Local testing with the Twitch CLI

The [Twitch CLI](https://dev.twitch.tv/docs/cli/) needs no Twitch account for these commands. Plain `ws://` and `http://` endpoints are accepted only on loopback hosts.

```sh
# Mock EventSub WebSocket server; connect with new EventSubWebSocketClient(endpoint: new Uri("ws://127.0.0.1:8080/ws"))
twitch event websocket start-server --port 8080
twitch event trigger channel.follow -v 2 --transport=websocket --session <session-id>
twitch event websocket reconnect   # exercises session migration

# Signed webhook deliveries to a local receiver (for example samples/TwitchDock.WebhookHost)
twitch event verify-subscription stream.online -F http://localhost:5000/eventsub -s <secret>
twitch event trigger stream.online -F http://localhost:5000/eventsub -s <secret>
```

The integration tests automate exactly these flows; see [testing](testing.md#integration-tests-twitch-cli).
