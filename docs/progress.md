# Implementation progress — 2026-10-09

The original [project plan](project-plan.md) remains the goal. The current foundation is not a finished SDK or a 1.0 release.

## Verified locally

- All six modules and the executable quickstart build for net8.0 and net10.0 with warnings treated as errors (earlier environment with the .NET 10 SDK).
- 279 unit/contract tests passed on each target in that environment (558 test executions). Coverage includes concurrent refresh, OAuth encoding and metadata validation, scope/identity preflight and alternative scopes, concurrently extended rate-limit waits, safe retries, cursor cycles, public calendar text without token acquisition, webhook tampering, deduplication, migration, keepalive expiry, hourly validation, DI and reviewed endpoint contracts.
- After adding Hype Train, all 287 tests pass on net8.0 (SDK 8.0.425, build with `-p:TargetFrameworks=net8.0`). The current developer machine has no .NET 10 SDK, so net10.0 has not been re-verified for this change; CI covers both targets.
- Six local 0.1.0-alpha.1 NuGet packages were created under artifacts/packages.
- A separate consumer restored these actual packages and ran on both frameworks with JSON reflection disabled. This verifies packaging and source-generated serialization, not native AOT compilation.
- Coverage evidence validation passes. The stable-release gate correctly rejects the current incomplete coverage.
- The local repository was initialized on main. No commit, remote repository, external publication, credentialed Twitch call or CI execution has been performed.

## Current coverage

149 Helix endpoint definitions and 83 EventSub type/version pairs are inventoried from official documentation, with source URLs/hashes. Seventy-four Helix endpoints now meet the per-endpoint definition of done against the pinned reference: complete request/response models, authorization rules, errors, passing tests and examples. This includes the earlier reviewed groups, all Users/Whispers/Channels/Streams/Subscriptions/Bits/Channel Points/Polls/Predictions/Schedule/Conduits/Hype Train endpoints, Send Chat, List EventSub and Delete EventSub. Generic Create EventSub and the dedicated chat event model remain **partial**, particularly for subscription-specific typed conditions and authorization rules. The other 74 Helix entries and 82 EventSub entries are inventoried only. Availability reviews record extension ownership, Channel Points eligibility, Whisper phone verification and non-recurring schedule restrictions; four Guest Star event versions are public beta. Remaining availability reviews are open. Credentialed integration remains a release-level requirement.

The HTML importer now accounts for inconsistent upstream table column order, singular headings, optional question marks in Required headers, and the upstream `Paramters` typo. Extracted schema fields are review inputs, not proof of complete models. The EventSub models reference is pinned alongside the subscription catalog. Changes to previously implemented Helix sections reset their status to needs-review; removed entries stop the importer for manual review.

Scope extraction now reads the actual scope-table entries without a prefix allow-list. This corrected the previously missed editor:manage:clips scope: the pinned table contains 81 names, exposed through generated TwitchScopes constants. Legacy documented scope names remain in that catalog without implying support for obsolete transports.

## Next work

1. Complete typed subscription conditions, subscription-specific authorization and the chat event review; generic Create EventSub remains partial. Its transport envelope is reviewed, request/response transport types are separate, listing has a forward paginator and HTTP 409 exposes ExistingSubscriptionId.
2. Add Moderation, remaining Chat and the other inventoried groups. Keep method groups small and reuse independent response-fixture checks against the official matrix. Conduits now include explicit per-shard failures in HTTP 202 responses and app-only authorization, including for WebSocket shards. Schedule includes public iCalendar, vacation settings, recurring-series caveats, string-encoded minute durations and segment pagination. ContractAssertions preserves nulls during round trips to detect accidentally omitted nullable fields.
3. Expand typed EventSub conditions/events and provide the HTTP webhook hosting adapter with durable inbox integration.
4. Complete remaining OAuth flows, broader failure tests, native AOT checks, API compatibility checks and full documentation before publishing.

## Local execution notes

Use `-m:1 -p:UseSharedCompilation=false` for builds in the current restricted environment. The sandbox's testhost communication stalled; test execution succeeded with the approved `dotnet test` escalation. Ordinary restore/build used cached dependencies with `-p:NuGetAudit=false` because network access is restricted; CI retains normal audit behavior. API downloads needed network escalation. Package-consumer restore used artifacts/smoke-cache rather than writing to the global NuGet cache. No automatic approval review rejected an action.

When repacking an unchanged prerelease version during development, use a fresh workspace-local package cache for package smoke tests so an older package with the same ID/version cannot be reused. The package consumer is intentionally outside the source-project solution and has only a package reference.

