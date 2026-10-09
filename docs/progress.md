# Implementation progress (2026-10-09)

TwitchSdk is at **1.0.0-rc.1**. Every API in the pinned official documentation meets the definition of done; what remains before 1.0.0 needs the maintainer's accounts (live credentials, GitHub, NuGet). The original goals are in the [project plan](project-plan.md); next steps are in the [roadmap](roadmap.md).

## Coverage

The [coverage matrix](api/coverage.json) inventories the official documentation snapshot of 2026-10-09 with source URLs and hashes. Rendered report: [coverage.md](coverage.md).

| Inventory | Entries | Complete | Availability notes |
| --- | ---: | ---: | --- |
| Helix endpoints | 149 | 149 | 113 public, 12 public beta (Guest Star), 11 extension owner, 6 affiliate/partner channel points, 2 game or organization owner (Drops), 2 schedule segments with non-recurring restrictions, 2 deprecated (Tags), 1 verified-phone sender (whispers) |
| EventSub type/versions | 83 | 83 | 77 public, 4 public beta (Guest Star), 1 extension owner, 1 game or organization owner |
| Scopes | 81 | n/a | Generated into `TwitchScopes` from the official scope table |

"Complete" means: public SDK method or event, every documented parameter and response field, authorization handling, errors, passing tests and user documentation, with linked evidence. `tools/Test-ApiCoverage.ps1 -RequireComplete` passes.

## Delivered

- **Core**: transport with bounded retries, shared rate-limit coordination, one refresh on 401, error mapping, pagination with cycle protection, scope/identity preflight, loopback-only plain HTTP for local mocks, empty-string timestamp converter.
- **Authentication**: client credentials, authorization code, implicit grant, device code with `WaitForDeviceAuthorizationAsync`, callback parsing with constant-time state checks, OpenID Connect (`CreateOpenIdAuthorizationUri`, `ValidateIdTokenAsync` with RS256/JWKS, `GetUserInfoAsync`), validation, revocation, `RefreshingTokenProvider`, `TokenValidationLoop`.
- **Helix**: all groups, including moderation, chat, Extensions with Extension JWTs, Drops entitlements, Guest Star (beta), Tags (obsolete), content classification labels, authorization by user and custom Power-ups.
- **EventSub**: typed factories and events for all 83 type/versions, registry, router, `TryReadEvent`, WebSocket client with migration, reconnect and optional loopback endpoint, webhook verifier and handler, batching for `drop.entitlement.grant`.
- **Chat**: `TwitchChatClient` over EventSub + Helix; IRC transport (`TwitchIrcClient`, IRCv3 parser, typed views, router, rate limiters, WebSocket/TCP connections).
- **DependencyInjection**: `AddTwitchSdk`, `AddTwitchTokenValidation` (hosted, bounded transient retries), `AddTwitchIrc`.
- **Samples**: quickstart console app, EventSub chat bot, ASP.NET Core webhook host.

## Verified locally

On Windows with .NET SDK 10.0.401 (runtimes 8.0 and 10.0):

- `dotnet build TwitchSdk.slnx -c Release`: 0 warnings, 0 errors (libraries, samples and tests; warnings are errors, AOT analyzers enabled).
- Unit and contract tests: 771 passed on net8.0 and 771 on net10.0.
- Integration tests against Twitch CLI 1.1.24: all 4 passed on each framework (mock API, mock EventSub WebSocket with reconnect, signed webhooks for every CLI-supported type, callback verification).
- `dotnet pack`: six `1.0.0-rc.1` packages. The package smoke test passed on net8.0 and net10.0 with JSON reflection disabled, and as native AOT executables (win-x64) on net8.0 and net10.0.
- The webhook host sample answered the Twitch CLI's `verify-subscription` challenge, routed `stream.online` and `channel.follow` v2, and rejected a delivery signed with another secret (403).
- The snippets in the README, quickstart, authentication and EventSub docs compile against the current API.
- `tools/Test-ApiCoverage.ps1 -RequireComplete` passes on Windows PowerShell 5.1.

CI defines the same checks on Ubuntu and Windows (`.github/workflows/ci.yml`), plus a native AOT run on linux-x64 and the weekly `api-drift` workflow. They have not run on GitHub yet because no repository exists.

## Not verified yet

- **Live Twitch API**: no request has been made with real credentials. Contract tests use fixtures built from the official reference, and integration tests use the Twitch CLI mocks, which lag behind the reference for some fields, scopes and versions.
- **Chat bot sample**: builds, but has not run against Twitch (needs a bot account and token).
- **Publication**: no GitHub repository, no NuGet package ID reservation, nothing published.

## Known limitations

- Duplicate suppression (`MessageDeduplicator`) is in memory; multi-instance webhook receivers need their own durable inbox.
- `RefreshingTokenProvider` serializes refreshes within one process only.
- Guest Star endpoints and events are public beta; Twitch may change them without a version bump.
- The Tags endpoints are deprecated by Twitch and marked `[Obsolete]`.
