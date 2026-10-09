# Releases and maintenance

Current version: **1.0.0-rc.1**, set in `Directory.Build.props`. Repository: [TwitchDock.NET](https://github.com/dermixer1305/TwitchDock.NET). Every GitHub release carries the six NuGet packages, a zip bundle of them and SHA-256 checksums for use as a local feed. Publishing to nuget.org is prepared in the release workflow but **disabled until a maintainer enables it** ([setup](#nugetorg-publication)).

## Versioning

All six packages share one version and follow Semantic Versioning:

| Change | Version |
| --- | --- |
| Removed/changed public API or incompatible behavior | Major |
| New endpoints, events or compatible features | Minor |
| Compatible bug fixes | Patch |

Prereleases use `-rc.N` or `-preview.N`. The public API can still change before 1.0.0; document changes and migration guidance in the changelog. Earlier unpublished builds used `TwitchSdk.*`; this release uses `TwitchDock.*` and `AddTwitchDock`.

Twitch deprecations are marked `[Obsolete]` in a minor release and removed only in a major release. Public API changes require a reviewed [snapshot update](testing.md#public-api-snapshots).

## GitHub release procedure

1. Update the version and dated changelog. Add **English and German** notes at `docs/release-notes/<version>.md`.
2. Build, test and validate coverage. Test packed dependencies and inspect the resulting package metadata and contents as described in [testing](testing.md).
3. Push the reviewed commit to GitHub and confirm the branch's CI is green.
4. Create an annotated version tag on that commit and push it:

   ```sh
   git tag -a v1.0.0-rc.1 -m "TwitchDock.NET 1.0.0-rc.1"
   git push origin v1.0.0-rc.1
   ```

5. The [release workflow](../.github/workflows/release.yml) runs the full reusable CI workflow: Windows/Linux tests on both frameworks, coverage gate, Twitch CLI integration tests, package smoke tests and native AOT. Only after all required jobs succeed does it check the tag/version match, pack the SDK, bundle the six packages into `TwitchDock.NET-<version>-packages.zip`, generate SHA-256 checksums and create the GitHub release. The `github-packages` job then pushes the same packages to [GitHub Packages](#github-packages). If nuget.org publication is enabled, the `nuget` job waits for approval and pushes them to nuget.org.
6. Verify that the release is marked **prerelease**, contains all six `.nupkg` assets, the zip bundle and `SHA256SUMS.txt`, and has working tutorial links. Install the release packages into a clean consumer project.

The workflow uses GitHub's per-run token with `contents: write` only in the GitHub release job. It stores no NuGet secret; the optional nuget.org job obtains a short-lived key through trusted publishing. The GitHub release contains the version-specific bilingual notes, not the complete historical changelog.

Do not replace an already published version with different packages. Fix the problem and publish the next release candidate.

## RC to 1.0.0 checklist

- [x] Selected real-credential live checks: app token acquisition/validation/revocation, Helix reads, user device authorization, EventSub chat subscription/reception and Helix chat send. [Exact evidence and limits](live-verification.md).
- [x] Public repository, English/German onboarding, repository URLs in package metadata and private vulnerability reporting.
- [ ] Broader live verification with test accounts/channels: authorization code, implicit flow, complete OpenID Connect, token refresh/rotation, extended hourly validation, IRC, public webhook delivery/retries, conduits, WebSocket migration/reconnect and revocation.
- [ ] Live reads/writes across the remaining Helix groups, including account-restricted APIs where applicable. Channel Points/Bits, Extensions and Drops require suitable accounts or organizations. Fix discrepancies and add regression coverage.
- [ ] Run the actual interactive ChatBot sample through its complete onboarding and reply flow; the earlier live chat test used a local harness.
- [ ] Collect release-candidate feedback and resolve issues.
- [ ] Refresh the API inventory, resolve drift, pass `tools/Test-ApiCoverage.ps1 -RequireComplete`, and regenerate the coverage report.
- [ ] Review the security checklist in [SECURITY.md](../SECURITY.md). Do not claim an independent security audit without one.
- [ ] Check NuGet package name availability/ownership and complete the [nuget.org setup](#nugetorg-publication) if distribution through nuget.org is desired.
- [ ] Set `1.0.0`, date the changelog and publish using the same verified release procedure.

## GitHub Packages

Every release is also pushed to the repository's NuGet registry on GitHub Packages with the run's `GITHUB_TOKEN`; no setup is needed. The packages' repository URL links them to this repository, so they appear in its **Packages** section. After the first push, check on the package page that its visibility is **public**. GitHub's NuGet registry requires authentication even for public packages: consumers need a personal access token with `read:packages`. For everyone else the zip bundle on the GitHub release, or nuget.org, remains the simpler way to install. To push an existing release, run the **Release** workflow manually and enter its tag.

## nuget.org publication

The `nuget` job in the [release workflow](../.github/workflows/release.yml) pushes the packages of a GitHub release to nuget.org. It is skipped until it is configured, so tag releases keep working without it. It downloads the release assets, verifies them against `SHA256SUMS.txt` and pushes exactly those files; it never repacks. It uses [NuGet trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): GitHub's OIDC token is exchanged for an API key that is valid for about an hour, so no NuGet key is stored in the repository.

One-time setup by the maintainer:

1. Sign in to [nuget.org](https://www.nuget.org/) and confirm that all six `TwitchDock.*` package IDs are free or owned by you. Do not assume a prefix is reserved; an ID prefix reservation can be requested separately.
2. On nuget.org open **Trusted Publishing** and add a GitHub Actions policy: owner `dermixer1305`, repository `TwitchDock.NET`, workflow file `release.yml`, environment `nuget`.
3. In the GitHub repository open **Settings → Environments**, create `nuget`, add yourself as a required reviewer and limit deployments to tags matching `v*` plus the `main` branch (manual runs for an existing tag start from `main`).
4. Under **Settings → Secrets and variables → Actions → Variables** add `NUGET_USER` (your nuget.org user name, not your e-mail address) and `NUGET_PUBLISH` with the value `true`.

From then on each version tag creates the GitHub release first; the `nuget` job then waits for your approval in the workflow run and pushes the packages. To publish an earlier GitHub release (for example `v1.0.0-rc.1`), run the **Release** workflow manually under **Actions** and enter the tag. Reruns are safe because versions that already exist are skipped.

Before the first push, review package contents, metadata, dependency versions and README links (`dotnet pack` output or the release assets). A version on nuget.org can be unlisted but never replaced; publish fixes as a new version. Set `NUGET_PUBLISH` to anything other than `true` to stop publishing.

Never commit NuGet keys or Twitch credentials.

## Maintenance

The weekly `api-drift` workflow refreshes the pinned inventory from Twitch's documentation. Changed entries become needs-review, new entries become inventoried, and removed entries stop the importer for manual review. Resolve changes in code, tests and documentation before marking them complete again.

Dependabot proposes dependency and GitHub Actions updates weekly, grouped (Microsoft.Extensions, test tooling, actions) and only for releases at least seven days old. Microsoft.Extensions stays on the 8.x line so .NET 8 consumers keep the lowest dependency versions; moving to a newer major line is a deliberate versioning decision. Keep checks green, review public API changes and use the versioning rules above. See [CONTRIBUTING.md](../CONTRIBUTING.md).
