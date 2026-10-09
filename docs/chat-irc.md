# Chat over IRC

`TwitchDock.Chat.Irc` implements Twitch chat over IRCv3: a parser and serializer (`IrcMessage`), typed views of the Twitch commands, a router, and `TwitchIrcClient`, which owns the connection lifecycle (login, rejoin, keepalive, reconnects and rate limits). It needs no extra packages.

## IRC or EventSub + Helix?

Twitch recommends EventSub (`channel.chat.message` and related subscriptions) plus the Helix Send Chat Message endpoint for new chatbots. Use `TwitchChatClient` and the EventSub module for that path. IRC remains officially supported and fits when:

- an existing IRC-based bot or tool is being migrated,
- you want to read many channels over one connection without creating a subscription per channel,
- you need the raw Twitch tags exactly as Twitch sends them.

| | IRC (`TwitchIrcClient`) | EventSub + Helix |
|---|---|---|
| Read | `JOIN` per channel on one connection | One `channel.chat.message` subscription per channel |
| Send | `PRIVMSG`, subject to the IRC rate limits below | `POST /chat/messages` (`SendAsync`) |
| Scopes | `chat:read`, plus `chat:edit` to send | `user:read:chat`, `user:write:chat` (and `user:bot`/`channel:bot` for bot sending) |
| Moderation | Observe `CLEARCHAT`/`CLEARMSG`; act through Helix | `channel.moderate` events; act through Helix |
| Message model | Flat string tags | Typed JSON with fragments, mentions and cheermotes |

IRC chat commands such as `/ban` or `/color` sent as `PRIVMSG` no longer work; use the Helix moderation and chat endpoints instead. Whispers can still arrive over IRC (`IrcWhisper`), but you send them through Helix.

## Authentication

The client needs a **user access token** from an `IAccessTokenProvider` (for example `RefreshingTokenProvider`) and the login name of the token's user:

- `chat:read` is required to connect. When the token's scopes are known, `RunAsync` throws `TwitchAuthorizationException` (with `MissingScopes`) before connecting. App tokens are rejected the same way.
- `chat:edit` is required to send. `SendMessageAsync` checks it before sending when the scopes are known.
- The client sends `CAP REQ`, `PASS oauth:<token>` and `NICK <login>` (lower case), in that order, then waits for the `001` welcome.
- If Twitch answers `Login authentication failed`, the client calls `RefreshTokenAsync` once and retries with the new token. A second failure, `Improperly formatted auth`, or a provider that returns the same token throws `TwitchIrcAuthenticationException`. Its `ServerNotice` holds the Twitch text, and nothing in the exception contains the token.
- The token is never logged. `IrcMessage.ToString()` redacts `PASS` parameters; only `Serialize()` returns the wire form.

## Endpoints and transports

| Endpoint | Transport |
|---|---|
| `wss://irc-ws.chat.twitch.tv:443` (default, `TwitchIrcOptions.DefaultEndpoint`) | `WebSocketIrcConnection` (`ClientWebSocket`) |
| `ircs://irc.chat.twitch.tv:6697` (`TwitchIrcOptions.DefaultTcpEndpoint`) | `TcpIrcConnection` (TLS, certificate checked against the host name) |
| `ws://` or `irc://` | Loopback hosts only, for local test servers |

Both transports split the byte stream on CR LF (a bare LF also ends a line), handle lines that span WebSocket frames or TCP reads, and discard any line longer than `maxLineBytes` (64 KiB by default) instead of buffering it. `IrcMessage.Parse` also rejects lines over `IrcMessage.MaxLineLength` (16 KiB) and lines with embedded CR, LF or NUL. To plug in a different transport, pass a `connectionFactory` that returns your own `IIrcConnection`.

## Capabilities

`TwitchIrcOptions.Capabilities` defaults to all three Twitch capabilities:

- `twitch.tv/tags` adds the metadata tags. The typed views depend on them.
- `twitch.tv/commands` enables `CLEARCHAT`, `CLEARMSG`, `GLOBALUSERSTATE`, `NOTICE` with `msg-id`, `RECONNECT`, `ROOMSTATE`, `USERNOTICE`, `USERSTATE` and `WHISPER`.
- `twitch.tv/membership` adds `JOIN`/`PART` for other chatters. Twitch batches these and they can arrive late.

If Twitch replies `CAP * NAK`, `RunAsync` throws `TwitchIrcException`. An empty list skips `CAP REQ` entirely.

## Receiving messages

`RunAsync(onMessage, cancellationToken)` runs until it is cancelled or fails. Callbacks run sequentially, like `EventSubWebSocketClient`:

