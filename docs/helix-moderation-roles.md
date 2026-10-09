# Moderation roles and safety

Twelve endpoints for moderator/VIP roles, Shield Mode, warnings and suspicious-user status are available through `helix.Moderation`, each with a final `CancellationToken`. The SDK checks known token metadata before sending; Twitch remains authoritative for roles, channel ownership, app grants and eligibility. Twitch errors surface as `TwitchApiException` with `StatusCode`, `Error` and `Message`.

## Moderated channels, moderators and VIPs

```csharp
var mine = await helix.Moderation.GetModeratedChannelsAsync(new() { UserId = "42", First = 100 }, cancellationToken);
await foreach (var channel in helix.Moderation.EnumerateModeratedChannelsAsync(new() { UserId = "42" }, cancellationToken))
    Console.WriteLine(channel.BroadcasterLogin);

var mods = await helix.Moderation.GetModeratorsAsync(new() { BroadcasterId = "123", UserIds = ["456", "789"] }, cancellationToken);
await foreach (var vip in helix.Moderation.EnumerateVipsAsync(new() { BroadcasterId = "123" }, cancellationToken))
    Console.WriteLine($"{vip.UserName} ({vip.UserId})");

await helix.Moderation.AddChannelModeratorAsync("123", "456", cancellationToken);
await helix.Moderation.RemoveChannelModeratorAsync("123", "456", cancellationToken);
await helix.Moderation.AddChannelVipAsync("123", "789", cancellationToken);
await helix.Moderation.RemoveChannelVipAsync("123", "789", cancellationToken);
```

