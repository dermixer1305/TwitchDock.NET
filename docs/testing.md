# Testing

All automated tests run offline or against local Twitch CLI mock servers; none needs Twitch credentials. Install the .NET 10 SDK (required by `global.json` and `LangVersion` 14) and the .NET 8 runtime to run the `net8.0` targets.

| Suite | Project | Needs | CI job |
| --- | --- | --- | --- |
| Unit, contract and public API tests | `tests/TwitchSdk.Tests` | .NET SDK only | `test` (Ubuntu and Windows × net8.0 and net10.0) |
| Integration tests | `tests/TwitchSdk.IntegrationTests` | Twitch CLI (skipped when missing) | `integration` |
| Package smoke and native AOT | `tests/TwitchSdk.PackageSmoke` (outside the solution) | Packed `artifacts/packages`; a native toolchain for AOT | `pack` |
| Coverage evidence and release gate | `tools/Test-ApiCoverage.ps1` | PowerShell | `test` |

## Unit and contract tests

```sh
dotnet build TwitchSdk.slnx -c Release
dotnet test tests/TwitchSdk.Tests/TwitchSdk.Tests.csproj -c Release --no-build              # net8.0 and net10.0
dotnet test tests/TwitchSdk.Tests/TwitchSdk.Tests.csproj -c Release --no-build -f net8.0    # one framework
```

The suite has 771 tests per target framework. HTTP responses are simulated (`TestHttpHandler`) and time is virtual (`ManualTimeProvider`). Contract tests compare every documented response field with independent JSON fixtures in `tests/TwitchSdk.Tests/Fixtures`, assert request routes, query and body encoding, check authorization preflight and error mapping, and round-trip nulls so a forgotten nullable field is detected. Other tests cover retries, rate limits, pagination, token refresh and validation, OAuth callbacks and OpenID Connect, EventSub WebSocket migration and keepalive expiry, webhook verification and routing, IRC parsing and the IRC client, dependency injection and the hosted validation service. A reflection test guards that public init-only properties keep their defaults when JSON omits a field.

## Integration tests (Twitch CLI)

