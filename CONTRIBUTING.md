# Contributing

Thanks for helping. Please read [architecture](docs/architecture.md) before larger changes and keep pull requests focused.

## Setup

Install the .NET 10 SDK (required by `global.json` for C# 14) and the .NET 8 runtime. Then:

```sh
dotnet build TwitchDock.slnx -c Release
dotnet test tests/TwitchDock.Tests/TwitchDock.Tests.csproj -c Release --no-build
pwsh ./tools/Test-ApiCoverage.ps1 -RequireComplete
```

Integration tests need the Twitch CLI; package smoke and native AOT runs need a packed build. Both are described in [testing](docs/testing.md).

## Never commit credentials

No access tokens, refresh tokens, client secrets, webhook secrets, extension secrets or stream keys in source, fixtures, logs, issues or screenshots. Fixtures use synthetic values. Samples read credentials from environment variables only. Tests must not need Twitch credentials; any future live test must be opt-in.

## Coding conventions

- Follow `.editorconfig`: UTF-8, LF, four-space indentation, file-scoped namespaces. The build treats warnings as errors and runs the trimming and AOT analyzers on all libraries.
- Public async methods end in `Async`, take `CancellationToken` last and use `ConfigureAwait(false)`. No sync-over-async.
- IDs and evolving discriminators (statuses, types, actions) are strings. Keep request and response models separate.
- Serialize only through source-generated metadata: register new models in the module's `JsonSerializerContext`. No reflection-based serialization.
- Init-only properties with a default coalesce null so JSON without the field keeps the default: `public IReadOnlyList<string> Items { get; init => field = value ?? []; } = [];`. Use nullable types for optional numbers, booleans and timestamps.
- Every endpoint declares its `TwitchAuthorizationRequirement` (scopes, alternative scopes, token kind, owning user). Every EventSub factory records required and alternative scopes, the authorizing user and the transports.
- Validate documented limits locally before sending; leave server-side facts (roles, grants, ownership) to Twitch.
- Types that hold credentials redact them in `ToString()`.
- Add no third-party dependencies. Keep classes small and focused; group by API area.
- Public API members should carry XML documentation where the name alone is not enough.

## Definition of done for an API

An endpoint or EventSub type/version is complete only when:

1. it is usable through the public SDK,
2. every documented parameter is supported,
3. request and response (or event) models contain every documented field,
4. its authorization rules are implemented,
5. its errors are handled and surfaced correctly,
6. automated tests exist and pass,
7. the user documentation describes it with an example.

Beta and deprecated APIs are marked as such in docs and code. Never mark partial work complete.

## Coverage evidence workflow

1. Read the official section in the pinned reference. `pwsh ./tools/Update-ApiInventory.ps1 -Download` refreshes the local copies (ignored by Git; URLs and SHA-256 hashes are kept in `docs/api/coverage.json`).
2. Implement the method or event and its models.
3. Add a fixture under `tests/TwitchDock.Tests/Fixtures` containing every documented field (Helix contract tests fail when a response field listed in the matrix is missing), plus tests for requests, authorization preflight and errors.
4. Document it in the group reference (`docs/helix-*.md` or `docs/eventsub-*.md`).
5. Update its entry in `docs/api/coverage.json`: `status: complete`, the availability and (for Helix) authorization review, and `evidence` paths under `src/`, `tests/` and `docs/`.
6. Run `pwsh ./tools/Test-ApiCoverage.ps1 -RequireComplete`, regenerate the report with `pwsh ./tools/New-CoverageReport.ps1` and commit `docs/coverage.md` with the change.

Inventory refreshes are reviewed like API changes, not accepted blindly: a changed Helix section resets its entry to needs-review, and removed entries stop the importer. After a refresh, run `pwsh ./tools/Update-ScopeConstants.ps1` to regenerate `TwitchScopes` from the official scope table; do not maintain a hand-written list of scope prefixes.

## Public API changes

`PublicApiTests` fails whenever the public surface changes. If the change is intended, regenerate the snapshots with `TWITCHDOCK_UPDATE_PUBLIC_API=1` ([testing](docs/testing.md#public-api-snapshots)), review the diff and add a `CHANGELOG.md` entry. Removed or changed members are breaking changes and need a versioning decision and migration notes ([releases](docs/releases.md#versioning)).

## Commits and pull requests

- Use Conventional Commits, as in the history: `feat(helix): ...`, `fix(eventsub): ...`, `docs: ...`, `test: ...`, `build: ...`, `ci: ...`, `chore: ...`, `refactor: ...`. Imperative mood, lower case, no trailing period; explain the why in the body.
- A pull request needs green CI, updated docs, coverage evidence for API work and a changelog entry for user-visible changes.
- Report security issues privately as described in [SECURITY.md](SECURITY.md), not in public issues.