- Each connection first delivers its login replies (`CAP ACK`, then `001`). Use `001` as the "connected" signal.
- The client answers `PING` itself. Neither `PING` nor `PONG` reaches the callback.
- `RECONNECT` is passed to the callback, then the client reconnects.
- Malformed lines are skipped (and logged at debug level without their content).
- If a callback throws, the client stops, disposes the connection, and `RunAsync` rethrows that exact exception.

`IrcMessage` exposes `Tags` (unescaped, as `IReadOnlyDictionary<string, string>`), `Prefix`/`Nick`/`User`/`Host`, `Command` (upper case) and `Parameters` (the trailing parameter is last). Typed views wrap it. Each view has a `TryCreate` method and keeps the original message in `Raw`, so tags the view does not model stay accessible:

| Command | View | Highlights |
|---|---|---|
| `PRIVMSG` | `IrcChatMessage` | `Text` (with `IsAction` for `/me`, wrapper removed), `MessageId`, `UserId`, `UserLogin`, `DisplayName`, `Color`, `Badges`, `BadgeInfo`, `Emotes`, `Bits`, `IsFirstMessage`, `IsModerator`/`IsSubscriber`/`IsVip`/`IsBroadcaster`, `MsgId`, `Reply` (`reply-parent-*`, `reply-thread-parent-*`), `Source` (shared chat `source-*`), `SentAt` |
| `USERNOTICE` | `IrcUserNotice` | `MsgId` (`sub`, `resub`, `subgift`, `raid`, `announcement`, `sharedchatnotice`, ...), `Parameters` (all `msg-param-*` tags without the prefix), `TryGetInt32Parameter`, `SystemMessage`, optional `Text`, `Source.MsgId` |
| `NOTICE` | `IrcNotice` | `MsgId`, `Text`, `Channel` (null for `*`) |
| `CLEARCHAT` | `IrcClearChat` | `TargetLogin`, `TargetUserId`, `BanDuration`, `IsPermanentBan`, `IsChatCleared` |
| `CLEARMSG` | `IrcClearMessage` | `TargetMessageId`, `Login`, `Text` |
| `ROOMSTATE` | `IrcRoomState` | Nullable settings, because updates after the first one carry only the changed tag |
| `USERSTATE` | `IrcUserState` | `MessageId` of the message you just sent, `IsModerator`, `IsBroadcaster`, `EmoteSets` |
| `GLOBALUSERSTATE` | `IrcGlobalUserState` | `UserId`, `DisplayName`, `EmoteSets` |
| `WHISPER` | `IrcWhisper` | `FromLogin`, `ToLogin`, `Text`, `ThreadId` |

Notes:

- **Emote positions** are Unicode code point indices, as Twitch counts them, not UTF-16 indices. Use `IrcEmote.GetText(chat.Text)` to extract an emote safely when emoji precede it.
- **Badges** stay in wire order (`IReadOnlyList<IrcBadge>`). `BadgeInfo` maps the set ID to its info, for example `subscriber` to the exact number of months.
- **Lenient reads.** Absent or empty optional tags read as null or false. Malformed numbers, timestamps or emote ranges are ignored rather than thrown. Values such as `MsgId` and `UserType` stay strings, so new Twitch values remain readable.

`IrcMessageRouter` dispatches to typed handlers (`OnChatMessage`, `OnUserNotice`, `OnNotice`, `OnClearChat`, `OnClearMessage`, `OnRoomState`, `OnUserState`, `OnGlobalUserState`, `OnWhisper`). `OnCommand` covers other commands, such as `JOIN` or `001`, and `OnUnhandled` catches the rest.

## Joining and sending

- `JoinAsync(channel)` / `PartAsync(channel)` accept `name` or `#name`, case-insensitive, 1 to 25 letters, digits or underscores (`TwitchIrcClient.NormalizeChannelName`). Joined channels are remembered and rejoined after every reconnect. `JoinAsync` before or between connections only records the channel. Cancelling a rate-limit wait leaves the channel unjoined.
- `SendMessageAsync(channel, text, replyParentMessageId)` accepts 1 to 500 Unicode code points. It rejects CR, LF and NUL, so no input can inject another IRC line, and sends the reply ID as the `reply-parent-msg-id` tag.
  - It throws `InvalidOperationException` when no connection is logged in. Messages are not queued across reconnects, because a lost send may or may not have reached Twitch.
  - A transport failure during the send surfaces as `TwitchIrcException` with the original error as `InnerException`.
- `SendRawAsync(IrcMessage)` sends anything else, for example a `PRIVMSG` with a `client-nonce` tag. `PRIVMSG` and `JOIN` still count against the limits (`JOIN` once per comma-separated channel). The client rejects `PASS`, `NICK`, `USER` and `CAP` here because it handles login itself.

