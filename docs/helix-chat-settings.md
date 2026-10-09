# Chat settings, announcements, Shoutouts, pins and colors

These eleven endpoints are available through `helix.Chat`, each with a final `CancellationToken`. The SDK checks known token metadata before sending: token kind, the user that must own the token, and the documented scopes. App grants (`user:bot`, `channel:bot`), moderator status and channel state remain server-authoritative. Wherever an endpoint takes `moderator_id`, a user token must belong to that moderator (or the broadcaster).

| Endpoint | Method | Token | User scopes |
| --- | --- | --- | --- |
| [Get Chat Settings](https://dev.twitch.tv/docs/api/reference/#get-chat-settings) | `GetChatSettingsAsync` | app or user; user only with a moderator ID | none |
| [Update Chat Settings](https://dev.twitch.tv/docs/api/reference/#update-chat-settings) | `UpdateChatSettingsAsync` | user or app | `moderator:manage:chat_settings` |
| [Send Chat Announcement](https://dev.twitch.tv/docs/api/reference/#send-chat-announcement) | `SendChatAnnouncementAsync` | user or app; app only with `ForSourceOnly` | `moderator:manage:announcements` |
| [Send a Shoutout](https://dev.twitch.tv/docs/api/reference/#send-a-shoutout) | `SendShoutoutAsync` | user or app | `moderator:manage:shoutouts` |
| [Get Pinned Chat Message](https://dev.twitch.tv/docs/api/reference/#get-pinned-chat-message) | `GetPinnedChatMessageAsync` | user or app | `moderator:read:chat_messages` **or** `moderator:manage:chat_messages` |
| [Pin Chat Message](https://dev.twitch.tv/docs/api/reference/#pin-chat-message) | `PinChatMessageAsync` | user or app | `moderator:manage:chat_messages` |
| [Update Pinned Chat Message](https://dev.twitch.tv/docs/api/reference/#update-pinned-chat-message) | `UpdatePinnedChatMessageAsync` | user or app | `moderator:manage:chat_messages` |
| [Unpin Chat Message](https://dev.twitch.tv/docs/api/reference/#unpin-chat-message) | `UnpinChatMessageAsync` | user or app | `moderator:manage:chat_messages` |
| [Get User Chat Color](https://dev.twitch.tv/docs/api/reference/#get-user-chat-color) | `GetUserChatColorsAsync` | app or user | none |
| [Update User Chat Color](https://dev.twitch.tv/docs/api/reference/#update-user-chat-color) | `UpdateUserChatColorAsync` | user (`user_id` owner) | `user:manage:chat_color` |
| [Get Shared Chat Session](https://dev.twitch.tv/docs/api/reference/#get-shared-chat-session) | `GetSharedChatSessionAsync` | app or user | none |

App tokens for announcements, Shoutouts and pins additionally need the moderator's `user:bot` grant with the listed scope and the broadcaster's `channel:bot` grant; Update Chat Settings needs the moderator's `moderator:manage:chat_settings` grant. Twitch checks those grants.

## Chat settings

```csharp
// App token or any user token: public settings only.
var settings = (await helix.Chat.GetChatSettingsAsync("123", cancellationToken: cancellationToken)).Data.Single();

// Moderator token with moderator:read:chat_settings: adds ModeratorId and the non-moderator delay.
var moderated = (await helix.Chat.GetChatSettingsAsync("123", "456", cancellationToken)).Data.Single();
Console.WriteLine($"{moderated.NonModeratorChatDelay} {moderated.NonModeratorChatDelayDuration}");

var updated = await helix.Chat.UpdateChatSettingsAsync("123", "456", new()
{
    SlowMode = true, SlowModeWaitTime = 10,   // change a value: send the mode and its value
    FollowerMode = false,                     // false clears the duration
    NonModeratorChatDelay = true, NonModeratorChatDelayDuration = 2,
    EmoteMode = false
}, cancellationToken);
```

Responses contain one object with the mode flags and their durations; a duration is null while its mode is off. `ModeratorId` is present only for user tokens with `moderator:read:chat_settings`, and Get Chat Settings returns `NonModeratorChatDelay`/`NonModeratorChatDelayDuration` only for such tokens when the supplied moderator ID belongs to one of the broadcaster's moderators. Those properties are therefore nullable. A moderator ID must match the user token, so the SDK rejects it with an app token.

Updates send only non-null fields in the PATCH body; false and zero are kept. A duration may only be sent together with its mode set to `true` in the same request; setting a mode to `false` clears its duration. Sending `SlowMode = true` or `FollowerMode = true` without a value applies Twitch's default (30 seconds, 0 minutes). Allowed values: `FollowerModeDuration` 0–129600 minutes, `SlowModeWaitTime` 3–120 seconds, `NonModeratorChatDelayDuration` 2, 4 or 6 seconds. The delay has no documented default, so enabling it requires a duration. Violations throw `ArgumentException`/`ArgumentOutOfRangeException` before any request. Twitch returns 403 when the moderator is not a moderator of the channel.

## Announcements

```csharp
await helix.Chat.SendChatAnnouncementAsync("123", "456", new()
{
    Message = "Stream starts in 5 minutes!", Color = "purple"
}, cancellationToken);

// App token in a shared chat session: send to every participating channel.
await helix.Chat.SendChatAnnouncementAsync("123", "456", new()
{
    Message = "Hello everyone", ForSourceOnly = false
}, cancellationToken);
```

The IDs are query parameters; the JSON body contains `message`, plus `color` and `for_source_only` when set. Messages must be non-blank and at most 500 Unicode code points; Twitch would silently truncate longer text, so the SDK rejects it. Colors are case-sensitive: `blue`, `green`, `orange`, `purple` or `primary` (the default channel accent color). With a user token, announcements always reach every channel of a shared chat session and `ForSourceOnly` is rejected locally (Twitch answers 400). With an app token Twitch defaults to the source channel only; `ForSourceOnly = false` is serialized explicitly. Twitch allows one announcement every 2 seconds and returns 429 otherwise; messages that fail Twitch's review return 400.

## Shoutouts

```csharp
await helix.Chat.SendShoutoutAsync(fromBroadcasterId: "123", toBroadcasterId: "789", moderatorId: "456", cancellationToken);
```

All three IDs are query parameters of a bodyless POST that completes with 204. A broadcaster may not shout out themselves; the SDK rejects equal IDs. Twitch requires the sender to be live with at least one viewer (400), returns 403 for non-moderators or when the receiver may not get a Shoutout from this broadcaster, and enforces one Shoutout every 2 minutes and one per receiver every 60 minutes (429). Subscribe to `channel.shoutout.create` to learn the cooldown end times.

## Pinned messages

```csharp
var pin = (await helix.Chat.GetPinnedChatMessageAsync("123", "456", cancellationToken)).Data.SingleOrDefault();
if (pin is not null)
    foreach (var fragment in pin.Message.Fragments)
        Console.WriteLine($"{fragment.Type}: {fragment.Text} {fragment.Emote?.Id} {fragment.Cheermote?.Bits} {fragment.Mention?.UserLogin}");

await helix.Chat.PinChatMessageAsync("123", "456", "message-id", durationSeconds: 300, cancellationToken);
await helix.Chat.UpdatePinnedChatMessageAsync("123", "456", "message-id", durationSeconds: 600, cancellationToken);
await helix.Chat.UpdatePinnedChatMessageAsync("123", "456", "message-id", cancellationToken: cancellationToken); // until stream end
await helix.Chat.UnpinChatMessageAsync("123", "456", "message-id", cancellationToken);
```

Only one mod-pinned message is active per channel; pinning another replaces it. `Data` is empty when nothing is pinned. A pin contains the message and sender, who pinned it, `StartsAt`, `UpdatedAt` and `EndsAt` (null when pinned until the stream ends). Fragments have a `Type` (`text`, `emote`, `cheermote`, `mention`) and text; the matching `Cheermote` (prefix, bits, tier), `Emote` (ID, set ID, owner ID, formats) or `Mention` (user ID, login, name) object is set, the others are null.

Pin, update and unpin send only query parameters and complete with 204. `durationSeconds` accepts 30–1800; null pins until the stream ends, and on update restarts the remaining time from now. Twitch errors: 403 without moderator permission, 404 for an unknown message or pin, 409 when the message is already pinned, 429 when the pin rate limit is exceeded.

## User chat colors

```csharp
var colors = await helix.Chat.GetUserChatColorsAsync(["11111", "44444"], cancellationToken);
foreach (var user in colors.Data)
    Console.WriteLine(user.Color.Length == 0 ? $"{user.UserName}: default" : $"{user.UserName}: {user.Color}");

await helix.Chat.UpdateUserChatColorAsync("11111", "blue_violet", cancellationToken);
await helix.Chat.UpdateUserChatColorAsync("11111", "#9146FF", cancellationToken); // Turbo or Prime only
```

Reading accepts 1–100 user IDs as repeated `user_id` parameters; Twitch ignores duplicates and unknown IDs. `Color` is an empty string when the user never chose one. Updating requires the user's own token with `user:manage:chat_color` and sends `user_id` and `color` as query parameters (the `#` is encoded as `%23`). Every user may choose `blue`, `blue_violet`, `cadet_blue`, `chocolate`, `coral`, `dodger_blue`, `firebrick`, `golden_rod`, `green`, `hot_pink`, `orange_red`, `red`, `sea_green`, `spring_green` or `yellow_green`; Turbo and Prime users may also use `#RRGGBB`. Other values are rejected locally; Twitch returns 400 when a non-Turbo/Prime user sends a Hex code.

## Shared chat sessions

```csharp
var session = (await helix.Chat.GetSharedChatSessionAsync("123", cancellationToken)).Data.SingleOrDefault();
if (session is not null)
    Console.WriteLine($"{session.SessionId} hosted by {session.HostBroadcasterId}: {string.Join(", ", session.Participants.Select(p => p.BroadcasterId))}");
```

`Data` is empty when the broadcaster is not in a shared chat session. A session has its ID, host broadcaster, participant broadcaster IDs and creation/update timestamps.

## Errors and tests

Twitch errors surface as `TwitchApiException` with status, error and message. A 429 is retried only within the transport's bounded rate-limit retry budget and then surfaces the same way; mutations are never retried after ambiguous 5xx or network failures. `ChatSettingsTests` covers every documented response field (round-trip contracts against `docs/api/coverage.json`), exact methods, paths, query strings and JSON bodies (nulls omitted, false/zero kept), Hex color encoding, all local validations, token kind/identity/scope preflight without HTTP calls, and the documented 400/403/404/409/429 errors. Fixtures are synthetic contract cases based on the official examples; live integration remains required before stable publication.

Sources: the pinned official API reference sections linked in the table above.
