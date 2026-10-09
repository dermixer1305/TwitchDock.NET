# TwitchDock.NET

**An independent .NET SDK for the Twitch API, EventSub and chat.**

[English](https://github.com/dermixer1305/TwitchDock.NET/blob/main/README.md) · [Deutsch](https://github.com/dermixer1305/TwitchDock.NET/blob/main/README.de.md) · [Tutorial](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/tutorial.md) · [Documentation](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/README.md) · [Releases](https://github.com/dermixer1305/TwitchDock.NET/releases)

[![Build and test](https://github.com/dermixer1305/TwitchDock.NET/actions/workflows/ci.yml/badge.svg)](https://github.com/dermixer1305/TwitchDock.NET/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/dermixer1305/TwitchDock.NET/blob/main/LICENSE)

Build bots, channel tools and stream integrations in C# with typed requests, typed events and asynchronous methods. TwitchDock targets **.NET 8 and .NET 10** and supports trimming and native AOT.

This is an independent community project, written from scratch. It is not affiliated with or endorsed by Twitch and is not a fork or successor of TwitchLib.

## Release status

**1.0.0-rc.1 is a release candidate, not a stable 1.0 release.**

- **Pinned API coverage:** 149 Helix endpoints and 83 EventSub type/version combinations from the official documentation snapshot of 2026-10-09 have typed models, authorization rules, tests and documentation. Beta and deprecated APIs are explicitly marked. See the [coverage report](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/coverage.md).
- **Automated validation:** 878 unit and contract tests per target framework, Twitch CLI integration tests, public API snapshots and package/native AOT smoke checks.
- **Live verification:** app-token acquisition, validation and revocation; real Helix reads; device authorization and user-token validation; EventSub chat subscription, receipt of a real message and successful Helix chat send. See the [live-test report](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/live-verification.md).
- **Still pending:** broader live coverage, including IRC, public webhooks, reconnect scenarios, refresh rotation and restricted APIs. Packages are **not published on nuget.org**; use GitHub release assets or a local build.

## Packages

| Package | What it provides |
| --- | --- |
| `TwitchDock.Core` | HTTP transport, rate limits, bounded retries, pagination, token abstractions and authorization checks |
| `TwitchDock.Authentication` | OAuth flows, device login, OpenID Connect, token refresh, validation and revocation |
| `TwitchDock.Helix` | Typed REST API groups: users, streams, channels, moderation, chat, polls, Channel Points and more |
| `TwitchDock.EventSub` | Typed subscriptions and events, WebSocket client, webhook verification and routing, conduits and batching |
| `TwitchDock.Chat` | Chat over EventSub + Helix, plus IRC with reconnects and rate limiting |
| `TwitchDock.DependencyInjection` | `AddTwitchDock`, hosted token validation and registration; brings in all modules |

Public operations accept cancellation tokens. JSON serialization is source-generated. Runtime package dependencies are limited to the Microsoft.Extensions packages used for logging and hosting.

## Start with a working example

Install the **.NET 10 SDK** to build the repository. The libraries also target .NET 8.

```sh
git clone https://github.com/dermixer1305/TwitchDock.NET.git
cd TwitchDock.NET
dotnet build TwitchDock.slnx -c Release
```

Register your own application in the [Twitch developer console](https://dev.twitch.tv/console/apps). In **PowerShell**:

```powershell
$env:TWITCH_CLIENT_ID = Read-Host 'Your Twitch application client ID'
dotnet run --project samples/TwitchDock.ChatBot -c Release -f net10.0
```

In **Bash**:

```bash
read -r -p 'Your Twitch application client ID: ' TWITCH_CLIENT_ID
export TWITCH_CLIENT_ID
dotnet run --project samples/TwitchDock.ChatBot -c Release -f net10.0
```

Open the Twitch URL printed in the terminal, sign in and authorize reading and sending chat messages. The sample connects to **your own channel** by default. Send `!ping` in your channel chat and it replies `pong`. Press Ctrl+C to stop. No client secret is needed for this device-login example; access tokens are neither printed nor saved.

Follow the **[English tutorial](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/tutorial.md)** or **[deutsches Tutorial](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/tutorial.de.md)** for registration, API calls, package installation and troubleshooting.

## Install into your own project

Download all six `.nupkg` files from the [release](https://github.com/dermixer1305/TwitchDock.NET/releases/tag/v1.0.0-rc.1) into a local package folder, or build them:

```sh
dotnet pack TwitchDock.slnx -c Release -o artifacts/packages
```

Add that folder as a NuGet source while keeping nuget.org enabled for Microsoft dependencies. The tutorial has complete commands for both shells. Then run in your own project:

```sh
dotnet add package TwitchDock.DependencyInjection --version 1.0.0-rc.1
```

You can also reference projects in `src/` directly. Namespaces and package IDs use `TwitchDock`; earlier unpublished builds used `TwitchSdk`.

## Documentation

- [English tutorial](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/tutorial.md) / [Deutsches Tutorial](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/tutorial.de.md)
- [C# quickstart](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/quickstart.md), [authentication](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/authentication.md), [EventSub](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/eventsub.md), [IRC](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/chat-irc.md)
- [Runnable samples](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/samples.md): API quickstart, chat bot and ASP.NET Core webhook receiver
- [Reference index](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/README.md), [roadmap](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/roadmap.md), [changelog](https://github.com/dermixer1305/TwitchDock.NET/blob/main/CHANGELOG.md)

The detailed API reference is in English. The project overview and beginner tutorial are available in English and German.

## Build and contribute

```sh
dotnet build TwitchDock.slnx -c Release
dotnet test tests/TwitchDock.Tests/TwitchDock.Tests.csproj -c Release --no-build
pwsh ./tools/Test-ApiCoverage.ps1 -RequireComplete
```

See [testing](https://github.com/dermixer1305/TwitchDock.NET/blob/main/docs/testing.md) for CLI integration tests, optional live tests and AOT checks, and [contributing](https://github.com/dermixer1305/TwitchDock.NET/blob/main/CONTRIBUTING.md) for development guidelines. Include a minimal reproduction and redact credentials in bug reports.

Report vulnerabilities privately through [GitHub security reporting](https://github.com/dermixer1305/TwitchDock.NET/security/advisories/new). Read the [security policy](https://github.com/dermixer1305/TwitchDock.NET/blob/main/SECURITY.md).

## License

[MIT](https://github.com/dermixer1305/TwitchDock.NET/blob/main/LICENSE). Twitch is a trademark of Twitch Interactive, Inc. This project has no affiliation with or endorsement from Twitch. Twitch's API terms and authorization requirements still apply.
