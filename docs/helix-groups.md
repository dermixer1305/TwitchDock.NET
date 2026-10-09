# Helix: Ads, Analytics, Games, Search, Goals and Raids

These groups are accessible from `HelixClient` as `Ads`, `Analytics`, `Games`, `Search`, `Goals` and `Raids`. They use the same shared HTTP transport, token provider, rate-limit coordination and error handling. Every method accepts CancellationToken. Page methods return `HelixPage<T>` with Data and Pagination; the corresponding Enumerate methods fetch subsequent pages automatically.

The contracts below were reviewed against the pinned official reference dated 2026-10-09. Offline contract tests compare every documented response-field name with independent fixtures, verify field values through source-generated JSON serialization, assert request routes/parameters, and exercise structured authorization errors. Credentialed Twitch integration remains a release-level check.

## Authorization

Known user-token scopes, token kind, client ID and user ID are checked before sending when metadata is available. OAuth grant methods tag token kind and client ID; `TokenValidation.ToAccessToken` includes validated user/scopes, and TokenValidationLoop updates RefreshingTokenProvider metadata safely. Concurrent token rotation cannot be overwritten by a stale validation result.

Unknown token metadata is deferred to Twitch. An app token's empty scope list does **not** mean the application's prior user grants are absent. Twitch checks app grants, resource ownership, roles and current channel state; the SDK exposes failures as TwitchApiException. Local preflight failures use TwitchAuthorizationException with MissingScopes where applicable.

## Ads

```csharp
var commercial = await helix.Ads.StartCommercialAsync(
    new() { BroadcasterId = broadcasterId, Length = 60 }, ct);
var schedule = await helix.Ads.GetAdScheduleAsync(broadcasterId, ct);
var snooze = await helix.Ads.SnoozeNextAdAsync(broadcasterId, ct);
```

`StartCommercialAsync` supports broadcaster_id and length in JSON. It requires `channel:edit:commercial`, authorization from that broadcaster, a live stream and partner/affiliate eligibility. Editors and moderators cannot run commercials on the broadcaster's behalf. Results include Length, Message and RetryAfter; observe the cooldown. Twitch caps requested lengths above 180 seconds. [Start Commercial](https://dev.twitch.tv/docs/api/reference/#start-commercial).

