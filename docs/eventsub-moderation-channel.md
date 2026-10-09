# EventSub: channel and moderation events

Typed subscriptions for channel updates, follows, ads, raids, bans, unban requests, moderation actions, moderators, VIPs, Shield Mode and Shoutouts. Each row pairs a factory on `EventSubSubscriptions` (condition plus authorization metadata) with a definition on `EventSubEvents` (the same name, used for routing and deserialization) and an event class in `TwitchDock.EventSub.Events`.

| Type@version | Factory / definition | Event class | Authorization (user in the token for WebSockets) | Transports |
|---|---|---|---|---|
| `channel.update@2` | `ChannelUpdateV2(broadcasterUserId)` | `ChannelUpdateEvent` | None | All |
| `channel.follow@2` | `ChannelFollowV2(broadcasterUserId, moderatorUserId)` | `ChannelFollowEvent` | `moderator:read:followers` (moderator) | All |
| `channel.ad_break.begin@1` | `ChannelAdBreakBeginV1(broadcasterUserId)` | `ChannelAdBreakBeginEvent` | `channel:read:ads` (broadcaster) | All |
| `channel.raid@1` | `ChannelRaidV1(from?, to?)`, `ChannelRaidFromBroadcasterV1(from)`, `ChannelRaidToBroadcasterV1(to)` | `ChannelRaidEvent` | None | All |
| `channel.ban@1` | `ChannelBanV1(broadcasterUserId)` | `ChannelBanEvent` | `channel:moderate` (broadcaster) | All |
| `channel.unban@1` | `ChannelUnbanV1(broadcasterUserId)` | `ChannelUnbanEvent` | `channel:moderate` (broadcaster) | All |
| `channel.unban_request.create@1` | `ChannelUnbanRequestCreateV1(broadcasterUserId, moderatorUserId)` | `ChannelUnbanRequestCreateEvent` | `moderator:read:unban_requests` or `moderator:manage:unban_requests` (moderator) | All |
| `channel.unban_request.resolve@1` | `ChannelUnbanRequestResolveV1(broadcasterUserId, moderatorUserId)` | `ChannelUnbanRequestResolveEvent` | `moderator:read:unban_requests` or `moderator:manage:unban_requests` (moderator) | All |
| `channel.moderate@1` | `ChannelModerateV1(broadcasterUserId, moderatorUserId)` | `ChannelModerateEvent` | `moderator:read:moderators`, `moderator:read:vips` and read or manage of `blocked_terms`, `chat_settings`, `unban_requests`, `banned_users`, `chat_messages` (moderator) | All |
| `channel.moderate@2` | `ChannelModerateV2(broadcasterUserId, moderatorUserId)` | `ChannelModerateEventV2` | As v1 plus read or manage of `warnings` (moderator) | All |
| `channel.moderator.add@1` | `ChannelModeratorAddV1(broadcasterUserId)` | `ChannelModeratorAddEvent` | `moderation:read` (broadcaster) | All |
| `channel.moderator.remove@1` | `ChannelModeratorRemoveV1(broadcasterUserId)` | `ChannelModeratorRemoveEvent` | `moderation:read` (broadcaster) | All |
| `channel.vip.add@1` | `ChannelVipAddV1(broadcasterUserId)` | `ChannelVipAddEvent` | `channel:read:vips` or `channel:manage:vips` (broadcaster) | All |
| `channel.vip.remove@1` | `ChannelVipRemoveV1(broadcasterUserId)` | `ChannelVipRemoveEvent` | `channel:read:vips` or `channel:manage:vips` (broadcaster) | All |
| `channel.shield_mode.begin@1` | `ChannelShieldModeBeginV1(broadcasterUserId, moderatorUserId)` | `ChannelShieldModeBeginEvent` | `moderator:read:shield_mode` or `moderator:manage:shield_mode` (moderator) | All |
| `channel.shield_mode.end@1` | `ChannelShieldModeEndV1(broadcasterUserId, moderatorUserId)` | `ChannelShieldModeEndEvent` | `moderator:read:shield_mode` or `moderator:manage:shield_mode` (moderator) | All |
| `channel.shoutout.create@1` | `ChannelShoutoutCreateV1(broadcasterUserId, moderatorUserId)` | `ChannelShoutoutCreateEvent` | `moderator:read:shoutouts` or `moderator:manage:shoutouts` (moderator) | All |
| `channel.shoutout.receive@1` | `ChannelShoutoutReceiveV1(broadcasterUserId, moderatorUserId)` | `ChannelShoutoutReceiveEvent` | `moderator:read:shoutouts` or `moderator:manage:shoutouts` (moderator) | All |

"Moderator" means the `moderator_user_id` condition value, which can be the broadcaster themselves. For WebSocket subscriptions the SDK preflights the token kind, scopes and that user before calling Twitch. Webhook and conduit subscriptions use an app token, and Twitch checks that the user granted the scopes to your client ID. Twitch remains authoritative for roles such as moderator status.

## Notes

