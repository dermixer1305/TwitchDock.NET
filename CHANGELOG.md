# Changelog

## 0.1.0-alpha.1 — Unreleased

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

This is a partial foundation. Complete API coverage, live integration validation and stable publication remain open.
