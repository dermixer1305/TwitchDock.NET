# Releases and maintenance

Current version: `0.1.0-alpha.1` in Directory.Build.props. Use Semantic Versioning; 0.x development may change public APIs and changes must be recorded. 1.x requires compatibility checks and migration notes for breaking changes.

Before publishing:

1. Run builds and tests on both target frameworks and CI operating systems.
2. Review the official API snapshot, all exceptions and coverage evidence. A stable release must pass `pwsh ./tools/Test-ApiCoverage.ps1 -RequireComplete`.
3. Complete credentialed integration and AOT checks, security review and package identity/license/ownership review.
4. Pack, inspect nuspec metadata and assemblies, install into a clean sample application, and review the changelog.
5. Configure repository URLs, source links, package ownership and a secret-protected publishing environment. Publish only reviewed versioned artifacts; never include secrets in commands or repository files.

CI currently builds and uploads packages. It does not publish them. No GitHub remote or NuGet account has been configured.

The inventory updater is the maintenance entry point. Automated scheduled drift monitoring, schema diffs and notifications remain to be added after the repository is established. Removed inventory entries currently fail refresh for manual review.
