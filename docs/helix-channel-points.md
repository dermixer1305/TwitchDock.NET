# Custom rewards and redemptions

All six endpoints are available through `helix.ChannelPoints`, with a final `CancellationToken`. They require a broadcaster user token: the broadcaster ID must match the token owner. Reading accepts either `channel:read:redemptions` or `channel:manage:redemptions`; mutations require `channel:manage:redemptions`. Twitch requires partner/affiliate status. The SDK checks known token metadata and leaves app ownership, channel eligibility and reward state to Twitch.

## Create and list rewards

```csharp
var created = await helix.ChannelPoints.CreateCustomRewardAsync("123", new()
{
    Title = "Choose the next topic", Cost = 500,
    Prompt = "Which topic?", IsUserInputRequired = true,
    IsEnabled = true, BackgroundColor = "#9147FF",
    IsMaxPerStreamEnabled = true, MaxPerStream = 5,
    IsMaxPerUserPerStreamEnabled = true, MaxPerUserPerStream = 1,
    IsGlobalCooldownEnabled = true, GlobalCooldownSeconds = 60,
    ShouldRedemptionsSkipRequestQueue = false
}, cancellationToken);
var rewardId = created.Data.Single().Id;

var rewards = await helix.ChannelPoints.GetCustomRewardsAsync(new()
{
    BroadcasterId = "123", Ids = [rewardId], OnlyManageableRewards = true
}, cancellationToken);
```

A channel can have at most 50 custom rewards, counting enabled and disabled rewards. Titles must be unique on the channel, contain 1–45 characters, and costs must be at least one point. Costs use `long`, matching the documented Int64 request. Prompts accept up to 200 characters; provide one when requesting viewer input. Background color uses `#RRGGBB`.

Creation's optional settings are omitted when null so Twitch applies its defaults. If a limit is explicitly enabled on creation, supply its positive value. Cooldowns below 60 seconds may be accepted by the API but are not shown in Twitch's UI. Twitch manages reward uniqueness, stock, channel limits and eligibility. Custom images are returned when configured but cannot be uploaded through these endpoints.

Listing supports up to 50 reward IDs and an optional `OnlyManageableRewards` filter. Explicit false is preserved. Twitch ignores duplicate IDs and returns found rewards in ascending ID order; if no requested IDs are found, it returns 404. This endpoint is not paginated.

Reward responses include broadcaster identity, images, flags, per-stream/per-user limits, cooldown settings and stream usage. `Image`, `RedemptionsRedeemedCurrentStream` and `CooldownExpiresAt` can be null. Default images expose `Url1x`, `Url2x` and `Url4x`, mapped to the exact underscore-containing wire names. Cost and configured limits use 64-bit integers.

## Change and delete rewards

```csharp
await helix.ChannelPoints.UpdateCustomRewardAsync("123", rewardId, new()
{
    Cost = 750, Prompt = "", IsPaused = true,
    IsMaxPerStreamEnabled = false, MaxPerStream = 0
}, cancellationToken);

// Re-enable a limit already configured on Twitch without replacing its duration.
await helix.ChannelPoints.UpdateCustomRewardAsync("123", rewardId, new()
{
    IsGlobalCooldownEnabled = true
}, cancellationToken);

await helix.ChannelPoints.DeleteCustomRewardAsync("123", rewardId, cancellationToken);
```

Only the app that created a reward may modify or delete it. The broadcaster and reward IDs are query parameters; only requested changes go into the PATCH body. All update fields are optional. Null omits a field; false, empty prompt and values for disabled limits are kept. The update supports all creation settings plus `IsPaused`. Enabled cooldown updates accept at most 604800 seconds. If an enable flag is omitted, Twitch evaluates a supplied limit against the existing setting; the SDK does not fetch or guess that state.

Deletion returns no body on HTTP 204. Twitch marks outstanding unfulfilled redemptions as fulfilled when deleting the reward, so deletion is not a refund operation. The SDK sends only the explicitly requested deletion and does not batch or cascade additional requests.

## Read and update redemptions

```csharp
var page = await helix.ChannelPoints.GetCustomRewardRedemptionsAsync(new()
{
    BroadcasterId = "123", RewardId = rewardId,
    Status = "UNFULFILLED", Sort = "OLDEST", First = 50, After = "cursor"
}, cancellationToken);
await foreach (var redemption in helix.ChannelPoints.EnumerateCustomRewardRedemptionsAsync(new()
{
    BroadcasterId = "123", RewardId = rewardId, Status = "UNFULFILLED"
}, cancellationToken))
    Console.WriteLine($"{redemption.UserName}: {redemption.UserInput}");

var selected = await helix.ChannelPoints.GetCustomRewardRedemptionsAsync(new()
{
    BroadcasterId = "123", RewardId = rewardId, Ids = ["redemption-id"]
}, cancellationToken);
await helix.ChannelPoints.UpdateRedemptionStatusAsync(new()
{
    BroadcasterId = "123", RewardId = rewardId,
    Ids = ["redemption-id"], Status = "CANCELED"
}, cancellationToken);
```

Only the creating app may read or change a reward's redemptions. Reading requires a status (`CANCELED`, `FULFILLED`, `UNFULFILLED`) unless at least one redemption ID is supplied. Up to 50 IDs are allowed; duplicate IDs are ignored. Sort accepts `OLDEST` or `NEWEST`, and page sizes are 1–50, with `After` pagination. The enumerator snapshots filters and follows cursors. Canceled/fulfilled redemptions are retained by Twitch only for a few days. Responses include the viewer, entered text, status, timestamp and a compact reward snapshot with ID/title/prompt/cost.

Status updates require 1–50 redemption IDs, all for the specified reward, and set only `CANCELED` or `FULFILLED`. Only currently unfulfilled redemptions can change. Canceling refunds the viewer's points; fulfilling marks the request complete. The body contains only `status`; IDs remain query parameters. No implicit splitting into batches or retry after ambiguous mutation failure occurs.

Errors use `TwitchApiException`: 403 includes app-ownership/eligibility failures, and 404 includes unknown rewards/redemptions or redemptions no longer unfulfilled. Local tests in `ChannelPointsTests` cover all documented response fields, request bodies, query limits, scope/identity checks, optional values, pagination and errors. Fixtures are synthetic contract cases; live integration remains required before stable publication.

Sources: pinned official [Create Custom Rewards](https://dev.twitch.tv/docs/api/reference/#create-custom-rewards), [Delete Custom Reward](https://dev.twitch.tv/docs/api/reference/#delete-custom-reward), [Get Custom Reward](https://dev.twitch.tv/docs/api/reference/#get-custom-reward), [Get Redemptions](https://dev.twitch.tv/docs/api/reference/#get-custom-reward-redemption), [Update Custom Reward](https://dev.twitch.tv/docs/api/reference/#update-custom-reward), [Update Redemption Status](https://dev.twitch.tv/docs/api/reference/#update-redemption-status).
