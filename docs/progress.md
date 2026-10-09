# Implementation progress (2026-10-09)

TwitchDock.NET is at **1.0.0-rc.1**. Every API in the pinned official documentation meets the definition of done. Selected real-credential tests passed and the public GitHub repository is configured. The packages are published on nuget.org. Broader live verification remains before 1.0.0. The original goals are in the [project plan](project-plan.md); next steps are in the [roadmap](roadmap.md).

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
- **DependencyInjection**: `AddTwitchDock`, `AddTwitchTokenValidation` (hosted, bounded transient retries), `AddTwitchIrc`.
- **Samples**: quickstart console app, EventSub chat bot, ASP.NET Core webhook host.

## Verified locally

On Windows with .NET SDK 10.0.401 (runtimes 8.0 and 10.0):

- `dotnet build TwitchDock.slnx -c Release`: 0 warnings, 0 errors (libraries, samples and tests; warnings are errors, AOT analyzers enabled).
- Unit and contract tests: 878 passed on net8.0 and 878 on net10.0.
- Integration tests against Twitch CLI 1.1.24: all 4 passed on each framework (mock API, mock EventSub WebSocket with reconnect, signed webhooks for every CLI-supported type, callback verification).
- `dotnet pack`: six `1.0.0-rc.1` packages. The package smoke test passed on net8.0 and net10.0 with JSON reflection disabled, and as native AOT executables (win-x64) on net8.0 and net10.0.
- The webhook host sample answered the Twitch CLI's `verify-subscription` challenge, routed `stream.online` and `channel.follow` v2, and rejected a delivery signed with another secret (403).
- The snippets in the README, quickstart, authentication and EventSub docs compile against the current API.
- `tools/Test-ApiCoverage.ps1 -RequireComplete` passes on Windows PowerShell 5.1.

CI defines these checks on Ubuntu and Windows (`.github/workflows/ci.yml`), plus native AOT on linux-x64 and a weekly `api-drift` workflow. The tag-triggered GitHub release requires the complete CI workflow to pass. Check [GitHub Actions](https://github.com/dermixer1305/TwitchDock.NET/actions) for the status of a particular commit.

Selected real Twitch checks passed: 5 automated live tests on each target framework, user authorization through the device flow, user-token validation and profile lookup, an enabled chat subscription, typed chat reception and a Helix chat send with `IsSent = true`. See [live verification](live-verification.md) for the scope and the distinction between the local harness and the sample.

## Not verified yet

- **Broader live Twitch API coverage**: restricted APIs, further OAuth/OIDC flows, refresh rotation, IRC, public webhooks, conduits and reconnect/revocation scenarios remain. CLI mocks lag behind some fields, scopes and versions.
- **Interactive chat sample**: builds; its new end-to-end onboarding has not been separately live tested. The underlying device login and chat operations were verified by a local harness.
- **NuGet publication**: all six packages are published on [nuget.org](https://www.nuget.org/profiles/DerMixer1305) through trusted publishing ([setup](releases.md#nugetorg-publication)). The `TwitchDock.*` ID prefix is not reserved yet; request a reservation from nuget.org.

## Known limitations

- Duplicate suppression (`MessageDeduplicator`) is in memory; multi-instance webhook receivers need their own durable inbox.
- `RefreshingTokenProvider` serializes refreshes within one process only.
- Guest Star endpoints and events are public beta; Twitch may change them without a version bump.
- The Tags endpoints are deprecated by Twitch and marked `[Obsolete]`.
