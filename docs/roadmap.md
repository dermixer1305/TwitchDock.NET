# Delivery roadmap

The project plan targets a complete, stable SDK. The current 0.1.0-alpha.1 foundation is an intermediate milestone. It must not be marketed or released as 1.0.

## Current baseline

- Six modules multi-target .NET 8 and .NET 10.
- Pinned official documentation inventory: 149 Helix endpoints, 83 EventSub type/version pairs, 81 scope names (2026-10-09).
- HTTP pipeline, OAuth requests/refresh gate and validation metadata, scope/identity preflight, 75 distinct Helix endpoints (74 reviewed complete, 1 partial), EventSub WebSocket infrastructure and webhook verifier, typed chat, DI.
- Offline unit and contract tests on both frameworks; CI and local NuGet packing.

## Remaining work toward 1.0

1. Individually review availability and authorization for every inventory entry; distinguish GA, beta, restricted/extension-owner functions, removed APIs and deprecated fields. Audit all request/response schemas against official tables. Pin an explicit release documentation snapshot.
2. Implement and test the remaining Helix groups: Extensions, Guest Star, Moderation and remaining Chat methods. Ads, Analytics, Games, Search, Goals, Raids, Clips, Videos, Charity, Teams, Channels, Streams, Subscriptions, Bits, Channel Points, Polls, Predictions, Users, Whispers, Schedule, Conduits and Hype Train have completed per-endpoint review. Send Chat and List/Delete EventSub are reviewed; generic Create EventSub remains partial pending subscription-specific typed conditions and permission rules.
3. Add typed conditions/events for all EventSub type/version pairs, typed subscription routing and hosting adapters. Only channel.chat.message currently has a dedicated event model.
4. Expand OAuth into automatic device-code polling, implicit callback parsing, reviewed OpenID Connect support where in scope, durable token storage integration and automatic hosting integration for hourly validation.
5. Expand chat events and moderation, then evaluate documented IRC requirements explicitly. The current transport is Helix + EventSub. PubSub is not planned.
6. Extend the existing virtual-time keepalive/hourly-validation tests with reconnect failures and close-code policy; add network integration tests with opt-in credentials and Twitch CLI; further schema fixtures; fuzzing/resource limits; AOT and public API compatibility tests.
7. Complete API reference/examples and security/release review, choose GitHub owner/repository and verified available package identity, publish prereleases, then release 1.0 only after every GA entry meets the definition of done.

## Definition of done

An API is complete only when its public method/event, every documented parameter and response field, authorization handling, errors, passing tests and user documentation are all reviewed. Source/test/doc evidence must be linked in coverage.json. `tools/Test-ApiCoverage.ps1 -RequireComplete` intentionally fails until the release gate is met. Inventory totals are not implementation coverage percentages.


