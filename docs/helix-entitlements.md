# Drops entitlements

Both endpoints are available through `helix.Entitlements` (`EntitlementsClient`) and accept a final `CancellationToken`. They take an app or user access token. The token's client must be owned by a user who is a member of the organization that owns the game; Twitch checks organization and game ownership (400/403). No OAuth scope is documented.

## Read entitlements

```csharp
// App token: filter by user, game, both, or neither (everything the organization owns).
var page = await helix.Entitlements.GetDropsEntitlementsAsync(new()
{
    UserId = "25009227", GameId = "33214",
    FulfillmentStatus = DropsFulfillmentStatuses.Claimed, First = 1000
}, cancellationToken);

await foreach (var entitlement in helix.Entitlements.EnumerateDropsEntitlementsAsync(new()
{
    GameId = "33214", FulfillmentStatus = DropsFulfillmentStatuses.Claimed
}, cancellationToken))
    Console.WriteLine($"{entitlement.UserId} claimed {entitlement.BenefitId} at {entitlement.Timestamp}");

// Specific entitlements (up to 100 IDs).
var selected = await helix.Entitlements.GetDropsEntitlementsAsync(new() { Ids = ["fb78259e-..."] }, cancellationToken);
```

Which filters are allowed depends on the token:

| Token | Filters | Result |
| --- | --- | --- |
| App | none | All entitlements owned by the organization |
| App | `UserId` | All of the organization's entitlements granted to that user |
| App | `UserId`, `GameId` | Entitlements the game granted to that user |
| App | `GameId` | Entitlements the game granted to all users |
| User | none | The token user's entitlements for any of the organization's games |
| User | `GameId` | The token user's entitlements for that game |
| User | `UserId` (with or without `GameId`) | Invalid |

The SDK rejects `UserId` with a known user token before sending (`TwitchAuthorizationException`). `Ids` (at most 100) and `FulfillmentStatus` (`CLAIMED` or `FULFILLED`, case-sensitive) can be combined with the other filters. `First` accepts 1–1000 (Twitch default 20). Pagination uses `After`; the enumerator snapshots the ID list and follows cursors with cycle protection. Twitch does not sort entitlements by any returned field, so filter by status or game instead of relying on order.

Each `DropsEntitlement` has `Id`, `BenefitId`, `Timestamp` (granted), `UserId`, `GameId`, `FulfillmentStatus` and `LastUpdated`. Statuses stay strings so new values do not break deserialization; `DropsFulfillmentStatuses` provides the documented constants.

## Update fulfillment status

```csharp
var result = await helix.Entitlements.UpdateDropsEntitlementsAsync(new()
{
    EntitlementIds = ["fb78259e-fb81-4d1b-8333-34a06ffc24c0", "862750a5-265e-4ab6-9f0a-c64df3d54dd0"],
    FulfillmentStatus = DropsFulfillmentStatuses.Fulfilled
}, cancellationToken);

foreach (var group in result.Data)
{
    if (group.Status == DropsEntitlementUpdateStatuses.UpdateFailed)
        retryLater.AddRange(group.Ids); // transient; retry these IDs later
    else if (group.Status != DropsEntitlementUpdateStatuses.Success)
        Console.WriteLine($"{group.Status}: {string.Join(", ", group.Ids)}");
}
```

An app token updates matching entitlements whose benefits the organization owns; a user token only updates the user's own entitlements with benefits owned by the organization. Both body fields are optional in the reference, so null fields are omitted. When supplied, `EntitlementIds` holds at most 100 IDs and `FulfillmentStatus` must be `CLAIMED` (the user claimed the benefit) or `FULFILLED` (you granted it).

HTTP 200 only means the request was processed. Check every returned group: `SUCCESS`, `INVALID_ID`, `NOT_FOUND`, `UNAUTHORIZED` or `UPDATE_FAILED` (transient, retry later), each with the affected `Ids`. The SDK does not split, merge or retry updates automatically, and a PATCH is never retried after an ambiguous 5xx or network failure.

## Errors and tests

Errors become `TwitchApiException` with status, message and `RequestId`. 400 covers an invalid status, a `user_id` that does not match a user token, or a client not linked to the organization; 401 covers missing or invalid tokens and client ID mismatches; 403 means the organization does not own the requested game or entitlements; Twitch also documents 500 for internal errors (GET requests use the transport's bounded retry for 5xx).

`EntitlementsTests` covers every documented response field, exact queries and bodies, the token-dependent filter rules, ID and page-size limits, status validation, enumeration and error preservation. Fixtures are synthetic contract cases; live verification remains required before a stable release.

Sources: pinned official reference for [Get Drops Entitlements](https://dev.twitch.tv/docs/api/reference/#get-drops-entitlements) and [Update Drops Entitlements](https://dev.twitch.tv/docs/api/reference/#update-drops-entitlements).
