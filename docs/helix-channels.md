# Channel management and follows

`helix.Channels` exposes channel information, updates, editors and both follow directions. All methods accept a final `CancellationToken`. Reading channel information is also available through the original `helix.GetChannelInformationAsync` facade; both use the same implementation. See [foundation contracts](helix-foundation.md) for the read endpoint.

## Updating channel properties

```csharp
await helix.Channels.ModifyChannelInformationAsync(new()
{
    BroadcasterId = "123",
    GameId = "509658",
    BroadcasterLanguage = "en",
    Title = "Today's stream",
    Delay = 0,
    Tags = ["English", "Creative"],
    ContentClassificationLabels =
    [
        new() { Id = "ProfanityVulgarity", IsEnabled = false }
    ],
    IsBrandedContent = false
}, cancellationToken);
```

This PATCH requires a broadcaster user token with `channel:manage:broadcast`. `BroadcasterId` belongs in the query; the SDK does not write it into the JSON body. At least one update field is required. Null fields are omitted, while explicit false, zero and empty arrays are retained. Use `GameId = ""` or `"0"` to unset the game and `Tags = []` to remove tags. An empty title is invalid; the limit is 140 characters. Delay is 0–900 seconds and Twitch restricts updates to partners. At most 10 tags are allowed, each up to 25 characters with no whitespace; Twitch additionally validates allowed characters, AutoMod, game restrictions and language support.

Content classification labels are explicit enable/disable operations. Clearing labels requires setting every applicable label's `IsEnabled` to false; an empty list does not mean clear all. Documented editable IDs are `DebatedSocialIssuesAndPolitics`, `DrugsIntoxication`, `SexualThemes`, `ViolentGraphic`, `Gambling` and `ProfanityVulgarity`. IDs remain strings for future values. Twitch controls gaming labels and age/region restrictions. Changing branded-content status too frequently can return HTTP 409; it is surfaced as `TwitchApiException` and is not silently retried.

## Editors

```csharp
var editors = await helix.Channels.GetChannelEditorsAsync("123", cancellationToken);
foreach (var editor in editors.Data)
    Console.WriteLine($"{editor.UserName} since {editor.CreatedAt:O}");
```

Requires the broadcaster's user token with `channel:read:editors`. Responses include user ID, display name and the timestamp when editor access was granted. An empty list means no editors; this endpoint is not paginated.

## Followed channels and followers

```csharp
var followed = await helix.Channels.GetFollowedChannelsAsync(new()
{
    UserId = "456", BroadcasterId = "123", First = 20, After = "cursor"
}, cancellationToken);
await foreach (var channel in helix.Channels.EnumerateFollowedChannelsAsync(new()
{
    UserId = "456", First = 100
}, cancellationToken))
    Console.WriteLine(channel.BroadcasterName);

var followers = await helix.Channels.GetChannelFollowersAsync(new()
{
    BroadcasterId = "123", UserId = "456", First = 20, After = "cursor"
}, cancellationToken);
await foreach (var follower in helix.Channels.EnumerateFollowersAsync(new()
{
    BroadcasterId = "123", First = 100
}, cancellationToken))
    Console.WriteLine(follower.UserName);

// A user token without follower scope may still obtain the total; do not pass UserId.
var counts = await helix.Channels.GetChannelFollowersAsync(new()
{
    BroadcasterId = "123"
}, cancellationToken);
Console.WriteLine(counts.Total);
```

`GetFollowedChannelsAsync` requires `user:read:follows` and a user token belonging to `UserId`. Optional `BroadcasterId` checks one follow relationship. The response provides broadcaster ID, login, display name and follow timestamp.

`GetChannelFollowersAsync` requires a user token. Individual follower details require `moderator:read:followers` and a token owner who is the broadcaster or a moderator. Without that scope or channel role, an unfiltered request can still return the total with empty `Data`; empty data therefore does **not** prove the channel has no followers. Setting `UserId` to check a specific follower requires the scope and channel role. Local preflight requires the scope for that filter but lets Twitch check moderator status. It deliberately does not require the token owner to equal the broadcaster, which would reject valid moderators.

Both follow endpoints accept page sizes 1–100 and an `After` cursor, expose `Total` and pagination, and order results from most recently followed to oldest. Totals can change between pages. The enumerators preserve filters and any initial cursor, check cancellation and detect cursor cycles. Use a manual page when you need the total as well as the data.

## Verification

`ChannelsTests` and `helix-channels.json` cover every documented response field, query parameters, PATCH fields including false/empty values, bounds, permissions, owner versus moderator distinctions, total-only responses, pagination and structured failures. Channel updates return no response body for HTTP 204. The source-generated request contract is also exercised through the separately restored NuGet package consumer with reflection disabled. Live credentialed integration is still a release requirement.

Sources: pinned official [Modify Channel Information](https://dev.twitch.tv/docs/api/reference/#modify-channel-information), [Get Channel Editors](https://dev.twitch.tv/docs/api/reference/#get-channel-editors), [Get Followed Channels](https://dev.twitch.tv/docs/api/reference/#get-followed-channels), [Get Channel Followers](https://dev.twitch.tv/docs/api/reference/#get-channel-followers).