| Method | Authorization checked locally |
| --- | --- |
| [`GetModeratedChannelsAsync`](https://dev.twitch.tv/docs/api/reference/#get-moderated-channels) | `user:read:moderated_channels`; user token for `UserId`, or an app token the user authorized with that scope |
| [`GetModeratorsAsync`](https://dev.twitch.tv/docs/api/reference/#get-moderators) | Broadcaster user token with `moderation:read` or `channel:manage:moderators` |
| [`AddChannelModeratorAsync`](https://dev.twitch.tv/docs/api/reference/#add-channel-moderator), [`RemoveChannelModeratorAsync`](https://dev.twitch.tv/docs/api/reference/#remove-channel-moderator) | Broadcaster user token with `channel:manage:moderators` |
| [`GetVipsAsync`](https://dev.twitch.tv/docs/api/reference/#get-vips) | Broadcaster user token with `channel:read:vips` or `channel:manage:vips` |
| [`AddChannelVipAsync`](https://dev.twitch.tv/docs/api/reference/#add-channel-vip) | Broadcaster user token with `channel:manage:vips` |
| [`RemoveChannelVipAsync`](https://dev.twitch.tv/docs/api/reference/#remove-channel-vip) | User token with `channel:manage:vips`, from the broadcaster or from the VIP removing their own status. Since either ID may match, only the scope is checked locally |

List requests are records with `First` (1–100, Twitch default 20) and `After`; enumerators follow cursors and snapshot the `UserIds` filter. `GetModeratorsAsync` and `GetVipsAsync` accept up to 100 `UserIds`, sent as repeated `user_id` parameters; moderators come back in the requested order, and IDs that are not moderators/VIPs are omitted. All response fields are non-null strings.

Role changes send only query parameters and return HTTP 204. Twitch allows the broadcaster 10 additions and 10 removals within a 10-second window per role list; exceeding it yields 429, which the shared transport retries only within `MaxRateLimitRetries`. Documented failures:

- Add moderator: 400 (unknown IDs, already a moderator, user banned), 422 (user is a VIP; remove the VIP role first).
- Remove moderator: 400 (unknown IDs or user is not a moderator).
- Add VIP: 400 (user blocked, invalid IDs), 404 (unknown IDs), 409 (no VIP slots available), 422 (user is a moderator or already a VIP), 425 (broadcaster has not completed Build a Community).
- Remove VIP: 400 (invalid IDs), 403 (caller may not remove that VIP), 404 (unknown IDs), 422 (user is not a VIP).

## Shield Mode

```csharp
var updated = await helix.Moderation.UpdateShieldModeStatusAsync("123", "42", isActive: true, cancellationToken);
var status = (await helix.Moderation.GetShieldModeStatusAsync("123", "42", cancellationToken)).Data.Single();
if (status.LastActivatedAt is null) Console.WriteLine("Shield Mode was never activated.");
```

[Update](https://dev.twitch.tv/docs/api/reference/#update-shield-mode-status) sends PUT with body `{"is_active": true|false}` (false is serialized explicitly) and requires `moderator:manage:shield_mode`. [Get](https://dev.twitch.tv/docs/api/reference/#get-shield-mode-status) accepts `moderator:read:shield_mode` or `moderator:manage:shield_mode`. Both use a user token whose ID equals `moderatorId` (the broadcaster or one of their moderators), or an app token the moderator authorized with the scope. A 403 means the user is not one of the broadcaster's moderators.

`ShieldModeStatus` contains `IsActive` and the moderator who last activated it. When Shield Mode was never activated Twitch sends empty strings: `ModeratorId`, `ModeratorLogin` and `ModeratorName` stay `""`, and `LastActivatedAt` becomes `null` (empty string or JSON null). Subscribe to `channel.shield_mode.begin`/`end` for change notifications.

## Warnings

```csharp
var warning = await helix.Moderation.WarnChatUserAsync("123", "42",
    new() { UserId = "9876", Reason = "Please keep chat friendly." }, cancellationToken);
```

[Warn Chat User](https://dev.twitch.tv/docs/api/reference/#warn-chat-user) blocks the user from chat interaction until they acknowledge the warning; a new warning replaces an existing one. The body is wrapped as `{"data":{"user_id":"…","reason":"…"}}`. The reason is required and limited to 500 characters (validated locally as Unicode code points). It requires `moderator:manage:warnings` with a user token for `moderatorId`, or an app token the moderator authorized. Errors: 400 (missing fields, reason too long, user cannot be warned), 403 (not a moderator), 409 (another warning-state update is in progress; retry later), 429 (per-broadcaster per-minute app limit). POST requests are never retried after ambiguous 5xx failures.

## Suspicious users

```csharp
var flagged = await helix.Moderation.AddSuspiciousStatusToChatUserAsync("123", "42",
    new() { UserId = "9876", Status = "RESTRICTED" }, cancellationToken);
var cleared = await helix.Moderation.RemoveSuspiciousStatusFromChatUserAsync("123", "42", "9876", cancellationToken);
```

[Add](https://dev.twitch.tv/docs/api/reference/#add-suspicious-status-to-chat-user) posts `{"user_id","status"}` where status is `ACTIVE_MONITORING` or `RESTRICTED` (other values are rejected locally). [Remove](https://dev.twitch.tv/docs/api/reference/#remove-suspicious-status-from-chat-user) is a DELETE with `user_id` in the query and returns the status `NO_TREATMENT`. Both accept a user or app token with `moderator:manage:suspicious_users`. The reference does not state that `moderator_id` must match the token user, so the SDK leaves that check to Twitch. `SuspiciousChatUserStatus` contains the user, broadcaster and moderator IDs, `UpdatedAt`, `Status` and `Types` (for example `MANUALLY_ADDED`, `DETECTED_BAN_EVADER`, `DETECTED_SUS_CHATTER`, `BANNED_IN_SHARED_CHANNEL`); status and types remain strings so new values do not break deserialization. Errors: 400 (invalid or disallowed status update), 403 (not a moderator).

## Testing

`ModerationRolesTests` round-trips every documented response field for all eight response-bearing endpoints, including 9-digit fractional timestamps. HTTP tests assert methods, paths, exact query strings and JSON bodies, empty-string Shield Mode values, the 100-ID and page-size limits, enumerator cursors and filter snapshots, user/app token preflight rules (including VIP self-removal), argument validation, and status/message preservation for the documented 400/403/404/409/422/425/429 responses. Fixtures are synthetic contract cases; live integration remains required before stable publication.
