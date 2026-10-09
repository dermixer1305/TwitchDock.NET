# EventSub: chat and AutoMod

Typed subscriptions and events for chat, shared chat, AutoMod, suspicious users and warnings. Each row pairs a factory on
`EventSubSubscriptions` with a definition of the same name on `EventSubEvents` (for example `EventSubSubscriptions.ChannelChatMessageV1(...)`
and `EventSubEvents.ChannelChatMessageV1`). Events live in `TwitchDock.EventSub.Events`. See [quickstart](quickstart.md) for client setup.

| Type@version | Factory / definition | Event class | Authorization (WebSocket user token) | Transports |
| --- | --- | --- | --- | --- |
| `automod.message.hold@1` | `AutomodMessageHoldV1(broadcasterUserId, moderatorUserId)` | `AutomodMessageHoldEvent` | `moderator:manage:automod`; token user = moderator | all |
| `automod.message.hold@2` | `AutomodMessageHoldV2(broadcasterUserId, moderatorUserId)` | `AutomodMessageHoldEventV2` | `moderator:manage:automod`; token user = moderator | all |
| `automod.message.update@1` | `AutomodMessageUpdateV1(broadcasterUserId, moderatorUserId)` | `AutomodMessageUpdateEvent` | `moderator:manage:automod`; token user = moderator | all |
| `automod.message.update@2` | `AutomodMessageUpdateV2(broadcasterUserId, moderatorUserId)` | `AutomodMessageUpdateEventV2` | `moderator:manage:automod`; token user = moderator | all |
| `automod.settings.update@1` | `AutomodSettingsUpdateV1(broadcasterUserId, moderatorUserId)` | `AutomodSettingsUpdateEvent` | `moderator:read:automod_settings`; token user = moderator | all |
| `automod.terms.update@1` | `AutomodTermsUpdateV1(broadcasterUserId, moderatorUserId)` | `AutomodTermsUpdateEvent` | `moderator:manage:automod`; token user = moderator | all |
| `channel.chat.clear@1` | `ChannelChatClearV1(broadcasterUserId, userId)` | `ChannelChatClearEvent` | `user:read:chat`; token user = chatting user | all |
| `channel.chat.clear_user_messages@1` | `ChannelChatClearUserMessagesV1(broadcasterUserId, userId)` | `ChannelChatClearUserMessagesEvent` | `user:read:chat`; token user = chatting user | all |
| `channel.chat.message@1` | `ChannelChatMessageV1(broadcasterUserId, userId)` | `ChannelChatMessageEvent` | `user:read:chat`; token user = chatting user | all |
| `channel.chat.message_delete@1` | `ChannelChatMessageDeleteV1(broadcasterUserId, userId)` | `ChannelChatMessageDeleteEvent` | `user:read:chat`; token user = chatting user | all |
| `channel.chat.notification@1` | `ChannelChatNotificationV1(broadcasterUserId, userId)` | `ChannelChatNotificationEvent` | `user:read:chat`; token user = chatting user | all |
| `channel.chat_settings.update@1` | `ChannelChatSettingsUpdateV1(broadcasterUserId, userId)` | `ChannelChatSettingsUpdateEvent` | `user:read:chat`; token user = chatting user | all |
| `channel.chat.user_message_hold@1` | `ChannelChatUserMessageHoldV1(broadcasterUserId, userId)` | `ChannelChatUserMessageHoldEvent` | `user:read:chat`; token user = chatting user | all |
| `channel.chat.user_message_update@1` | `ChannelChatUserMessageUpdateV1(broadcasterUserId, userId)` | `ChannelChatUserMessageUpdateEvent` | `user:read:chat`; token user = chatting user | all |
| `channel.shared_chat.begin@1` | `ChannelSharedChatBeginV1(broadcasterUserId)` | `ChannelSharedChatBeginEvent` | none | all |
| `channel.shared_chat.update@1` | `ChannelSharedChatUpdateV1(broadcasterUserId)` | `ChannelSharedChatUpdateEvent` | none | all |
| `channel.shared_chat.end@1` | `ChannelSharedChatEndV1(broadcasterUserId)` | `ChannelSharedChatEndEvent` | none | all |
| `channel.suspicious_user.message@1` | `ChannelSuspiciousUserMessageV1(broadcasterUserId, moderatorUserId)` | `ChannelSuspiciousUserMessageEvent` | `moderator:read:suspicious_users`; token user = moderator | all |
| `channel.suspicious_user.update@1` | `ChannelSuspiciousUserUpdateV1(broadcasterUserId, moderatorUserId)` | `ChannelSuspiciousUserUpdateEvent` | `moderator:read:suspicious_users`; token user = moderator | all |
| `channel.warning.acknowledge@1` | `ChannelWarningAcknowledgeV1(broadcasterUserId, moderatorUserId)` | `ChannelWarningAcknowledgeEvent` | `moderator:read:warnings` or `moderator:manage:warnings`; token user = moderator | all |
| `channel.warning.send@1` | `ChannelWarningSendV1(broadcasterUserId, moderatorUserId)` | `ChannelWarningSendEvent` | `moderator:read:warnings` or `moderator:manage:warnings`; token user = moderator | all |

