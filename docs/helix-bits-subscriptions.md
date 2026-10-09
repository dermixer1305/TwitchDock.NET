# Bits and subscriptions

Methods are available through `helix.Bits` and `helix.Subscriptions`. All accept a final `CancellationToken`, use source-generated JSON and preserve non-success statuses through `TwitchApiException`. Examples assume a configured client from [quickstart](quickstart.md).

## Broadcaster subscriptions

```csharp
var page = await helix.Subscriptions.GetBroadcasterSubscriptionsAsync(new()
{
    BroadcasterId = "123", First = 100, After = "cursor"
}, cancellationToken);
Console.WriteLine($"{page.Total} subscribers, {page.Points} points");

var selected = await helix.Subscriptions.GetBroadcasterSubscriptionsAsync(new()
{
    BroadcasterId = "123", UserIds = ["456", "789"]
}, cancellationToken);
await foreach (var subscriber in helix.Subscriptions.EnumerateBroadcasterSubscriptionsAsync(new()
{
    BroadcasterId = "123", First = 100
}, cancellationToken))
    Console.WriteLine($"{subscriber.UserName}: {subscriber.Tier}");
```

Requires a broadcaster user token with `channel:read:subscriptions` and matching broadcaster identity. Twitch also allows an extension's app token when the broadcaster granted that scope in the Extensions manager. Local checks allow this documented app-token case; Twitch verifies the extension grant.

Optional `UserIds` filters up to 100 subscribers. Cursors are not allowed together with that filter. `First` accepts 1–100; manual pages may use either `Before` or `After`, and the forward enumerator accepts only `After`. Filtered enumeration makes one request because filtered queries cannot page with cursors; it snapshots the ID list before execution. With user IDs, `Points` and `Total` are null and must not be interpreted as zero. Unfiltered requests return both aggregates and pagination.

Subscription records contain broadcaster and subscriber identities, gift status and gifter fields, tier and plan name. Gifter strings are empty for non-gifts. Tier remains a string (`1000`, `2000`, `3000`) rather than being mapped to an integer price.

## Check the authenticated user's subscription

```csharp
try
{
    var subscription = await helix.Subscriptions.CheckUserSubscriptionAsync(new()
    {
        BroadcasterId = "123", UserId = "456"
    }, cancellationToken);
    Console.WriteLine(subscription.Data.Single().Tier);
}
catch (TwitchApiException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound)
{
    Console.WriteLine("The user does not subscribe to this broadcaster.");
}
```

Requires a user token with `user:read:subscriptions` belonging to `UserId`. The broadcaster must be a partner or affiliate. The response is a single subscription on HTTP 200; HTTP 404 means not subscribed and is preserved as an exception rather than silently collapsed into an empty response. Gift metadata is omitted for non-gifts, so those properties are nullable. Unlike broadcaster subscription records, this response has no plan name or subscriber identity fields.

## Bits leaderboard

```csharp
var leaders = await helix.Bits.GetBitsLeaderboardAsync(new()
{
    Count = 100,
    Period = "month",
    StartedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(-8)),
    UserId = "456"
}, cancellationToken);
foreach (var leader in leaders.Data)
    Console.WriteLine($"{leader.Rank}: {leader.UserName} ({leader.Score} Bits)");
```

Requires a user token with `bits:read`; the broadcaster is determined by the token. `Count` accepts 1–100 (Twitch default 10). Period is `day`, `week`, `month`, `year` or `all` (default). A supplied start instant requires an explicit period; bounded periods require a start instant, matching Twitch's documented 400 condition. `all` ignores the supplied instant on Twitch's side. The SDK encodes the instant in UTC without changing it. Twitch aggregates in its documented PST reporting timezone, so midnight UTC may fall in the previous reporting day/month; the example explicitly selects midnight at UTC−08:00.

Optional `UserId` centers results around that cheerer's rank rather than necessarily starting with the highest-ranked user. The response includes identities, rank, a 64-bit Bits score, total ranked results and a date range. The range uses strings because Twitch documents empty dates when no start instant was supplied. There is no pagination for this endpoint.

## Cheermotes

```csharp
var global = await helix.Bits.GetCheermotesAsync(cancellationToken: cancellationToken);
var channel = await helix.Bits.GetCheermotesAsync("123", cancellationToken);
var tier = channel.Data.First().Tiers.First();
var image = tier.Images.Dark.Animated["1.5"];
```

Accepts app or user tokens without additional scopes. Omit the broadcaster to get global Cheermotes, or supply it to include custom channel Cheermotes when available. Each Cheermote includes prefix, type, display order, last update, charitable-match flag and tiers. Tiers contain minimum Bits, ID, color, cheer/card flags and image URLs. Images use typed dark/light themes and animated/static formats; size keys remain strings to preserve `1`, `1.5`, `2`, `3` and `4` and accept new sizes. Access a size only when present for the returned set. The SDK returns URLs and does not download images. A display-only type is internal to Twitch and should not be used for cheering.

## Extension transactions

```csharp
var transactions = await helix.Bits.GetExtensionTransactionsAsync(new()
{
    ExtensionId = "extension-client-id", Ids = ["transaction-id"], First = 20, After = "cursor"
}, cancellationToken);
await foreach (var transaction in helix.Bits.EnumerateExtensionTransactionsAsync(new()
{
    ExtensionId = "extension-client-id", First = 100
}, cancellationToken))
    Console.WriteLine($"{transaction.ProductData.DisplayName}: {transaction.ProductData.Cost.Amount}");
```

This publicly documented endpoint is restricted to the owning extension: it requires an app token whose client ID matches `ExtensionId`. Local checks require an app token; Twitch verifies ownership. It is marked `extension-owner` in the availability review. Optional transaction IDs allow up to 100 repeated `id` parameters. `First` is 1–100; `After` and the forward enumerator support pagination. The enumerator snapshots ID filters.

Models include transaction timestamp, broadcaster/buyer identities, product type, SKU/domain, cost, development status, display name, expiration and broadcast flag. The wire names `inDevelopment` and `displayName` are deliberately preserved with JSON attributes, despite surrounding snake_case fields. Expiration is an empty string because only unexpired products can be bought. HTTP 404 means one or more supplied transaction IDs were not found.

`BitsAndSubscriptionsTests` and the independent `helix-bits-subscriptions.json` fixture verify complete response tables, nullable/omitted values, nested images and product fields, every query parameter, token distinctions, limits, pagination and errors. Live credentialed validation remains a release requirement.

Sources: pinned official [Broadcaster Subscriptions](https://dev.twitch.tv/docs/api/reference/#get-broadcaster-subscriptions), [Check User Subscription](https://dev.twitch.tv/docs/api/reference/#check-user-subscription), [Bits Leaderboard](https://dev.twitch.tv/docs/api/reference/#get-bits-leaderboard), [Cheermotes](https://dev.twitch.tv/docs/api/reference/#get-cheermotes), [Extension Transactions](https://dev.twitch.tv/docs/api/reference/#get-extension-transactions).