Watch `NOTICE` `msg-id` values such as `msg_ratelimit`, `msg_duplicate`, `msg_slowmode`, `msg_followersonly`, `msg_banned` and `msg_channel_suspended`. Twitch reports rejected messages this way.

## Rate limits

All waits are cancellable and use the `TimeProvider` passed to the client. The limiters use a sliding window: every permit lasts exactly one window, so no burst can exceed the limit across a window boundary.

| Option | Default | Twitch limit |
|---|---|---|
| `MessageRateLimit` | `IrcRateLimit.Messages` (20 per 30 s) | Regular users. Use `IrcRateLimit.ModeratorMessages` (100 per 30 s) only when the account is broadcaster or moderator in **every** channel it sends to. |
| `JoinRateLimit` | `IrcRateLimit.Joins` (20 per 10 s) | Counted per channel joined, including automatic rejoins |
| `AuthenticationRateLimit` | `IrcRateLimit.Authentication` (20 per 10 s) | Applies to every login attempt, including reconnects |

Twitch counts limits per account, while each client has its own limiters. If several clients log in as the same account, give each a share of the budget, for example `new IrcRateLimit(10, TimeSpan.FromSeconds(30))`. `IrcSlidingWindowRateLimiter` is public if you want to apply the same limits to your own operations.

## Keepalive and reconnects

- **Server PING.** Twitch sends `PING` about every five minutes. The client answers with `PONG` carrying the same text.
- **Client keepalive.** After `KeepaliveInterval` (60 s) without any received line, the client sends `PING :tmi.twitch.tv`. If nothing arrives within `KeepaliveTimeout` (10 s), it treats the connection as dead. `Timeout.InfiniteTimeSpan` disables the client keepalive.
- **RECONNECT.** The client opens a new connection immediately.
- **Dropped connections, failed connects and timeouts.** The client retries after 1 s, 2 s, 4 s and so on, capped at `MaxReconnectDelay` (30 s). The backoff resets after each successful login. `ConnectTimeout` (30 s) bounds connecting plus the login handshake.
- **What gets retried.** WebSocket, socket and I/O errors, timeouts, and transient token-endpoint failures (network errors, HTTP 429, HTTP 5xx). Authentication, authorization, capability and callback errors stop `RunAsync` and propagate.
- **Rejoins** run in the background on each new connection and respect `JoinRateLimit`.
- **No replay.** Twitch does not replay chat missed while disconnected.

Callbacks block the receive loop while they run. A callback that awaits `SendMessageAsync` may wait for a rate-limit permit, and no lines are read during that wait. For heavy work, hand messages to a bounded queue.

## Example bot

```csharp
using TwitchDock.Chat.Irc;
using TwitchDock.Core;

// A user token for the bot account with chat:read and chat:edit, e.g. a RefreshingTokenProvider.
IAccessTokenProvider tokens = provider.GetRequiredService<IAccessTokenProvider>();
var irc = new TwitchIrcClient(tokens, new TwitchIrcOptions { Login = "mybot" }, logger: loggerFactory.CreateLogger<TwitchIrcClient>());
await irc.JoinAsync("somechannel", cancellationToken);

var router = new IrcMessageRouter()
    .OnCommand("001", (_, _) =>
    {
        Console.WriteLine("Connected to Twitch chat.");
        return Task.CompletedTask;
    })
    .OnChatMessage(async (chat, ct) =>
    {
        if (chat.Text.Equals("!ping", StringComparison.OrdinalIgnoreCase))
            await irc.SendMessageAsync(chat.Channel, $"@{chat.DisplayName} pong", chat.MessageId, ct);
    })
    .OnUserNotice((notice, _) =>
    {
        if (notice.MsgId == "resub" && notice.TryGetInt32Parameter("cumulative-months", out var months))
            Console.WriteLine($"{notice.DisplayName} resubscribed for {months} months: {notice.Text}");
        return Task.CompletedTask;
    })
    .OnClearChat((clear, _) =>
    {
        Console.WriteLine(clear.IsChatCleared ? "Chat cleared" : $"{clear.TargetLogin} removed for {clear.BanDuration?.ToString() ?? "ever"}");
        return Task.CompletedTask;
    })
    .OnNotice((notice, _) =>
    {
        Console.WriteLine($"NOTICE {notice.MsgId}: {notice.Text}");
        return Task.CompletedTask;
    });

// Runs until cancelled; host it in a BackgroundService for long-running bots.
await irc.RunAsync(router.DispatchAsync, cancellationToken);
```

For tests, inject a fake `IIrcConnection` through `connectionFactory` and a manual `TimeProvider`. That makes keepalive, backoff and rate limits deterministic. See `tests/TwitchDock.Tests/IrcClientTests.cs`.
