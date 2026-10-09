# Stream tags, content labels, user authorization and Power-ups

These endpoints follow their official reference categories: `helix.Tags` (Tags), `helix.ContentClassification` (CCLs), `helix.Users` (Users) and `helix.Bits` (Bits). All methods accept a final `CancellationToken`.

## Content classification labels

```csharp
var labels = await helix.ContentClassification.GetContentClassificationLabelsAsync("de-DE", cancellationToken);
foreach (var label in labels.Data)
    Console.WriteLine($"{label.Id} {label.Name}: {label.Description}");
```

Accepts an app or user token without scopes. `locale` is optional (one value; Twitch defaults to `en-US`); `Name` and `Description` are localized, `Id` is stable and is the value used for `ContentClassificationLabels` in Modify Channel Information. The documented locales are bg-BG, cs-CZ, da-DK, de-DE, el-GR, en-GB, en-US, es-ES, es-MX, fi-FI, fr-FR, hu-HU, it-IT, ja-JP, ko-KR, nl-NL, no-NO, pl-PL, pt-BT (sic, presumably pt-BR), pt-PT, ro-RO, ru-RU, sk-SK, sv-SE, th-TH, tr-TR, vi-VN, zh-CN and zh-TW. The SDK only rejects a blank locale and leaves the list to Twitch, because the documented list contains a typo and may grow.

## Authorization by user

```csharp
// Requires an app access token.
var grants = await helix.Users.GetAuthorizationByUserAsync(["141981764", "197886470"], cancellationToken);
foreach (var user in grants.Data)
    Console.WriteLine($"{user.UserLogin}: {(user.HasAuthorized ? string.Join(' ', user.Scopes) : "not authorized")}");
```

Reports, for 1 to 10 user IDs (repeated `user_id`), whether each user authorized this client ID and which scopes were granted. The SDK rejects user tokens before sending. `Scopes` is empty for users who have not authorized the application.

## Custom Power-ups

```csharp
var powerUps = await helix.Bits.GetCustomPowerUpsAsync(new()
{
    BroadcasterId = "274637212", Ids = ["92af127c-7326-4483-a52b-b0da0be61c02"]
}, cancellationToken);
foreach (var powerUp in powerUps.Data)
    Console.WriteLine($"{powerUp.Title}: {powerUp.Bits} Bits, in stock {powerUp.IsInStock}");
```

Requires the broadcaster's user token with `bits:read`; `BroadcasterId` must match the token user. Up to 50 IDs filter the list (duplicates are ignored); without IDs all custom Power-ups are returned in ascending ID order (a channel has at most 50). If none of the requested IDs exist, Twitch returns 404; 403 means the broadcaster is not a partner or affiliate. Both are raised as `TwitchApiException`. The model contains broadcaster identity, title, prompt, `Bits` (64-bit), the nullable custom `Image` and the `DefaultImage` (`Url1x`/`Url2x`/`Url4x`), background color, enabled/input/paused/stock flags, per-stream, per-user and cooldown settings (sharing the custom reward setting types), the nullable `RedemptionsRedeemedCurrentStream` and the nullable `CooldownExpiresAt`.

## Deprecated stream tags

```csharp
#pragma warning disable CS0618 // Twitch-defined stream tags are deprecated.
var all = await helix.Tags.GetAllStreamTagsAsync(new() { TagIds = ["621fb5bf-5498-4d8f-b4ac-db4d40d401bf"], First = 100 }, cancellationToken);
await foreach (var tag in helix.Tags.EnumerateAllStreamTagsAsync(cancellationToken: cancellationToken))
    Console.WriteLine(tag.LocalizationNames.GetValueOrDefault("en-us"));
var channelTags = await helix.Tags.GetStreamTagsAsync("527115020", cancellationToken);
#pragma warning restore CS0618
```

Twitch replaced Twitch-defined tags with channel-defined tags. According to the reference, both endpoints return an empty list since February 28, 2023 and are documented to return HTTP 410 from July 13, 2023; a 410 is raised as `TwitchApiException` with `StatusCode == HttpStatusCode.Gone`. The methods are therefore marked `[Obsolete]` and kept only for completeness. Use the `Tags` of Get Channel Information, Get Streams or Search Channels instead.

Get All Stream Tags accepts up to 100 `TagIds` (Twitch ignores invalid IDs but not duplicates, so the SDK sends duplicates unchanged), `First` 1 to 100 (Twitch default 20) and `After`; the enumerator snapshots the IDs. Get Stream Tags requires a broadcaster ID. Both accept app or user tokens and return `StreamTag` with `TagId`, `IsAuto` and the `LocalizationNames`/`LocalizationDescriptions` dictionaries keyed by locales such as `en-us`.

## Tests

`TagsLabelsAuthorizationTests` covers all documented response fields, exact queries, token kinds and identity checks, limits, the enumerator, deprecation attributes and Twitch errors with synthetic fixtures. Live integration remains required before stable publication.

Sources: pinned official [Get All Stream Tags](https://dev.twitch.tv/docs/api/reference/#get-all-stream-tags), [Get Stream Tags](https://dev.twitch.tv/docs/api/reference/#get-stream-tags), [Get Content Classification Labels](https://dev.twitch.tv/docs/api/reference/#get-content-classification-labels), [Get Authorization By User](https://dev.twitch.tv/docs/api/reference/#get-authorization-by-user), [Get Custom Power-up](https://dev.twitch.tv/docs/api/reference/#get-custom-power-up).
