# Roadmap

The [project plan](project-plan.md) targets a complete, stable SDK. The current state is documented in [progress](progress.md).

## Done: 1.0.0-rc.1

- Pinned official documentation inventory (2026-10-09): 149 Helix endpoints, 83 EventSub type/versions, 81 scopes, all reviewed for availability and authorization.
- Every inventory entry meets the definition of done, including beta (Guest Star) and deprecated (Tags) APIs, which are clearly marked.
- All OAuth flows including device code polling and OpenID Connect, hosted token validation, typed EventSub with router and webhook handler, chat over EventSub and IRC, dependency injection.
- Quality gates: unit and contract tests on both frameworks, Twitch CLI integration tests, public API snapshots, AOT analyzers and native AOT smoke runs, coverage release gate, weekly API drift check, Dependabot.
- Documentation for every group, samples and release process.

## Remaining before 1.0.0

These steps need the maintainer's accounts; the checklist with details is in [releases](releases.md#rc-to-100-checklist).

1. **Live verification** with real Twitch credentials across the main flows (OAuth grants, Helix reads and writes, EventSub WebSocket and webhooks, conduits, IRC). Fix and test every difference from the documentation; record intended API changes in the changelog.
2. **Repository**: create the GitHub repository, push, get every CI job green on GitHub, enable private vulnerability reporting, add repository URLs to the package metadata and make README links resolve on nuget.org.
3. **Publication**: check and reserve the `TwitchSdk.*` package IDs (or choose others), publish the release candidate, collect feedback, then release 1.0.0.

## After 1.0.0

- **Maintenance through API drift**: the weekly `api-drift` workflow compares the inventory with the live documentation. A changed endpoint is reset to needs-review, a new one is added as inventoried, a removed one stops the importer for manual review; any drift fails the job and shows the diff. Each change is implemented, tested, documented and marked complete with evidence before the next release.
- **Twitch deprecations** become `[Obsolete]` members in a minor release and are removed only in a major release (see [versioning](releases.md#versioning)).
- **Dependencies** are updated through Dependabot pull requests for NuGet packages and GitHub Actions.
- Candidates that are not scheduled: a pluggable durable store for webhook deduplication and an optional ASP.NET Core integration package.

## Definition of done

An API is complete only when its public method or event, every documented parameter and response field, authorization handling, errors, passing tests and user documentation are reviewed. Source, test and documentation evidence is linked in `coverage.json`. `tools/Test-ApiCoverage.ps1 -RequireComplete` fails CI as soon as any entry is neither complete nor explicitly excluded. Inventory totals are scope, not implementation percentages.
