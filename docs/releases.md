# Releases and maintenance

Current version: **1.0.0-rc.1**, set in `Directory.Build.props`. Repository: [TwitchDock.NET](https://github.com/dermixer1305/TwitchDock.NET). GitHub release assets are NuGet-format packages for a local feed; **they are not published on nuget.org**.

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

5. The [release workflow](../.github/workflows/release.yml) runs the full reusable CI workflow: Windows/Linux tests on both frameworks, coverage gate, Twitch CLI integration tests, package smoke tests and native AOT. Only after all required jobs succeed does it check the tag/version match, pack the SDK, generate SHA-256 checksums and create the GitHub release.
6. Verify that the release is marked **prerelease**, contains all six `.nupkg` assets and `SHA256SUMS.txt`, and has working tutorial links. Install the release packages into a clean consumer project.

The workflow uses GitHub's per-run token with `contents: write` only in the publication job. It needs no NuGet secret and does not push anything to nuget.org. The GitHub release contains the version-specific bilingual notes, not the complete historical changelog.

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
- [ ] Check NuGet package name availability/ownership and configure publishing if distribution through nuget.org is desired.
- [ ] Set `1.0.0`, date the changelog and publish using the same verified release procedure.

## Optional future NuGet publication

NuGet publication is separate from this GitHub-only release. Before enabling it, check the availability and ownership of all `TwitchDock.*` package IDs; do not assume a prefix is reserved. Review package contents, metadata, dependency versions and README links. Configure an appropriately scoped publishing credential and a protected environment before adding an automated publishing job.

Never commit NuGet keys or Twitch credentials. A GitHub release does not require either.

## Maintenance

The weekly `api-drift` workflow refreshes the pinned inventory from Twitch's documentation. Changed entries become needs-review, new entries become inventoried, and removed entries stop the importer for manual review. Resolve changes in code, tests and documentation before marking them complete again.

Dependabot proposes dependency and GitHub Actions updates. Keep checks green, review public API changes and use the versioning rules above. See [CONTRIBUTING.md](../CONTRIBUTING.md).