`GetAdScheduleAsync` uses broadcaster_id and requires `channel:read:ads`. Results retain every schedule field: SnoozeCount, SnoozeRefreshAt, NextAdAt, Duration, LastAdAt and PrerollFreeTime. NextAdAt and LastAdAt can be empty strings, so they remain wire-format strings. [Get Ad Schedule](https://dev.twitch.tv/docs/api/reference/#get-ad-schedule).

`SnoozeNextAdAsync` sends broadcaster_id as a query parameter and requires `channel:manage:ads`. Its result contains SnoozeCount, SnoozeRefreshAt and NextAdAt. Availability depends on the broadcaster's available snoozes and schedule. All three endpoints support either an appropriately scoped user token or an app token with the documented prior grant for the broadcaster. [Snooze Next Ad](https://dev.twitch.tv/docs/api/reference/#snooze-next-ad).

## Analytics

```csharp
var extensions = await helix.Analytics.GetExtensionAnalyticsAsync(new()
{
    ExtensionId = extensionId, Type = "overview_v2",
    StartedAt = new DateOnly(2026, 9, 1), EndedAt = new DateOnly(2026, 9, 30),
    First = 100, After = cursor
}, ct);
var games = await helix.Analytics.GetGameAnalyticsAsync(new()
{
    GameId = gameId, Type = "overview_v2",
    StartedAt = new DateOnly(2026, 9, 1), EndedAt = new DateOnly(2026, 9, 30),
    First = 100, After = cursor
}, ct);
```

Both methods support all six documented filters. Dates must be supplied together and are serialized as UTC midnight. The server adjusts the available historical window and recent dates for report processing delay. User tokens require `analytics:read:extensions` or `analytics:read:games`; reports are limited to the user's owned resources. Report objects expose the resource ID, Url, Type and DateRange. The wire field is uppercase `URL`; download URLs expire after five minutes. The SDK returns these URLs without following them or forwarding Twitch credentials to the report host. [Extension Analytics](https://dev.twitch.tv/docs/api/reference/#get-extension-analytics), [Game Analytics](https://dev.twitch.tv/docs/api/reference/#get-game-analytics).

`EnumerateExtensionAnalyticsAsync` and `EnumerateGameAnalyticsAsync` follow cursors when no resource filter is supplied. When ExtensionId/GameId is set, Twitch ignores `after`; enumeration therefore ends after that result. First accepts 1–100, although Twitch documents a maximum of 20 returned report URLs per page.

## Games

```csharp
var games = await helix.Games.GetGamesAsync(new()
{
    Ids = [gameId], Names = ["Just Chatting"], IgdbIds = [igdbId]
}, ct);
var top = await helix.Games.GetTopGamesAsync(new() { First = 50, Before = cursor }, ct);
await foreach (var game in helix.Games.EnumerateTopGamesAsync(new() { First = 100 }, ct))
    Console.WriteLine(game.Name);
```

App and user tokens are accepted without additional scopes. GetGames supports repeated id, name and igdb_id parameters; their combined count must be 1–100. TwitchGame exposes Id, Name, BoxArtUrl and IgdbId, which may be empty. GetTopGames supports first, after and before; enumeration pages forward and rejects Before. [Get Games](https://dev.twitch.tv/docs/api/reference/#get-games), [Get Top Games](https://dev.twitch.tv/docs/api/reference/#get-top-games).

## Search

```csharp
var categories = await helix.Search.SearchCategoriesAsync(
    new() { Query = "#archery", First = 20, After = cursor }, ct);
var channels = await helix.Search.SearchChannelsAsync(
    new() { Query = "twitchdev", LiveOnly = false, First = 20, After = cursor }, ct);
```

Pass ordinary, unencoded search text; the transport encodes it once. Both methods accept app or user tokens without additional scopes. Categories support query, first and after and expose Id, Name and BoxArtUrl. Channels additionally support live_only, preserving an explicit false value. The channel model retains all documented fields, including Tags, deprecated TagIds and an empty StartedAt for offline channels. Both have Enumerate methods with the same filters. [Search Categories](https://dev.twitch.tv/docs/api/reference/#search-categories), [Search Channels](https://dev.twitch.tv/docs/api/reference/#search-channels).

## Goals and raids

```csharp
var goals = await helix.Goals.GetCreatorGoalsAsync(broadcasterId, ct);
var pendingRaid = await helix.Raids.StartRaidAsync(broadcasterId, targetBroadcasterId, ct);
await helix.Raids.CancelRaidAsync(broadcasterId, ct);
```

Goals require a user token with `channel:read:goals` and a matching broadcaster ID. Results include Id, BroadcasterId/Login/Name, Type, Description, CurrentAmount, TargetAmount and CreatedAt. Type remains a string so new Twitch goal types are preserved. [Get Creator Goals](https://dev.twitch.tv/docs/api/reference/#get-creator-goals).

Raids require a user token with `channel:manage:raids` for the initiating broadcaster. Start sends from_broadcaster_id and to_broadcaster_id in the query; cancel uses broadcaster_id and handles an empty success response. The start response describes a pending raid, not proof it happened: observe the channel.raid event for confirmation. Twitch controls the countdown and per-channel rate limit. The returned IsMature field is deprecated. [Start a Raid](https://dev.twitch.tv/docs/api/reference/#start-a-raid), [Cancel a Raid](https://dev.twitch.tv/docs/api/reference/#cancel-a-raid).

## Errors and retry behavior

Handle TwitchApiException for invalid/expired credentials, missing grants, unavailable resources, state restrictions and rate limits. HTTP status and Twitch's structured error/message remain available. Only rejected 429 operations are eligible for bounded automatic retry; ambiguous mutation failures are not replayed. Safe reads may retry transient server errors. Cancellation interrupts HTTP and retry waits. A successful response with an empty Data list is valid where Twitch documents no matching resources.