`tests/TwitchSdk.IntegrationTests` runs the SDK against [Twitch CLI](https://dev.twitch.tv/docs/cli/) 1.1.24:

- `MockApiTests` starts `twitch mock-api start`, gets a user token from the mock's authorization endpoint and reads Helix responses through `TwitchHttpOptions.BaseAddress = http://localhost:<port>/mock/`.
- `WebSocketCliTests` starts `twitch event websocket start-server`, receives the welcome and a typed `channel.follow` v2 notification, and follows `twitch event websocket reconnect` to a migrated session.
- `WebhookCliTests` sends CLI-signed `twitch event trigger` deliveries for every type the CLI can generate through `EventSubWebhookVerifier` into the typed registry, and answers `twitch event verify-subscription` with `EventSubWebhookHandler`.

Install the CLI:

```sh
# Windows
winget install Twitch.TwitchCLI

# Linux x64 (the CI job does the same)
curl -fsSL -o twitch-cli.tar.gz https://github.com/twitchdev/twitch-cli/releases/download/v1.1.24/twitch-cli_1.1.24_Linux_x86_64.tar.gz
mkdir -p twitch-cli && tar -xzf twitch-cli.tar.gz -C twitch-cli
export TWITCH_CLI="$(find "$PWD/twitch-cli" -type f -name twitch | head -n 1)"
```

Other platforms: see the CLI's [releases](https://github.com/twitchdev/twitch-cli/releases). The tests locate the executable through `TWITCH_CLI` (full path), then `PATH`, then the winget package folder, so a fresh winget install works before a new shell picks up the alias. Without the CLI every test is reported as skipped, not failed.

```sh
dotnet test tests/TwitchSdk.IntegrationTests/TwitchSdk.IntegrationTests.csproj -c Release
```

Both target frameworks run in parallel, so the tests serialize CLI use with a lock file in the temp directory. CLI 1.1.24 sometimes panics in its own update check after finishing; the tests therefore assert on delivered messages and verdict lines instead of exit codes. The CLI mock API rejects scopes introduced after its release, so `MockApiTests` requests only older scopes.

## Package smoke test and native AOT

`tests/TwitchSdk.PackageSmoke` consumes the **packed** `TwitchSdk.DependencyInjection` package with JSON reflection disabled. Its `NuGet.config` maps `TwitchSdk.*` to `artifacts/packages` only, so a public package with the same ID cannot be picked up.

```sh
dotnet pack TwitchSdk.slnx -c Release -o artifacts/packages
dotnet restore tests/TwitchSdk.PackageSmoke/TwitchSdk.PackageSmoke.csproj
dotnet run --project tests/TwitchSdk.PackageSmoke/TwitchSdk.PackageSmoke.csproj -c Release -f net8.0 --no-restore
dotnet run --project tests/TwitchSdk.PackageSmoke/TwitchSdk.PackageSmoke.csproj -c Release -f net10.0 --no-restore
```

NuGet caches packages by ID and version. After repacking an unchanged version, restore into a fresh folder (for example `--packages artifacts/smoke-cache`) so an older build of `1.0.0-rc.1` is not reused.

Native AOT publishes the same program as a native executable and runs it:

```sh
dotnet publish tests/TwitchSdk.PackageSmoke/TwitchSdk.PackageSmoke.csproj -c Release -f net10.0 -r linux-x64 -p:PublishAot=true -o artifacts/aot/net10.0
./artifacts/aot/net10.0/TwitchSdk.PackageSmoke
```

Use `-r win-x64` on Windows; repeat with `-f net8.0`. The output ends with `native AOT: True`. Native AOT needs the platform toolchain: on Linux clang and the zlib development package, on Windows the Visual Studio C++ build tools ("Desktop development with C++") with `vswhere.exe` on `PATH` (it lives in `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer`). All library projects set `IsAotCompatible`, so trimming and AOT analyzer warnings already fail the normal build.

## Public API snapshots

`PublicApiTests` compares the public surface of each assembly with `tests/TwitchSdk.Tests/PublicApi/<assembly>.txt` and fails on any added or removed member. After an intended change, regenerate the snapshots and review the diff like code:

```sh
TWITCHSDK_UPDATE_PUBLIC_API=1 dotnet test tests/TwitchSdk.Tests/TwitchSdk.Tests.csproj -c Release -f net10.0 --filter "FullyQualifiedName~PublicApiTests"
git diff tests/TwitchSdk.Tests/PublicApi
```

In PowerShell set `$env:TWITCHSDK_UPDATE_PUBLIC_API = '1'` first and remove it afterwards. Then run the full suite on both frameworks without the variable. Removed or changed lines are breaking changes: they need a versioning decision and a CHANGELOG entry ([releases](releases.md)).

## Coverage validation

```sh
pwsh ./tools/Test-ApiCoverage.ps1 -RequireComplete   # validate docs/api/coverage.json and the release gate
pwsh ./tools/New-CoverageReport.ps1                   # regenerate docs/coverage.md
```

On Windows PowerShell 5.1 use `powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-ApiCoverage.ps1 -RequireComplete` (same for the report). The validator rejects duplicate IDs, unknown statuses, missing official sources and evidence paths outside the repository or missing on disk. A `complete` entry needs reviewed availability (and authorization for Helix) plus `src/`, `tests/` and `docs/` evidence. `-RequireComplete` fails while any entry is neither `complete` nor `excluded`. The unit tests read the same matrix: Helix fixtures must contain every documented response field, and every registered EventSub definition must be inventoried. The weekly `api-drift` workflow refreshes the inventory from the live documentation and fails on any change; see [releases](releases.md#maintenance).

## Live checks against Twitch

`LiveTwitchTests` in the integration project calls the real Twitch services. It is opt-in because it needs network access:

```sh
TWITCHSDK_LIVE=1 dotnet test tests/TwitchSdk.IntegrationTests/TwitchSdk.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~LiveTwitchTests"
```

Without credentials it checks the real EventSub WebSocket welcome, the public iCalendar path without a token, fetching and parsing Twitch's OpenID signing keys, and the error mapping for invalid client credentials and tokens (verified on 2026-10-09). With `TWITCH_CLIENT_ID` and `TWITCH_CLIENT_SECRET` of your own app it also acquires an app token, validates it and reads users, games, search, streams, channels, emotes, badges, cheermotes, content labels, videos, clips, teams, EventSub subscriptions and conduits, then revokes the token. All live checks are read-only.

## Not automated

Write operations, user-token flows and EventSub notifications against the real Twitch API need a test account and channel; they remain the manual checklist in [releases](releases.md#rc-to-100-checklist).