All condition fields are required; blank values throw `ArgumentException` before any request. `SubscribeWebSocketAsync` preflights the
scopes (`RequiredScopes`, or one of `AnyOfScopes` for warnings) and the condition user (`AuthorizingUserId`) against the user token.
Webhook and conduit subscriptions use an app token, so the user's grant to your app is checked by Twitch only:

- AutoMod, suspicious-user and warning types: the moderator must have granted the scope to your client ID and must be a moderator or the
  broadcaster of the channel.
- Chat types with an app token additionally need `user:bot` from the chatting user and either `channel:bot` from the broadcaster or moderator
  status. `channel.chat.user_message_hold` and `channel.chat.user_message_update` only need the extra `user:bot`.

## Variants and polymorphic payloads

- **AutoMod v1 vs v2.** v1 events carry `Category` and `Level` at the top level. v2 events carry `Reason` (`automod`, `blocked_term`
  or `blocked_link`) and populate `Automod` (category, level, boundaries) or `BlockedTerm` (terms found with their boundary and owner)
  accordingly. Only public blocked terms trigger v2 notifications. The v1 `Message` also accepts the plain string that Twitch's v1
  examples show; `Message.Text` then holds it and `Fragments` is empty.
- **Held message fragments.** `AutomodHeldMessage` is shared by `automod.message.*` and `channel.chat.user_message_*`; each
  `AutomodHeldMessageFragment` carries `Type` (`text`, `emote`, `cheermote`; null when Twitch omits it) plus an optional `Emote` or
  `Cheermote`. Suspicious-user messages use the same fragment shape. Cheermote `Bits` and `Tier` also accept numeric strings, because the
  suspicious-user field table documents them as strings while Twitch's example sends numbers.
- **Chat message fragments.** `ChatMessageFragment.Type` is `text`, `cheermote`, `emote`, `mention` or `gif`, and the matching object
  (`Cheermote`, `Emote`, `Mention`, `Gif`) is populated. Render `Gif.Url` unmodified.
- **Chat message metadata.** `MessageType`, `Badges`, `Cheer`, `Color`, `Reply` (parent and thread), `ChannelPointsCustomRewardId` and
  `ChannelPointsAnimationId` are modeled. In shared chat, `SourceBroadcasterUser*`, `SourceMessageId`, `SourceBadges` and `IsSourceOnly` describe
  the originating channel; they are null for messages sent in the subscribed broadcaster's own channel. `Color`, `MessageType`, badge `Info`
  and the notification's `ChatterUserLogin` read as empty strings and every list as empty when Twitch omits them or sends null.
- **Chat notifications.** `NoticeType` selects exactly one variant property: `Sub`, `Resub`, `SubGift`, `CommunitySubGift`, `GiftPaidUpgrade`,
  `PrimePaidUpgrade`, `PayItForward`, `Raid`, `Unraid` (an empty object), `Announcement`, `BitsBadgeTier`, `CharityDonation`, `WatchStreak`,
  `Modiversary` or `GiftedDropsSummary`. `shared_chat_*` notice types populate the matching `SharedChat*` property instead and fill the
  `Source*` fields. `unknown` populates no variant. Discriminators and status values stay strings, so new Twitch values never fail
  deserialization.
- **Shared chat sessions.** `begin` and `update` include all `Participants`; `end` has no participant list.

## Example

