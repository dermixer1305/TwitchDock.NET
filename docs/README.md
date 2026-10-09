# TwitchSdk documentation

Version 1.0.0-rc.1. Start with the [quickstart](quickstart.md), then pick the reference for the API group you need. Every reference lists the methods, parameters, authorization rules, errors and an example, reviewed against the pinned official Twitch documentation of 2026-10-09.

## Getting started

- [Quickstart](quickstart.md): setup, app token, users, streams, chat over EventSub, webhooks, dependency injection and hosting
- [Authentication](authentication.md): every OAuth flow, OpenID Connect, refresh, hourly validation, revocation, scope preflight
- [Samples](samples.md): quickstart console app, EventSub chat bot, ASP.NET Core webhook host

## Helix (REST API)

| Area | Reference |
| --- | --- |
| Users, streams, channels, Send Chat Message, EventSub subscription management, errors | [helix-foundation.md](helix-foundation.md) |
| Users, extension slots, whispers | [helix-users-whispers.md](helix-users-whispers.md) |
| Channel information, editors, followers, followed channels | [helix-channels.md](helix-channels.md) |
| Streams, stream keys, markers | [helix-streams.md](helix-streams.md) |
| Chatters, emotes, badges | [helix-chat-catalog.md](helix-chat-catalog.md) |
| Chat settings, announcements, Shoutouts, pins, chat colors, shared chat | [helix-chat-settings.md](helix-chat-settings.md) |
| AutoMod, bans, unban requests, blocked terms, message deletion | [helix-moderation-enforcement.md](helix-moderation-enforcement.md) |
| Moderators, VIPs, Shield Mode, warnings, suspicious users | [helix-moderation-roles.md](helix-moderation-roles.md) |
| Channel Points rewards and redemptions | [helix-channel-points.md](helix-channel-points.md) |
| Bits, Cheermotes, extension transactions, subscriptions | [helix-bits-subscriptions.md](helix-bits-subscriptions.md) |
| Polls and predictions | [helix-polls-predictions.md](helix-polls-predictions.md) |
| Stream schedule and iCalendar | [helix-schedule.md](helix-schedule.md) |
| Hype Train | [helix-hype-train.md](helix-hype-train.md) |
| Ads, Analytics, Games, Search, Goals, Raids | [helix-groups.md](helix-groups.md) |
| Clips, Videos, Charity, Teams | [helix-media.md](helix-media.md) |
| Extensions and Extension JWTs | [helix-extensions.md](helix-extensions.md) |
| Drops entitlements | [helix-entitlements.md](helix-entitlements.md) |
| Guest Star (public beta) | [helix-guest-star.md](helix-guest-star.md) |
| Tags (deprecated), content classification labels, authorization by user, custom Power-ups | [helix-tags-labels-authorization.md](helix-tags-labels-authorization.md) |
| EventSub conduits | [helix-conduits.md](helix-conduits.md) |

## EventSub

- [EventSub overview](eventsub.md): typed subscriptions, authorization, events and registry, router, WebSocket lifecycle, webhooks, conduits, batching, deduplication, local testing
- [Chat and AutoMod events](eventsub-chat-automod.md)
- [Channel and moderation events](eventsub-moderation-channel.md)
- [Monetization and interaction events](eventsub-monetization-interaction.md)
- [Community and system events](eventsub-community-system.md), including stream online/offline, drops batching and Guest Star (beta)

## Chat

- Recommended: EventSub `channel.chat.message` plus Helix Send Chat Message through `TwitchChatClient` ([quickstart](quickstart.md#chat-over-eventsub), [chat events](eventsub-chat-automod.md), [chat bot sample](samples.md#chat-bot))
- [Chat over IRC](chat-irc.md): `TwitchIrcClient`, typed IRC views, router, rate limits, reconnects
- Chat management through Helix: [catalog](helix-chat-catalog.md) and [settings](helix-chat-settings.md)

## Dependency injection and hosting

- `AddTwitchSdk`, `AddTwitchTokenValidation` and `AddTwitchIrc`: [quickstart](quickstart.md#dependency-injection-and-hosting)
- Hosted hourly token validation: [authentication](authentication.md#validation-startup-and-hourly)
- ASP.NET Core webhook endpoint: [EventSub webhooks](eventsub.md#webhooks) and the [webhook host sample](samples.md#webhook-host)

## Project

- [Architecture](architecture.md): modules, conventions, JSON defaults, EventSub semantics, native AOT, API snapshots
- [Testing](testing.md): unit, contract, integration (Twitch CLI), package smoke, native AOT, public API snapshots, coverage validation
- [API coverage](coverage.md): every Helix endpoint and EventSub type with status, availability and evidence (generated from [api/coverage.json](api/coverage.json))
- [Progress](progress.md) and [roadmap](roadmap.md): verified state of the release candidate and what remains before 1.0.0
- [Releases](releases.md): versioning, RC to 1.0.0 checklist, publishing, maintenance
- [Project plan](project-plan.md) (original goals, in German)
- [Changelog](../CHANGELOG.md), [contributing](../CONTRIBUTING.md), [security](../SECURITY.md)
