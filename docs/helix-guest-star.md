# Guest Star (public beta)

> **Beta:** every Guest Star endpoint is marked BETA in the pinned official API reference. Twitch may change fields, values and behavior without a Helix version bump. Their availability classification is `public-beta`; build in tolerance for change and verify against a live channel before relying on them.

All twelve endpoints are available through `helix.GuestStar`, each with a final `CancellationToken`. They require a user access token; app tokens are rejected before HTTP. All IDs are query parameters, including for mutations. Only Update Channel Guest Star Settings sends a JSON body. Layouts, invite statuses and slot IDs remain strings so values added during the beta are preserved.

## Authorization

| Endpoint | Token user must match | Scopes (any one of) |
| --- | --- | --- |
| [Get Channel Guest Star Settings](https://dev.twitch.tv/docs/api/reference/#get-channel-guest-star-settings), [Get Guest Star Session](https://dev.twitch.tv/docs/api/reference/#get-guest-star-session), [Get Guest Star Invites](https://dev.twitch.tv/docs/api/reference/#get-guest-star-invites) | `moderator_id` | broadcaster: `channel:read:guest_star` or `channel:manage:guest_star`; moderator: `moderator:read:guest_star` or `moderator:manage:guest_star` |
| [Send](https://dev.twitch.tv/docs/api/reference/#send-guest-star-invite)/[Delete Guest Star Invite](https://dev.twitch.tv/docs/api/reference/#delete-guest-star-invite), [Assign](https://dev.twitch.tv/docs/api/reference/#assign-guest-star-slot)/[Update](https://dev.twitch.tv/docs/api/reference/#update-guest-star-slot)/[Delete Guest Star Slot](https://dev.twitch.tv/docs/api/reference/#delete-guest-star-slot), [Update Guest Star Slot Settings](https://dev.twitch.tv/docs/api/reference/#update-guest-star-slot-settings) | `moderator_id` | broadcaster: `channel:manage:guest_star`; moderator: `moderator:manage:guest_star` |
| [Update Channel Guest Star Settings](https://dev.twitch.tv/docs/api/reference/#update-channel-guest-star-settings), [Create](https://dev.twitch.tv/docs/api/reference/#create-guest-star-session)/[End Guest Star Session](https://dev.twitch.tv/docs/api/reference/#end-guest-star-session) | `broadcaster_id` | `channel:manage:guest_star` (required) |

`moderator_id` is the broadcaster's own ID or the ID of one of the broadcaster's moderators. When it equals `broadcaster_id` the request counts as the broadcaster's and needs a `channel:*` scope; otherwise it needs a `moderator:*` scope. The SDK checks known token kind, user ID and scopes and throws `TwitchAuthorizationException` without sending a request; `RequiredAnyOfScopes` lists the alternatives. Whether the user really moderates the channel or is a Guest Star moderator is decided by Twitch. For Get Guest Star Session the reference also states that guests must be invited or assigned a slot; the SDK leaves that to Twitch.

## Channel settings

```csharp
var settings = (await helix.GuestStar.GetChannelGuestStarSettingsAsync("123", moderatorId: "123", cancellationToken)).Data.Single();
Console.WriteLine($"{settings.SlotCount} slots, layout {settings.GroupLayout}");

await helix.GuestStar.UpdateChannelGuestStarSettingsAsync("123", new()
{
    SlotCount = 4, GroupLayout = "SCREENSHARE",
    IsModeratorSendLiveEnabled = false, IsBrowserSourceAudioEnabled = true
}, cancellationToken);
```

Settings expose `IsModeratorSendLiveEnabled`, `SlotCount`, `IsBrowserSourceAudioEnabled`, `GroupLayout` (`TILED` or `SCREENSHARE`; Twitch reports other layouts as `TILED`) and `BrowserSourceToken`. The browser-source token is a view-only credential for browser-source URLs; do not log it. It is null unless `moderator_id` is the broadcaster. The update is a PATCH whose body contains only the fields you set: null omits a field, false is sent explicitly. Set at least one field, or `RegenerateBrowserSources = true`. `SlotCount` must be 1–6 and `GroupLayout` `TILED` or `SCREENSHARE`. These rules are validated locally, so a layout Twitch adds during the beta needs an SDK update before it can be sent (responses already keep unknown layouts). Twitch no longer documents the former `TILED_LAYOUT`, `SCREENSHARE_LAYOUT`, `HORIZONTAL_LAYOUT` and `VERTICAL_LAYOUT` values, and the SDK rejects them. `RegenerateBrowserSources = true` immediately invalidates every browser source already configured in streaming software. Success is HTTP 204.

## Sessions

```csharp
var session = (await helix.GuestStar.CreateGuestStarSessionAsync("123", cancellationToken)).Data.Single();
var current = await helix.GuestStar.GetGuestStarSessionAsync("123", moderatorId: "456", cancellationToken);
foreach (var guest in current.Data.SelectMany(s => s.Guests))
    Console.WriteLine($"{guest.SlotId}: {guest.UserId} live={guest.IsLive} volume={guest.Volume} mic={guest.Audio.IsGuestEnabled}");
var final = await helix.GuestStar.EndGuestStarSessionAsync("123", session.Id, cancellationToken);
```

Creation requires the broadcaster to join the call interface: the session ends automatically when nobody in it is connected. If guests stay connected without the broadcaster, the broadcaster loses their slot and the session continues until the last participant leaves. A new session contains the broadcaster as its only guest. Only one active session is allowed. End performs the host's "End Call" action and returns the final session state. Get Guest Star Session returns an empty `Data` list when there is no session, or when `moderator_id` is not the broadcaster, a guest or invited user in the session, or a Guest Star moderator. Each guest has `SlotId` (host `"0"`, guests `"1"`, `"2"` …, screen share `"SCREENSHARE"`, matching browser-source links; when `moderator_id` holds a guest slot, slots are numbered from that guest's view with the guest in `"0"`), `IsLive`, `UserId`, `Volume` (0–200, default 100), `AssignedAt` and `Audio`/`Video` with `IsHostEnabled`, `IsGuestEnabled` and `IsAvailable`. Twitch renamed `audio_settings`/`video_settings` to `audio`/`video` and dropped `user_display_name`/`user_login`: the SDK reads both names into `Audio`/`Video`, and the obsolete `AudioSettings`, `VideoSettings`, `UserDisplayName` and `UserLogin` stay readable (the last two are empty unless Twitch sends them; use Get Users). Create and End are mutations and are never retried after ambiguous server or network failures.

## Invites and slots

```csharp
await helix.GuestStar.SendGuestStarInviteAsync(new()
{
    BroadcasterId = "123", ModeratorId = "456", SessionId = session.Id, GuestId = "789"
}, cancellationToken);
var invites = await helix.GuestStar.GetGuestStarInvitesAsync("123", "456", session.Id, cancellationToken);
if (invites.Data.Any(i => i.UserId == "789" && i.Status == "READY"))
    await helix.GuestStar.AssignGuestStarSlotAsync(new()
    {
        BroadcasterId = "123", ModeratorId = "456", SessionId = session.Id, GuestId = "789", SlotId = "1"
    }, cancellationToken);

await helix.GuestStar.UpdateGuestStarSlotSettingsAsync(new()
{
    BroadcasterId = "123", ModeratorId = "456", SessionId = session.Id, SlotId = "1",
    IsLive = true, IsAudioEnabled = false
}, cancellationToken);
await helix.GuestStar.UpdateGuestStarSlotAsync(new()
{
    BroadcasterId = "123", ModeratorId = "456", SessionId = session.Id, SourceSlotId = "1", DestinationSlotId = "2"
}, cancellationToken);
await helix.GuestStar.DeleteGuestStarSlotAsync(new()
{
    BroadcasterId = "123", ModeratorId = "456", SessionId = session.Id, GuestId = "789", SlotId = "2", ShouldReinviteGuest = true
}, cancellationToken);
await helix.GuestStar.DeleteGuestStarInviteAsync(new()
{
    BroadcasterId = "123", ModeratorId = "456", SessionId = session.Id, GuestId = "789"
}, cancellationToken);
```

Invites report `UserId`, `InvitedAt`, `Status` (`INVITED`, `ACCEPTED`, `READY`) and the invitee's `IsVideoEnabled`, `IsAudioEnabled`, `IsVideoAvailable` and `IsAudioAvailable` waiting-room flags. A guest can be assigned only after an invite and a ready signal. Assignment slot IDs must be numeric guest slots `"1"` to the session's slot count; `"0"` (host), leading zeros and non-numeric values are rejected locally, the upper bound is checked by Twitch. Update Slot moves an assignment; if the destination is occupied, the two guests swap. Omitting `DestinationSlotId` omits the parameter. Delete Slot revokes the guest's session media access immediately; `ShouldReinviteGuest = true` sends them back to the invite queue. HTTP 404 means the guest has no slot in the session.

Slot settings are query parameters too. Specify at least one of `IsAudioEnabled`, `IsVideoEnabled` and `IsLive` (checked locally). Null omits a setting; false is sent. A moderator can't set `IsLive = true` while the broadcaster's `IsModeratorSendLiveEnabled` setting is false (HTTP 403). Twitch no longer documents `volume`; `Volume` is obsolete, still validated (0–100) and forwarded, and no longer counts as a setting on its own. `IsAudioEnabled = false` mutes the slot in every view; `true` lets the guest unmute but does not unmute them. `IsLive` controls visibility in broadcasting-software integrations. Invites, slot and slot-settings mutations return HTTP 204 without a body.

## Errors

Twitch failures surface as `TwitchApiException` with status, `Error` and `Message`. Documented cases: 400 for missing or invalid IDs, invalid `slot_count`/`group_layout`, a reached session limit or an already ended session; 401 for missing phone verification (create), a `moderator_id`/token mismatch (get session) or a non-Guest-Star moderator (assign); 403 for insufficient authorization, unauthorized or already invited guests, the host slot, uninvited/already assigned/not ready guests and restricted slots; 404 for an unknown session (invites), a missing invite or an unknown guest/slot (delete slot); 409 when the broadcaster is already in another session. On HTTP 401 the transport tries one token refresh; an unchanged token is not resent.

## Documentation inconsistencies

The SDK follows the field tables. The reference before November 2026 named `group_layout` as `layout` in the settings example and the guest slot `id` in the Create/End session examples, so `GroupLayout` and `SlotId` stay nullable; the current reference uses `group_layout` and `slot_id` throughout and documents the `data` wrapper. The Markdown copy of the reference doesn't show the Beta badges; the HTML pages still do. Get Guest Star Invites says `broadcaster_id` must match the token while its parameter table (and the moderator scopes) says `moderator_id`; the SDK checks `moderator_id`. `should_reinvite_guest` is a Boolean and is sent as `true`/`false`. The invite flags `is_video_enabled`/`is_audio_enabled` are described as "has chosen to disable". Assign Slot lists only 204 success but its example shows a `{"data":{"code":"USER_NOT_READY"}}` body, which the SDK does not model.

## Tests

`GuestStarTests` cover every documented response field for all five responses through source-generated JSON round trips, exact methods, paths and query strings for all twelve endpoints, the exact update JSON body including false values, query-only mutations with explicit false/zero and omitted optional values, shapes from the older reference (legacy names), app-token rejection, broadcaster-versus-moderator identity checks, the channel-versus-moderator scope split, read-only scopes for mutations, local validation boundaries and structured 400/401/403/404/409 errors. Fixtures are synthetic contract cases; credentialed integration against a live Guest Star channel remains required, especially while the API is in beta.

Sources: pinned official [Guest Star reference](https://dev.twitch.tv/docs/api/reference/#get-channel-guest-star-settings) (entries linked above).
