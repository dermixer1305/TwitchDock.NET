# Changelog

All notable changes are listed here. The project follows [Semantic Versioning](https://semver.org/); see [releases](docs/releases.md#versioning).

## 1.0.0-rc.1 (2026-10-09)

First public release candidate under the TwitchDock.NET name. Every Helix endpoint (149) and EventSub type/version (83) in the pinned documentation of 2026-10-09 meets the definition of done ([coverage](docs/coverage.md)). Selected real-credential authentication, API and chat checks passed; broader live verification remains open ([report](docs/live-verification.md), [roadmap](docs/roadmap.md)).

### Publication and onboarding

- Renamed packages, namespaces, projects and `AddTwitchSdk` from the unpublished `TwitchSdk` name to `TwitchDock` / `AddTwitchDock`. Live-test and API-snapshot environment variables now use the `TWITCHDOCK_` prefix.
- English and German READMEs, step-by-step tutorials and release notes; repository metadata and private security reporting links.
- The chat sample supports interactive device login without a pre-supplied token, defaults to the user's own channel, and accepts `!ping` from that account for a single-account test.
- GitHub releases attach all six NuGet packages, a zip bundle of them and SHA-256 checksums after the full CI checks succeed. The same packages are published to nuget.org through trusted publishing after maintainer approval, and to GitHub Packages.
- CLI webhook integration tests explicitly bind IPv4 loopback so the Linux listener matches the Twitch CLI's IPv4 connection.

### Breaking changes since 0.1.0-alpha.1

- `TwitchDock.Chat.ChatMessage` and its companion types (`ChatMessageContent`, `ChatFragment`, `ChatBadge`, `ChatCheer`, `ChatCheermote`, `ChatEmote`, `ChatMention`, `ChatGif`, `ChatReply`, `ChatJsonContext`) were removed. `TwitchChatClient.TryReadMessage` now returns EventSub's `ChannelChatMessageEvent`; property names are unchanged, nested types are now `ChatMessageBody`, `ChatMessageFragment`, `ChatMessageBadge` and so on ([migration notes](docs/eventsub-chat-automod.md#chat-module-0x-breaking-change)).
- `TwitchChatClient.SubscribeAsync` now subscribes through the typed `channel.chat.message` spec, so a token without `user:read:chat`, an app token or another user's token fails with `TwitchAuthorizationException` before the request.
- `ScheduleVacation.StartTime` and `EndTime` are nullable; an empty vacation object reads as null timestamps.
- Null request lists now mean "no filter", the same as empty lists, and response properties with defaults keep them when Twitch omits a field.
- `TwitchOAuthClient` takes optional `TimeProvider` and `OpenIdSigningKeyCache` constructor parameters (source compatible, binary breaking).
- `ValidateIdTokenAsync` requires the expected nonce; flows without a nonce use `ValidateIdTokenWithoutNonceAsync`. `ImplicitGrantCallback.AccessToken` is nullable for ID-token-only responses.
- `TwitchHttpClient` accepts only allow-listed endpoint paths (`[A-Za-z0-9_-]` segments separated by `/`).
- `MessageDeduplicator` evicts the oldest ID when full (100,000 IDs, eleven minutes by default); the previous fail-closed behavior is `throwWhenFull: true` and raises `EventSubDeduplicationException`.

### Security and robustness (review follow-ups)

- Fixed a bearer-token exfiltration path: leading whitespace or control characters in an endpoint path could resolve to another host. The resolved URI must now keep the configured origin.
- OpenID Connect: required nonce, `at_hash` and `azp` checks, 16 KiB token limit, and a shared signing-key cache with single-flight refresh, one-hour key expiry and negative caching.
- EventSub: undeserializable events are reported through `EventSubEventRouter.OnDeserializationError` instead of stopping the client; the webhook handler checks the message-type header against the signed body; WebSocket session migration survives the old socket closing first; reconnect backoff has jitter; connections accept plain `ws://` only for loopback addresses.
- `RefreshingTokenProvider` runs refresh and persistence independent of the caller's cancellation (bounded by a timeout) and briefly replays a failed refresh instead of hammering the token endpoint.
- Response size caps (`TwitchHttpOptions.MaxResponseContentBytes`, 32 MiB; OAuth 1 MiB), `TwitchApiException.RetryAfter` for 429s that are not retried, `TwitchHttpOptions.EnsureValid()` called at registration, device polling defaults for missing `expires_in`/`interval`.
- IRC: reconnect backoff grows for accept-then-drop servers and fast RECONNECTs, writes are bounded by the keepalive timeout and no longer use the caller's cancellation, transient TLS handshake failures are retried, join/part races and long PING origins are handled, loopback-only plain endpoints, bounded connection disposal.
- CI: actions pinned to commit SHAs without persisted credentials, and the Twitch CLI download is verified against the release checksums.

### Core

- Plain `http://` base addresses are accepted on loopback hosts for local mock servers such as the Twitch CLI.
- `EmptyStringAsNullDateTimeOffsetConverter` reads empty-string timestamps as null.
- Clearer authorization messages when an operation accepts no OAuth token at all (Extension JWT endpoints).

### Authentication

- Device code flow polling with `WaitForDeviceAuthorizationAsync` (`authorization_pending`, `slow_down`, expiry).
- Callback parsing with `TwitchOAuthCallbacks.ParseAuthorizationCode` and `ParseImplicitGrant`: constant-time state check, error reporting through `TwitchOAuthCallbackException`, rejection of repeated parameters.
- OpenID Connect: `CreateOpenIdAuthorizationUri` with claims requests, `ValidateIdTokenAsync` (RS256 signature against Twitch's JWKS, issuer, audience, lifetime, nonce) and `GetUserInfoAsync`.
- `TokenValidationLoop` tries one refresh when Twitch rejects the token with 401.

### Helix

- Moderation enforcement: AutoMod check, held messages and settings, bans and timeouts, unban requests, blocked terms, chat message deletion.
- Moderation roles and safety: moderated channels, moderators, VIPs, Shield Mode, warnings, suspicious users.
- The moderation endpoints validate parameters locally, preflight known token scopes, kind and user, and offer enumerators for paginated lists.
- Chat catalog: chatters, channel, global, set and user emotes with templates, chat badges.
- Chat settings, announcements, Shoutouts, pins, chat colors and shared chat sessions.
- Extensions (12 endpoints) with EBS support through `ExtensionSecret`, `ExtensionJwt` and `ExtensionJwtTokenProvider` (minimum JWT lifetime 30 seconds).
- Drops entitlements (empty entitlement ID lists are rejected locally), Guest Star (12 endpoints, public beta), Tags (obsolete, deprecated by Twitch), content classification labels, Get Authorization by User and custom Power-ups.

### EventSub

- Typed subscription factories (`EventSubSubscriptions`) and event definitions (`EventSubEvents`) for all 83 type/versions: chat and AutoMod, moderation and channel, monetization and interaction, community and system (including Guest Star beta and batched Drops), with per-type scopes, authorizing user and transports.
- Typed `CreateEventSubSubscriptionAsync(spec, transport)` and `SubscribeWebSocketAsync(spec, sessionId)` with WebSocket preflight and app-token enforcement for webhooks and conduits.
- `EventSubEventRouter`, `TryReadEvent` on messages and payloads, and a registry (`EventSubEvents.All`, `TryGetDefinition`).
- `EventSubWebhookHandler`: framework-independent verification, callback challenge, deduplication and dispatch; handler failures release the message ID so Twitch retries.
- `EventSubWebSocketClient` accepts a `ws://` endpoint on loopback hosts for the Twitch CLI mock server.
- Batched payloads (`events`) and `is_batching_enabled` for `drop.entitlement.grant`.
- Models tolerate documented payload variants (charity `broadcaster_*` and `broadcaster_user_*` fields, missing `stream.offline` and unban request IDs).

### Chat

- IRC transport in `TwitchDock.Chat.Irc`: `TwitchIrcClient` (login with one refresh, rejoin, keepalive, reconnects, rate limits), `IrcMessage` IRCv3 parser and serializer, typed views, `IrcMessageRouter`, rate limiters, WebSocket and TCP connections.

### DependencyInjection

- `AddTwitchTokenValidation` registers a hosted service that validates at startup and hourly, retries transient failures a bounded number of times and faults on invalid tokens, which stops the host under the default host settings.
- `AddTwitchIrc` registers `TwitchIrcClient` with the configured token provider.

### Build, tests and documentation

- Trimming and AOT analyzers on all libraries; native AOT smoke runs of the packed SDK on net8.0 and net10.0.
- XML documentation files ship in the packages.
- Public API snapshot tests detect breaking changes.
- Integration tests against the Twitch CLI mock API, mock EventSub WebSocket server and CLI-signed webhooks.
- CI: test matrix (Ubuntu and Windows, net8.0 and net10.0) with the coverage release gate, integration job, pack with package smoke and native AOT; weekly `api-drift` workflow; Dependabot.
- Documentation index, authentication and EventSub guides, testing guide, generated coverage report (`tools/New-CoverageReport.ps1`), chat bot and webhook host samples.

## 0.1.0-alpha.1 (not published)

- Create independent .NET 8/.NET 10 modules and a pinned official API inventory.
- Add HTTP retries, rate-limit coordination, error mapping and pagination.
- Add OAuth grant requests, token validation/revocation and serialized refresh.
- Add initial Users, Streams, Channels, Chat and EventSub subscription methods.
- Add EventSub WebSocket sessions/migration, webhook authentication and chat models.
- Add DI, offline tests, CI, documentation and local NuGet packaging.
- Add reviewed Ads, Analytics, Games, Search, Goals and Raids groups with full wire-model fixtures and examples.
- Check known token kinds, scopes and identities before sending, including after refresh; apply validation metadata without racing token rotation.
- Honor rate-limit extensions from concurrent responses while requests are already waiting.
- Add reviewed Clips, Videos, Charity and Teams groups, including VOD clip creation, temporary clip-download URLs, exact decimal durations and nullable media fields.
- Support alternative user-scope requirements and generate the complete scope-name catalog directly from Twitch's tables.
- Review the initial Users, Streams, Channels, Send Chat, List EventSub and Delete EventSub contracts; add subscription enumeration and preserve existing subscription IDs in duplicate-subscription errors.
- Separate EventSub request and response transports and reject fields that belong to a different transport. Explicit creation initializers must now use EventSubTransportRequest.
- Add channel updates, editor lists, followed channels and followers, including total-only follower responses without follower scope and both forward paginators.
- Complete the Streams group with stream keys, followed live streams and marker creation/listing; allow editor access and alternative marker-read scopes.
- Add broadcaster/user subscriptions, Bits leaderboards, Cheermotes and extension-owned transactions, preserving nullable totals, nested image dictionaries and mixed-case product JSON fields.
- Add all six custom reward/redemption endpoints with creator-app rules, conditional settings, 64-bit costs, nullable images/cooldowns and pagination.
- Add all Polls and Predictions endpoints, including ten-outcome predictions, alternate read scopes, state transitions and nullable lifecycle fields.
- Complete Users with profile changes, block lists, installed extensions and typed active-extension slot maps; add Whispers with documented truncation, delivery and rate-limit semantics.
- Add all Schedule endpoints, including vacation settings, recurring segment operations, string-encoded minute durations and a public iCalendar text path that bypasses token acquisition.
- Add Get Hype Train Status with shared-train participants, nullable records and 64-bit point counters. Contract tests now compare timestamps with more than seven fractional digits by truncation, matching System.Text.Json.
- Add all six Conduits endpoints with app-only authorization, shard pagination, separate secret-bearing request transports and explicit per-shard errors in HTTP 202 results.
