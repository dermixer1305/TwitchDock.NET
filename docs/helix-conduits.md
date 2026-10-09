# EventSub conduits

`HelixClient.Conduits` manages app-owned conduits through all six [official endpoints](https://dev.twitch.tv/docs/api/reference/#get-conduits). All six require an **app access token**, including assigning WebSocket sessions to shards. No user scopes are required. Known user tokens are rejected before HTTP; Twitch checks ownership against the token's client ID. Unknown token metadata is checked by Twitch.

```csharp
var created = await helix.Conduits.CreateConduitAsync(new() { ShardCount = 2 }, ct);
var conduitId = created.Data.Single().Id;
var conduits = await helix.Conduits.GetConduitsAsync(ct);

// sessionId comes from an EventSub WebSocket Welcome message.
var result = await helix.Conduits.UpdateConduitShardsAsync(new()
{
    ConduitId = conduitId,
    Shards =
    [
        new() { Id = "0", Transport = new() { Method = "websocket", SessionId = sessionId } },
        new() { Id = "1", Transport = new() { Method = "webhook", Callback = callbackUrl, Secret = webhookSecret } }
    ]
}, ct);
foreach (var error in result.Errors)
    Console.WriteLine($"Shard {error.Id}: {error.Code}: {error.Message}");

await foreach (var shard in helix.Conduits.EnumerateConduitShardsAsync(new() { ConduitId = conduitId }, ct))
    Console.WriteLine($"Shard {shard.Id}: {shard.Status}");

// This removes shard 1. Lower counts disable and remove the highest IDs.
await helix.Conduits.UpdateConduitAsync(new() { Id = conduitId, ShardCount = 1 }, ct);
await helix.Conduits.DeleteConduitAsync(conduitId, ct);
```

Shard IDs are zero-based strings. `GetConduitShardsAsync` accepts `ConduitId`, optional `Status` and `After`; the enumerator follows cursors and detects cycles through the shared paginator. There is no documented `first` parameter. Status is a string to preserve new server values. Response transports contain webhook callbacks or WebSocket session IDs and nullable connection/disconnection timestamps, never the webhook secret.

`UpdateConduitShardsAsync` accepts 1–100 entries and returns `UpdateConduitShardsResponse`. HTTP **202 can include both successes in `Data` and failures in `Errors`**, or only failures. A successful HTTP request must not be interpreted as success for every shard. The SDK preserves each failure's shard ID, message and code and does not retry individual shard failures. Callers decide whether to repair and resubmit failed entries. Successful webhook assignments may still be awaiting callback verification; notifications only arrive on enabled shards.

The reference marks transport `method` optional, so the request model allows omission and leaves those semantics to Twitch. Explicit methods accept webhook or websocket; local validation checks the relevant fields, HTTPS port 443 and 10–100 ASCII secret characters. Webhook redirects are not followed by Twitch. Twitch checks whether a session is connected and whether a shard lies within the current conduit range. Secrets are redacted in the transport request's `ToString()` but are necessarily included in the JSON sent to Twitch; do not log request bodies.

Conduit creation and resizing send JSON integer shard counts; deletion sends an `id` query parameter. Shard updates send `conduit_id` in the JSON body, as the request table and example specify (the reference's 400 description incorrectly calls it a query parameter). The SDK rejects nonpositive counts; server limits remain authoritative. Deleting a conduit can take time to appear as disabled subscriptions. Creating a conduit and connecting its shards does not create EventSub subscriptions: use `CreateEventSubSubscriptionAsync` with a typed spec from `EventSubSubscriptions` and the transport method `conduit` with its `ConduitId` afterward ([EventSub](eventsub.md#conduits)).

HTTP 400, 401, 404 and 429 are surfaced as `TwitchApiException` with the original status and message after the shared bounded refresh/rate-limit handling. In particular, create's 429 can indicate the application's conduit limit, which waiting may not resolve. Ambiguous mutation failures are not automatically retried as transient 5xx errors. No automatic shard allocation, failover manager or hosting adapter is implied by these endpoint wrappers.

Offline fixtures and contract tests cover all documented response fields, both transport types, nullable timestamps, request placement, app-only authorization, pagination, batch limits, partial failures and HTTP errors. They use synthetic data, not credentialed Twitch responses.
