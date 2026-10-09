# TwitchSdk

An independent Twitch SDK for .NET 8 and .NET 10. This is an early development version, not a fork of TwitchLib and not yet a complete or production-ready SDK.

The authoritative scope and implementation status are tracked in [the coverage matrix](docs/api/coverage.json). An inventoried API is **not** an implemented API. See [the roadmap](docs/roadmap.md) and [architecture](docs/architecture.md).

## Build

Install the .NET 10 SDK and .NET 8 runtime, then run:

```sh
dotnet restore TwitchSdk.slnx
dotnet build TwitchSdk.slnx -c Release --no-restore
dotnet test TwitchSdk.slnx -c Release --no-build
dotnet pack TwitchSdk.slnx -c Release --no-build -o artifacts/packages
```

All modules target both frameworks. Tests use simulated HTTP responses and do not require Twitch credentials. Never commit access tokens, refresh tokens, client secrets, or webhook secrets.

See the [quickstart](docs/quickstart.md) for users, streams, OAuth and chat, [verified progress](docs/progress.md), [security notes](SECURITY.md), and [release checklist](docs/releases.md). The local packages use the provisional version `0.1.0-alpha.1`; none have been published.
