# Releases and maintenance

Current version: `1.0.0-rc.1` (`<Version>` in `Directory.Build.props`). Nothing has been published yet: there is no GitHub repository and no NuGet package ID has been reserved.

## Versioning

TwitchSdk follows [Semantic Versioning 2.0](https://semver.org/). All six packages always share one version.

| Change | Version part |
| --- | --- |
| Removed or changed public members (any removed line in `tests/TwitchSdk.Tests/PublicApi/*.txt`), changed behavior callers rely on | Major |
| New endpoints, EventSub types, members or options; Twitch deprecations marked `[Obsolete]` | Minor |
| Bug fixes and model corrections without public API changes | Patch |

- Prereleases use `-rc.N` (or `-preview.N` for early builds of a later version). Release candidates may still change the public API when live verification uncovers a problem; every such change is listed in the changelog with migration notes.
- When Twitch deprecates an API, the SDK marks it `[Obsolete]` in a minor release. Removing it (also after Twitch removed it and answers HTTP 410) waits for the next major release.
- Every public API change goes through the snapshot workflow in [testing](testing.md#public-api-snapshots) and gets a `CHANGELOG.md` entry.

## RC to 1.0.0 checklist

- [ ] **Live verification** with a test account, a test channel and real credentials (keep them in environment variables or a secret store):
  - OAuth: client credentials; authorization code with `ParseAuthorizationCode`; device code with `WaitForDeviceAuthorizationAsync`; refresh with rotation; hourly validation; revocation; OpenID Connect with `ValidateIdTokenAsync` and `GetUserInfoAsync`.
  - Helix: reads and writes per group on the test channel (users, channels, streams, chat incl. Send Chat Message drop reasons, moderation, polls, predictions, schedule; Channel Points and Bits need an affiliate or partner channel; Extensions and Drops need an extension or organization).
  - EventSub: WebSocket welcome, subscriptions with preflight, notifications, revocation, keepalive and reconnect; webhooks on a public HTTPS callback (challenge, notifications, revocation, retries); a conduit with a WebSocket shard.
  - Chat: the chat bot sample, and IRC connect, join, send and reconnect.
  - Fix every difference, add a regression test, and update docs and the changelog.
  - Start with the automated read-only live checks (`TWITCHSDK_LIVE=1`, plus `TWITCH_CLIENT_ID`/`TWITCH_CLIENT_SECRET` for the app-token part); see [testing](testing.md#live-checks-against-twitch). The credential-free part already passes.
- [ ] **Repository**: create the GitHub repository, push all branches and history, and get every job green (test matrix, integration, pack with package smoke and native AOT, api-drift). Enable private vulnerability reporting and update [SECURITY.md](../SECURITY.md).
- [ ] **Package metadata**: set `RepositoryUrl`, `PackageProjectUrl` and the repository type in `Directory.Build.props`; make README links absolute so they work on nuget.org; inspect a packed `.nupkg` (license, readme, XML docs, `lib/net8.0` and `lib/net10.0`, dependencies).
- [ ] **Package IDs**: check that `TwitchSdk.*` is available on nuget.org and reserve the ID prefix for the owning account, or choose other IDs before the first upload.
- [ ] **Coverage**: refresh the inventory (`tools/Update-ApiInventory.ps1 -Download`), resolve any drift, pass `tools/Test-ApiCoverage.ps1 -RequireComplete` and regenerate `docs/coverage.md`.
- [ ] **Security review** of the items in [SECURITY.md](../SECURITY.md).
- [ ] **Release**: set `<Version>1.0.0</Version>`, date the changelog section, follow the publishing steps below.

## Publishing

**Automated (recommended):** `.github/workflows/release.yml` runs on a pushed tag `v<version>` (for example `v1.0.0-rc.1`). It checks that the tag matches `<Version>`, builds, tests, enforces the release gate, packs, pushes to NuGet with the repository secret `NUGET_API_KEY` and creates a GitHub release with the packages (marked prerelease for versions with a suffix). One-time setup: create a NuGet API key scoped to push `TwitchSdk.*`, store it as the `NUGET_API_KEY` secret, and protect the `nuget` environment with required reviewers. Then `git tag v1.0.0-rc.1 && git push origin v1.0.0-rc.1`.

**Manual:**

1. Set `<Version>` in `Directory.Build.props` and move the changelog's unreleased notes into a dated section for that version.
2. Build, test and pack from a clean tree:

   ```sh
   rm -rf artifacts
   dotnet build TwitchSdk.slnx -c Release
   dotnet test tests/TwitchSdk.Tests/TwitchSdk.Tests.csproj -c Release --no-build
   dotnet test tests/TwitchSdk.IntegrationTests/TwitchSdk.IntegrationTests.csproj -c Release --no-build
   pwsh ./tools/Test-ApiCoverage.ps1 -RequireComplete
   dotnet pack TwitchSdk.slnx -c Release --no-build -o artifacts/packages
   ```

3. Run the package smoke test and the native AOT run against `artifacts/packages` ([testing](testing.md#package-smoke-test-and-native-aot)).
4. Inspect the packages (for example with NuGet Package Explorer or `unzip -l artifacts/packages/TwitchSdk.Core.<version>.nupkg`).
5. Push with an API key scoped to pushing `TwitchSdk.*` and a short expiry. The key comes from a secret store or a CI secret into an environment variable; never write it into a file, a command you commit, or shell history you share.

   ```sh
   # NUGET_API_KEY is set from your secret store, for example a protected CI environment secret.
   dotnet nuget push "artifacts/packages/*.nupkg" --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json --skip-duplicate
   ```

   In PowerShell use `--api-key $env:NUGET_API_KEY`. Packages appear after nuget.org's validation and indexing; install the published version into a clean project to confirm.
6. Tag the commit and create the GitHub release with the changelog section as notes and the packages attached:

   ```sh
   git tag -a v1.0.0 -m "TwitchSdk 1.0.0"
   git push origin v1.0.0
   gh release create v1.0.0 artifacts/packages/*.nupkg --title "TwitchSdk 1.0.0" --notes-file release-notes.md
   ```

   `release-notes.md` is a temporary file with the version's changelog section. Add `--prerelease` for `-rc.N` versions.

CI builds and uploads the packages as a workflow artifact but does not publish them. A tag-triggered publishing workflow should read the key from a secret in a protected GitHub environment that requires approval.

## Maintenance

- The weekly `api-drift` workflow (Mondays, 05:17 UTC, also runnable manually) refreshes `docs/api/coverage.json` and `TwitchScopes` from dev.twitch.tv and fails with a diff summary on any change. Changed Helix sections reset to needs-review, new APIs are added as inventoried, and removed APIs stop the importer for manual review. Review the official change, update models, tests and docs, mark entries complete with evidence, regenerate `docs/coverage.md`, and release a minor (new APIs) or patch (fixes) version.
- Dependabot opens weekly pull requests for NuGet packages and GitHub Actions.
- Inventory changes are reviewed like API changes; see [CONTRIBUTING.md](../CONTRIBUTING.md).