```csharp
using TwitchDock.EventSub;
using TwitchDock.EventSub.Events;

var router = new EventSubEventRouter()
    .On(EventSubEvents.ChannelChatMessageV1, (chat, _, _) =>
    {
        var origin = chat.SourceBroadcasterUserLogin ?? chat.BroadcasterUserLogin;
        Console.WriteLine($"[{origin}] {chat.ChatterUserName}: {chat.Message.Text}");
        return Task.CompletedTask;
    })
    .On(EventSubEvents.ChannelChatNotificationV1, (notice, _, _) =>
    {
        if (notice.Raid is { } raid) Console.WriteLine($"{raid.UserName} raids with {raid.ViewerCount} viewers");
        return Task.CompletedTask;
    })
    .On(EventSubEvents.AutomodMessageHoldV2, (held, _, _) =>
    {
        Console.WriteLine($"Held {held.MessageId} ({held.Reason}): {held.Message.Text}");
        return Task.CompletedTask;
    })
    .OnRevocation((subscription, _) => { Console.WriteLine($"Revoked {subscription.Type}: {subscription.Status}"); return Task.CompletedTask; });

await socket.RunAsync(
    async (session, resubscribe, ct) =>
    {
        if (!resubscribe) return;
        // The user token belongs to botUserId, which moderates broadcasterId.
        await helix.SubscribeWebSocketAsync(EventSubSubscriptions.ChannelChatMessageV1(broadcasterId, botUserId), session.Id, ct);
        await helix.SubscribeWebSocketAsync(EventSubSubscriptions.ChannelChatNotificationV1(broadcasterId, botUserId), session.Id, ct);
        await helix.SubscribeWebSocketAsync(EventSubSubscriptions.AutomodMessageHoldV2(broadcasterId, botUserId), session.Id, ct);
    },
    (message, ct) => router.DispatchAsync(message, ct),
    cancellationToken);
```

## Chat module (0.x breaking change)

`channel.chat.message` previously had a hand-written partial model in `TwitchDock.Chat` (`ChatMessage`, `ChatMessageContent`, `ChatFragment`,
`ChatBadge`, `ChatCheer`, `ChatCheermote`, `ChatEmote`, `ChatMention`, `ChatGif`, `ChatReply`, `ChatJsonContext`). These types were removed.
`TwitchChatClient.TryReadMessage(EventSubMessage, out ChannelChatMessageEvent?)` now reads through `EventSubEvents.ChannelChatMessageV1`
and requires the notification's `payload.subscription`, which Twitch always sends. `TwitchChatClient.SubscribeAsync` now sends
`EventSubSubscriptions.ChannelChatMessageV1` through `SubscribeWebSocketAsync`, so a token without `user:read:chat`, an app token or a token for
another user fails with `TwitchAuthorizationException` before the request. Property names of the old model are unchanged
(`ChatterUserName`, `Message.Text`, `Message.Fragments`, `Badges`, ...); nested types are now `ChatMessageBody`, `ChatMessageFragment`,
`ChatMessageBadge`, `ChatMessageCheer`, `ChatMessageCheermote`, `ChatMessageEmote`, `ChatMessageMention`, `ChatMessageGif` and `ChatMessageReply`.

## Notes on Twitch's documentation

The models follow the field tables where they disagree with the examples:

- `automod.message.hold@1` and `automod.message.update@1`: the examples send `message` as a string with a top-level `fragments` object
  (`emotes`/`cheermotes`, `set-id`), whereas the tables define `message.text` and `message.fragments[]` with `emote_set_id`. Both
  `message` shapes are read; the example's top-level `fragments` object is ignored.
- `automod.message.update@2`: the example's subscription type reads `automod.message.hold` and sets `reason: automod` with `automod: null`.
- `automod.settings.update@1`: the example wraps the event in `data: [...]`; the table describes a flat event.
- `automod.terms.update@1`: the example's `action` is a message ID instead of one of the documented actions.
- `channel.chat.notification@1`: the examples use `resub.sub_plan` instead of `sub_tier` and omit `is_prime`; the table omits
  `chatter_user_login` (present in the examples, empty when omitted) and `gift_paid_upgrade.gifter_user_login` (modeled as optional); the shared
  chat example adds `shared_chat_unraid`, `shared_chat_bits_badge_tier` and `shared_chat_charity_donation` (modeled as optional variants);
  `message.text` is typed object and `charity_donation` string in the table.
- `channel.chat.message@1`: `channel_points_animation_id` is not in the local field table snapshot; it is modeled as optional.
- `channel.suspicious_user.message@1`: cheermote `bits` and `tier` are typed String in the table but are numbers in the example.