- **Raids.** The condition takes exactly one of `from_broadcaster_user_id` (raids the broadcaster starts) or `to_broadcaster_user_id` (raids they receive). `ChannelRaidV1` throws `ArgumentException` when neither or both are set; the `From`/`To` factories make the direction explicit. Subscribe twice to see both directions.
- **Bans and timeouts.** `channel.ban` fires for both. `ChannelBanEvent.IsPermanent` distinguishes them and `EndsAt` is null for permanent bans (an empty string on the wire is also read as null).
- **channel.moderate.** `Action` is the discriminator and is kept as a string so new actions do not break deserialization. Exactly the matching object is non-null: `Followers`, `Slow`, `Vip`, `Unvip`, `Mod`, `Unmod`, `Ban`, `Unban`, `Timeout`, `Untimeout`, `Raid`, `Unraid`, `Delete`, `AutomodTerms` (add/remove blocked/permitted term), `UnbanRequest` (approve/deny), `Warn` (v2 only) and the shared chat variants `SharedChatBan`, `SharedChatUnban`, `SharedChatTimeout`, `SharedChatUntimeout`, `SharedChatDelete`. Actions without data (`clear`, `emoteonly`, `followersoff`, `slowoff`, `subscribers`, `uniquechat`, and so on) carry no object. `SourceBroadcasterUser*` identifies the shared chat channel where the action happened and may be null outside shared chat. Both versions derive from `ChannelModerateEventBase`, so one handler can serve v1 and v2.
- **channel.moderate scopes.** Twitch requires one scope from each read/manage pair. A subscription spec can express only one alternative group, so `RequiredScopes` holds `moderator:read:moderators` and `moderator:read:vips` and `AnyOfScopes` lists every pair scope; the local preflight therefore checks the fixed scopes and at least one pair scope, and Twitch validates the rest.
- **Unban request resolution.** The field table names the moderator `moderator_id`/`moderator_login`/`moderator_name`, while the official notification example sends `moderator_user_id`/`moderator_user_login`/`moderator_user_name`. `ChannelUnbanRequestResolveEvent` reads both (`ModeratorId…` and `ModeratorUserId…`); all are optional. `Status` is `approved`, `canceled` or `denied`.
- **Ad breaks.** The field table types `duration_seconds` as an integer and `is_automatic` as a boolean while the official example quotes both; the model accepts either form.
- **Shield Mode.** Begin carries `StartedAt`, end carries `EndedAt`.
- **Shoutouts.** `channel.shoutout.receive` is sent only when Twitch posts the Shoutout to the broadcaster's activity feed.

## Example

```csharp
using TwitchDock.EventSub;
using TwitchDock.EventSub.Events;

var router = new EventSubEventRouter()
    .On(EventSubEvents.ChannelModerateV2, (evt, subscription, ct) =>
    {
        switch (evt.Action)
        {
            case "timeout": Console.WriteLine($"{evt.ModeratorUserName} timed out {evt.Timeout!.UserName} until {evt.Timeout.ExpiresAt:O}"); break;
            case "warn": Console.WriteLine($"{evt.Warn!.UserName} warned: {evt.Warn.Reason}"); break;
            case "shared_chat_ban": Console.WriteLine($"{evt.SharedChatBan!.UserName} banned in {evt.SourceBroadcasterUserName}"); break;
        }
        return Task.CompletedTask;
    })
    .On(EventSubEvents.ChannelBanV1, (evt, _, _) =>
    {
        Console.WriteLine(evt.IsPermanent ? $"{evt.UserName} banned" : $"{evt.UserName} timed out until {evt.EndsAt:O}");
        return Task.CompletedTask;
    })
    .On(EventSubEvents.ChannelRaidV1, (evt, _, _) =>
    {
        Console.WriteLine($"{evt.FromBroadcasterUserName} raided with {evt.Viewers} viewers");
        return Task.CompletedTask;
    })
    .OnRevocation((subscription, _) =>
    {
        Console.WriteLine($"{subscription.Type} revoked: {subscription.Status}");
        return Task.CompletedTask;
    });

// The user token belongs to moderatorId and holds the scopes listed above.
var socket = provider.GetRequiredService<EventSubWebSocketClient>();
await socket.RunAsync(
    async (session, resubscribe, ct) =>
    {
        if (!resubscribe) return;
        await helix.SubscribeWebSocketAsync(EventSubSubscriptions.ChannelModerateV2(broadcasterId, moderatorId), session.Id, ct);
        await helix.SubscribeWebSocketAsync(EventSubSubscriptions.ChannelBanV1(broadcasterId), session.Id, ct); // broadcaster token only
        await helix.SubscribeWebSocketAsync(EventSubSubscriptions.ChannelRaidToBroadcasterV1(broadcasterId), session.Id, ct);
    },
    (message, ct) => router.DispatchAsync(message, ct),
    cancellationToken);
```

`SubscribeWebSocketAsync` throws `TwitchAuthorizationException` before any request when the token lacks scopes (`MissingScopes`, `RequiredAnyOfScopes`) or belongs to another user. In the example the `channel.ban` subscription therefore needs `moderatorId == broadcasterId`; a moderator should subscribe to `channel.moderate` instead. For webhooks or conduits call `helix.CreateEventSubSubscriptionAsync(spec, transport)` with an app token and pass verified payloads to the same router.
