# Samples

The samples live in `samples/`, are part of `TwitchSdk.slnx`, build for net8.0 and net10.0 with warnings as errors, and reference the SDK projects directly (packages are not on NuGet yet). They read every credential from environment variables; never put tokens or secrets into source, launch profiles or command history.

| Sample | Shows |
| --- | --- |
| [TwitchSdk.Quickstart](../samples/TwitchSdk.Quickstart/Program.cs) | Dependency injection, an app token through client credentials, token validation, Get Users |
| [TwitchSdk.ChatBot](../samples/TwitchSdk.ChatBot/Program.cs) | EventSub WebSocket chat bot with a typed router, replies through Helix, hourly validation |
| [TwitchSdk.WebhookHost](../samples/TwitchSdk.WebhookHost/Program.cs) | ASP.NET Core minimal API receiving EventSub webhooks through `EventSubWebhookHandler` |

## Quickstart

```sh
export TWITCH_CLIENT_ID=...       # PowerShell: $env:TWITCH_CLIENT_ID = '...'
export TWITCH_CLIENT_SECRET=...
dotnet run --project samples/TwitchSdk.Quickstart -f net10.0 -- twitchdev
```

Acquires an app token, validates it, checks its client ID and prints the user ID and display name for the login given as argument (default `twitchdev`).

## Chat bot

Connects to EventSub over WebSocket, subscribes to `channel.chat.message` (`EventSubSubscriptions.ChannelChatMessageV1`), logs chat through an `EventSubEventRouter` and answers `!ping` with `pong` as a reply through `helix.SendChatMessageAsync`.

| Variable | Value |
| --- | --- |
| `TWITCH_CLIENT_ID` | Client ID the token was issued to |
| `TWITCH_ACCESS_TOKEN` | User access token of the bot account with `user:read:chat` and `user:write:chat` |
| `TWITCH_BOT_USER_ID` | User ID of the bot account (the token's user) |
| `TWITCH_BROADCASTER_ID` | User ID of the channel to join |

```sh
dotnet run --project samples/TwitchSdk.ChatBot -f net10.0
```

Get a token with the [device code flow](authentication.md#device-code) or the authorization code flow. The bot first calls `TwitchOAuthClient.ValidateAsync`, rejects a token for another client or user, and builds the `AccessToken` from the validation result. That gives the SDK the token's scopes and user ID, so a missing scope or a wrong account fails locally with `TwitchAuthorizationException` before any subscription or message is sent. It runs `TokenValidationLoop` next to the WebSocket client to validate hourly, as Twitch requires; the sample uses a fixed token, so it stops when the token expires or is revoked. A long-running bot would use `RefreshingTokenProvider` with a refresh token ([authentication](authentication.md#token-providers-and-refresh)). Check `IsSent` and `DropReason` on send results: HTTP success does not guarantee delivery. Ctrl+C cancels both loops.

The sample reads and sends with the bot's own user token. Sending with an app token instead, which Twitch shows with the chat bot badge, requires the `user:bot` grant from the bot account and either `channel:bot` from the broadcaster or moderator status; see [Twitch chat authentication](https://dev.twitch.tv/docs/chat/authenticating/).

## Webhook host

Maps `POST /eventsub` to `EventSubWebhookHandler`: it reads the raw body (Kestrel limits it to 1 MiB), builds the request with `EventSubWebhookRequest.FromHeaders`, and writes the returned status code, content type and body. The router logs `stream.online` and `channel.follow` v2 notifications and revocations.

| Variable | Value |
| --- | --- |
| `TWITCH_EVENTSUB_SECRET` | The webhook secret (10 to 100 ASCII characters) used when creating the subscriptions |

Try it locally with the [Twitch CLI](testing.md#integration-tests-twitch-cli), no Twitch account needed:

```sh
export TWITCH_EVENTSUB_SECRET=local-test-secret
dotnet run --project samples/TwitchSdk.WebhookHost -f net10.0 -- --urls http://localhost:5000
# in a second shell
twitch event verify-subscription stream.online -F http://localhost:5000/eventsub -s local-test-secret
twitch event trigger stream.online -F http://localhost:5000/eventsub -s local-test-secret
twitch event trigger channel.follow -v 2 -F http://localhost:5000/eventsub -s local-test-secret
```

The host logs the live and follow events and answers 204; a delivery signed with another secret gets 403. For real subscriptions Twitch requires a public HTTPS callback on port 443, typically TLS termination in a reverse proxy in front of the app. Create them with an app token, for example `helix.CreateEventSubSubscriptionAsync(EventSubSubscriptions.StreamOnlineV1(broadcasterId), new EventSubTransportRequest { Method = "webhook", Callback = "https://example.com/eventsub", Secret = secret })`. The handler's deduplication is in memory; several replicas need a shared durable inbox ([EventSub](eventsub.md#delivery-and-deduplication)).
