# Moderation: AutoMod, bans, unban requests, blocked terms and chat deletion

These 13 endpoints are available through `helix.Moderation`, each with a final `CancellationToken`. Query IDs (`broadcaster_id`, `moderator_id`) are sent as query parameters; request objects mark them `[JsonIgnore]` so only the documented body is serialized. Null omits an optional field; false, zero and empty strings are sent.

## Authorization

| Endpoint | Method | Token | ID that must match the token user | User scopes |
| --- | --- | --- | --- | --- |
| [Check AutoMod Status](https://dev.twitch.tv/docs/api/reference/#check-automod-status) | `CheckAutoModStatusAsync` | user or app | `BroadcasterId` | `moderation:read` |
| [Manage Held AutoMod Messages](https://dev.twitch.tv/docs/api/reference/#manage-held-automod-messages) | `ManageHeldAutoModMessageAsync` | user or app | `UserId` (the moderator) | `moderator:manage:automod` |
| [Get AutoMod Settings](https://dev.twitch.tv/docs/api/reference/#get-automod-settings) | `GetAutoModSettingsAsync` | user or app | moderator | `moderator:read:automod_settings` or `moderator:manage:automod_settings` |
| [Update AutoMod Settings](https://dev.twitch.tv/docs/api/reference/#update-automod-settings) | `UpdateAutoModSettingsAsync` | user or app | moderator | `moderator:manage:automod_settings` |
| [Get Banned Users](https://dev.twitch.tv/docs/api/reference/#get-banned-users) | `GetBannedUsersAsync`, `EnumerateBannedUsersAsync` | user or app | `BroadcasterId` | `moderation:read` or `moderator:manage:banned_users` |
| [Ban User](https://dev.twitch.tv/docs/api/reference/#ban-user) | `BanUserAsync` | user or app | moderator | `moderator:manage:banned_users` |
| [Unban User](https://dev.twitch.tv/docs/api/reference/#unban-user) | `UnbanUserAsync` | user or app | moderator | `moderator:manage:banned_users` |
| [Get Unban Requests](https://dev.twitch.tv/docs/api/reference/#get-unban-requests) | `GetUnbanRequestsAsync`, `EnumerateUnbanRequestsAsync` | **user only** | moderator | `moderator:read:unban_requests` or `moderator:manage:unban_requests` |
| [Resolve Unban Requests](https://dev.twitch.tv/docs/api/reference/#resolve-unban-requests) | `ResolveUnbanRequestAsync` | **user only** | moderator | `moderator:manage:unban_requests` |
| [Get Blocked Terms](https://dev.twitch.tv/docs/api/reference/#get-blocked-terms) | `GetBlockedTermsAsync`, `EnumerateBlockedTermsAsync` | user or app | moderator | `moderator:read:blocked_terms` or `moderator:manage:blocked_terms` |
| [Add Blocked Term](https://dev.twitch.tv/docs/api/reference/#add-blocked-term) | `AddBlockedTermAsync` | user or app | moderator | `moderator:manage:blocked_terms` |
| [Remove Blocked Term](https://dev.twitch.tv/docs/api/reference/#remove-blocked-term) | `RemoveBlockedTermAsync` | user or app | moderator | `moderator:manage:blocked_terms` |
| [Delete Chat Messages](https://dev.twitch.tv/docs/api/reference/#delete-chat-messages) | `DeleteChatMessageAsync`, `DeleteAllChatMessagesAsync` | user or app | moderator | `moderator:manage:chat_messages` |

"Moderator" means the `ModeratorId` argument: the broadcaster or a user who moderates the channel. Check AutoMod Status and Get Banned Users are broadcaster-only: a moderator's token is rejected locally because `broadcaster_id` must match the token user. App tokens are accepted where Twitch documents that the application holds the scope through a prior authorization of the user named above (Ban/Unban additionally need the `user:bot` grant); the SDK cannot see app grants, so Twitch verifies them. Known user-token scopes, token kind and user ID are checked before any HTTP call (`TwitchAuthorizationException`); the moderator role itself is checked by Twitch (HTTP 403).

## AutoMod

```csharp
var checks = await helix.Moderation.CheckAutoModStatusAsync(new()
{
    BroadcasterId = "123",
    Data = [new() { MsgId = "1", MsgText = "Hello World!" }, new() { MsgId = "2", MsgText = "Boooooo!" }]
}, cancellationToken);
var held = checks.Data.Where(c => !c.IsPermitted).Select(c => c.MsgId);

await helix.Moderation.ManageHeldAutoModMessageAsync(new()
{
    UserId = "456", MsgId = "836013710", Action = "ALLOW"
}, cancellationToken);

// PUT overwrites: read the current settings, change them and send every level you want to keep.
var current = (await helix.Moderation.GetAutoModSettingsAsync("123", "456", cancellationToken)).Data.Single();
await helix.Moderation.UpdateAutoModSettingsAsync(new()
{
    BroadcasterId = "123", ModeratorId = "456",
    Disability = current.Disability, Aggression = current.Aggression, SexualitySexOrGender = current.SexualitySexOrGender,
    Misogyny = current.Misogyny, Bullying = current.Bullying, Swearing = 3,
    RaceEthnicityOrReligion = current.RaceEthnicityOrReligion, SexBasedTerms = current.SexBasedTerms
}, cancellationToken);
```

Check AutoMod Status accepts 1–100 messages, each with a non-blank caller-defined `MsgId` and `MsgText`; the body is the documented `{"data":[...]}` array. `IsPermitted` is false when Twitch would hold or block the message. Twitch limits checks **per channel** by account type (normal 5/min and 50/h, affiliate 10/min and 100/h, partner 30/min and 300/h) on top of the standard limits; the response's rate-limit headers do not reflect these limits. Exceeding them returns HTTP 429. The shared transport retries a 429 up to `MaxRateLimitRetries` times (default 2) using the reset headers or `FallbackRetryDelay`, and pauses the whole transport meanwhile. Those headers describe the standard bucket, so such retries rarely help for this per-channel quota. Budget checks yourself and handle the final `TwitchApiException` with status 429, or use a transport with `MaxRateLimitRetries = 0` for bulk checks.

Held messages accept `ALLOW` or `DENY` (case-sensitive) and return HTTP 204. Twitch returns 403 if `UserId` is not a moderator and 404 for an unknown message.

AutoMod settings return a single object. `OverallLevel` is null when individual levels are configured. Updates use PUT and therefore overwrite: set **either** `OverallLevel` **or** one or more individual levels, never both and never neither (checked locally). All levels must be 0 (no filtering) to 4 (most aggressive). Setting `OverallLevel` applies Twitch's recommended defaults, not necessarily the same value for every category. Updating an individual level while an overall level is active resets `OverallLevel` to null and every unspecified individual level to 0; if the individual levels you send equal an overall preset, Twitch switches back to that overall level.

## Bans and timeouts

```csharp
var timeout = await helix.Moderation.BanUserAsync(new()
{
    BroadcasterId = "123", ModeratorId = "456",
    Data = new() { UserId = "789", Duration = 600, Reason = "Spam" }
}, cancellationToken);
await helix.Moderation.BanUserAsync(new()
{
    BroadcasterId = "123", ModeratorId = "456", Data = new() { UserId = "789" } // no duration: permanent ban
}, cancellationToken);
await helix.Moderation.UnbanUserAsync("123", "456", "789", cancellationToken);

await foreach (var banned in helix.Moderation.EnumerateBannedUsersAsync(new() { BroadcasterId = "123" }, cancellationToken))
    Console.WriteLine($"{banned.UserName}: {(banned.ExpiresAt is null ? "permanent" : $"until {banned.ExpiresAt}")}");
```

Ban User sends the documented wrapper `{"data":{"user_id":...,"duration":...,"reason":...}}`. Omitting `Duration` bans permanently; 1–1209600 seconds (two weeks) creates a timeout, and `Duration = 1` ends an existing timeout early. A timeout can be changed or converted to a ban, but a banned user cannot be moved to a timeout. `Reason` is optional and limited to 500 characters (Unicode code points). The result's `EndTime` is null for permanent bans.

Unban sends all IDs as query parameters and returns HTTP 204. Both operations surface documented failures as `TwitchApiException`: 400 (user already banned / not banned / may not be banned, invalid duration or reason), 403 (not a moderator), **409** while someone else is changing the same user's ban state (not retried by the SDK; retry later) and **429** when the app exceeds its per-broadcaster, per-minute limit (retried only within `MaxRateLimitRetries`).

Get Banned Users supports up to 100 repeated `user_id` filters (results keep the requested order), `First` 1–100 (default 20) and `After`/`Before` cursors (mutually exclusive). The enumerator snapshots the filters, follows `After` and rejects `Before`. `ExpiresAt` is null for permanent bans: Twitch sends an empty string there, which `EmptyStringAsNullDateTimeOffsetConverter` maps to null. `Reason` is an empty string when none was given.

## Unban requests

```csharp
var pending = await helix.Moderation.GetUnbanRequestsAsync(new()
{
    BroadcasterId = "123", ModeratorId = "456", Status = "pending", First = 50
}, cancellationToken);
await helix.Moderation.ResolveUnbanRequestAsync(new()
{
    BroadcasterId = "123", ModeratorId = "456", UnbanRequestId = pending.Data[0].Id,
    Status = "denied", ResolutionText = "Not this time."
}, cancellationToken);
```

Both endpoints require a **user** token; app tokens are rejected locally. Listing requires a status (`pending`, `approved`, `denied`, `acknowledged`, `canceled`) and optionally a requester `UserId`, `After` cursor and `First`. Twitch documents no maximum page size here, so only values below 1 are rejected locally. Resolving sends everything as query parameters (no body): status `approved` or `denied` and an optional `ResolutionText` of at most 500 characters (an empty string is sent). For unresolved requests, `ResolvedAt` is null (an empty string is also mapped to null). `ModeratorId`/`ModeratorLogin`/`ModeratorName` and `ResolutionText` are null or empty; they are kept as strings, so check for both (`string.IsNullOrEmpty`). Twitch returns 400 when the channel does not receive unban requests or the update is invalid, and 404 for an unknown request ID.

## Blocked terms

```csharp
var term = await helix.Moderation.AddBlockedTermAsync(new()
{
    BroadcasterId = "123", ModeratorId = "456", Text = "crac*"
}, cancellationToken);
await foreach (var blocked in helix.Moderation.EnumerateBlockedTermsAsync(new() { BroadcasterId = "123", ModeratorId = "456" }, cancellationToken))
    Console.WriteLine(blocked.Text);
await helix.Moderation.RemoveBlockedTermAsync("123", "456", term.Data.Single().Id, cancellationToken);
```

Terms contain 2–500 characters. A wildcard `*` may appear only at the beginning or end of a word (`*foo`, `foo*`); embedded wildcards such as `f*oo` are rejected locally. Adding an existing term returns the existing entry. The list contains non-private terms, including terms AutoMod denied, newest first; `First` is 1–100 (default 20). `ExpiresAt` is null for manually added or permanently blocked terms; `UpdatedAt` changes while AutoMod keeps denying the term. Removing an unknown ID still returns HTTP 204.

## Chat message deletion

```csharp
await helix.Moderation.DeleteChatMessageAsync("123", "456", "abc-123-def", cancellationToken);
await helix.Moderation.DeleteAllChatMessagesAsync("123", "456", cancellationToken); // clears the chat room
```

Delete Chat Messages is exposed as two methods so that a missing message ID cannot accidentally clear the whole chat: `DeleteChatMessageAsync` requires a non-blank `message_id`, `DeleteAllChatMessagesAsync` omits it. A single message must be younger than 6 hours and must not belong to the broadcaster or another moderator; Twitch returns 400 or 404 otherwise.

## Tests

`ModerationEnforcementTests` cover every documented response field through `helix-moderation-enforcement.json` (round-trip contract checks against the coverage inventory, 9-digit fractional timestamps, nulls for nullable fields), exact HTTP methods, paths, query strings and JSON bodies (zero levels and empty strings kept, nulls omitted, the `data` wrappers), local validation limits, cursor enumeration with filter snapshots, empty-string `expires_at`, broadcaster versus moderator identity checks, app-token acceptance/rejection, alternative read/manage scopes, and structured `TwitchApiException` errors including 404, 409 and 429. Fixtures are synthetic contract cases; live integration remains required before stable publication.
