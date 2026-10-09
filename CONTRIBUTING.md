# Contributing

Use the .NET 10 SDK with .NET 8 and .NET 10 runtimes. Run the README build/test commands and `pwsh ./tools/Test-ApiCoverage.ps1` before proposing a change. Keep credentials out of source, logs and fixtures. Live integration tests must be opt-in.

For each API change, inspect its official Twitch reference, implement all parameters and response fields, add meaningful contract/error tests, document usage and update the coverage matrix with evidence. Leave incomplete work marked partial. Preserve unknown wire discriminator values. Changes to the documentation inventory must be reviewed as API changes, not blindly accepted.

Use `pwsh ./tools/Update-ApiInventory.ps1 -Download` to refresh the local official references and manifest. Downloaded HTML is ignored by Git; source URLs and SHA-256 hashes are retained. Changed Helix documentation sections reset partial/complete entries to needs-review. Review all model/auth/availability changes, including EventSub changes, before merging. Do not copy Twitch credentials from examples.

Public API breaking changes require a versioning decision and migration notes. Keep dependencies small and avoid sync-over-async. Follow .editorconfig and docs/architecture.md.

After updating the inventory, run `pwsh ./tools/Update-ScopeConstants.ps1` to regenerate TwitchScopes from the official scope table. Do not maintain a hardcoded allow-list of scope prefixes: new Twitch capabilities can introduce new prefixes.
