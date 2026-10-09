# Chatters, emotes and chat badges

The seven chat catalog endpoints are available through `helix.Chat`, each with a final `CancellationToken`. Emote and badge reads accept an app or user token without scopes. Get Chatters and Get User Emotes have user-bound authorization, described below. The SDK checks known token metadata before sending; moderator roles, subscriptions and ownership remain server-authoritative.

## Chatters

```csharp
var page = await helix.Chat.GetChattersAsync(new()
{
    BroadcasterId = "123", ModeratorId = "456", First = 1000, After = "cursor"
}, cancellationToken);
Console.WriteLine($"{page.Total} users connected");

await foreach (var chatter in helix.Chat.EnumerateChattersAsync(new()
{
    BroadcasterId = "123", ModeratorId = "456"
}, cancellationToken))
    Console.WriteLine($"{chatter.UserName} ({chatter.UserLogin}, {chatter.UserId})");
```

`ModeratorId` must be the broadcaster or one of their moderators. Twitch documents two token forms: a user token with `moderator:read:chatters` that belongs to `ModeratorId`, or an app token whose application was previously authorized with `moderator:read:chatters` by that user. The SDK rejects a user token for a different user or without the scope; the app grant is checked by Twitch. `First` accepts 1 to 1000 (Twitch default 100), and `After` continues from a cursor. `Total` is the number of connected users and may change while paging; the list is updated with a delay after joins and parts,. Use Get Moderators and Get VIPs for roles. HTTP 403 means `ModeratorId` is not a moderator of the channel.

## Emotes

```csharp
var channel = await helix.Chat.GetChannelEmotesAsync("123", cancellationToken);
var global = await helix.Chat.GetGlobalEmotesAsync(cancellationToken);
var sets = await helix.Chat.GetEmoteSetsAsync(["301590448", "1234"], cancellationToken);

var emote = channel.Data[0];
var url = channel.Template
    .Replace("{{id}}", emote.Id)
    .Replace("{{format}}", emote.Format.Contains("animated") ? "animated" : "static")
    .Replace("{{theme_mode}}", "dark")
    .Replace("{{scale}}", "3.0");

var mine = await helix.Chat.GetUserEmotesAsync(new()
{
    UserId = "456", BroadcasterId = "123", After = "cursor"
}, cancellationToken);
await foreach (var userEmote in helix.Chat.EnumerateUserEmotesAsync(new() { UserId = "456" }, cancellationToken))
    Console.WriteLine($"{userEmote.Name} [{userEmote.EmoteType}]");
```

Channel, global and emote set responses are `ChatEmotesResponse<T>` with `Data` and the CDN URL `Template`. Twitch recommends building URLs from the template with the emote's `Id`, one of its `Format` (`static`, `animated`), `ThemeMode` (`light`, `dark`) and `Scale` (`1.0`, `2.0`, `3.0`) values. `Images` (`Url1x`, `Url2x`, `Url4x`, mapped to the exact `url_1x`/`url_2x`/`url_4x` wire names) always point to static light-theme images.

- Channel emotes expose `Tier` (empty unless `EmoteType` is `subscriptions`), `EmoteType` (`bitstier`, `follower`, `subscriptions`) and `EmoteSetId`. Except for follower emotes, they can be used in any channel.
- Global emotes expose ID, name, images, formats, scales and themes.
- Emote sets require 1 to 25 IDs, sent as repeated `emote_set_id` parameters; Twitch ignores duplicates and returns found sets only. Each emote also has `OwnerId`.
- User emotes require a user token with `user:read:emotes` that belongs to `UserId`. The optional `BroadcasterId` guarantees that broadcaster's follower emotes are included. The page type `UserEmotesResponse` contains `Data`, `Template` and `Pagination`; the enumerator yields only the emotes, so read the template from a page (it is the same template as for the other emote endpoints). `EmoteType` is an evolving string (for example `none`, `bitstier`, `follower`, `subscriptions`, `channelpoints`, `rewards`, `hypetrain`, `prime`, `turbo`, `smilies`, `globals`, `owl2019`, `twofactor`, `limitedtime`). `EmoteSetId` and `OwnerId` are empty strings when the emote has no set or owner. User emotes have no `Images` object.

## Chat badges

```csharp
var channelBadges = await helix.Chat.GetChannelChatBadgesAsync("123", cancellationToken);
var globalBadges = await helix.Chat.GetGlobalChatBadgesAsync(cancellationToken);
foreach (var set in channelBadges.Data)
    foreach (var version in set.Versions)
        Console.WriteLine($"{set.SetId}/{version.Id}: {version.Title} {version.ImageUrl4x} {version.ClickUrl ?? "-"}");
```

Both return badge sets sorted by `SetId` and versions sorted by `Id`; channel badges are empty when the broadcaster has no custom badges. Versions expose `ImageUrl1x`, `ImageUrl2x` and `ImageUrl4x` (18, 36 and 72 px, mapped to `image_url_1x`/`image_url_2x`/`image_url_4x`), `Title`, `Description` and the nullable `ClickAction` and `ClickUrl`.

## Validation and errors

Required IDs must be non-blank; empty cursors and empty filter values are rejected before HTTP. Twitch errors are raised as `TwitchApiException` with status code, error and message (400 for invalid parameters, 401 for token problems, 403 for chatters requested by a non-moderator). `ChatCatalogTests` covers all documented response fields, exact queries, token checks, limits, enumerators and error mapping with synthetic fixtures; live integration is still required before stable publication.

Sources: pinned official [Get Chatters](https://dev.twitch.tv/docs/api/reference/#get-chatters), [Get Channel Emotes](https://dev.twitch.tv/docs/api/reference/#get-channel-emotes), [Get Global Emotes](https://dev.twitch.tv/docs/api/reference/#get-global-emotes), [Get Emote Sets](https://dev.twitch.tv/docs/api/reference/#get-emote-sets), [Get User Emotes](https://dev.twitch.tv/docs/api/reference/#get-user-emotes), [Get Channel Chat Badges](https://dev.twitch.tv/docs/api/reference/#get-channel-chat-badges), [Get Global Chat Badges](https://dev.twitch.tv/docs/api/reference/#get-global-chat-badges).
