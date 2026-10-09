# TwitchSdk

An independent, asynchronous Twitch SDK for .NET 8 and .NET 10: the Helix REST API, EventSub (WebSocket, webhooks, conduits), chat (EventSub + Helix or IRC), OAuth 2.0 and OpenID Connect, and dependency injection. It is written from scratch; it is not a fork of TwitchLib and shares no code or architecture with it.

## Status: 1.0.0-rc.1 (release candidate)

- **Complete coverage of the pinned official documentation** (snapshot of 2026-10-09): all 149 Helix endpoints, including the 12 Guest Star beta endpoints and the 2 deprecated Tags endpoints, and all 83 EventSub subscription types/versions meet the definition of done: typed public API, every documented parameter and response field, authorization rules, error handling, tests and documentation. See the [coverage report](docs/coverage.md).
- **Tested offline and locally**: 771 unit and contract tests on each of net8.0 and net10.0, integration tests against the Twitch CLI mock API, mock EventSub WebSocket server and CLI-signed webhooks, public API snapshots, and a native AOT run of the packed SDK.
- **Not done yet**: verification against the live Twitch API with real credentials, the public GitHub repository, and reserving and publishing the NuGet package IDs. Until then the packages are local build artifacts. See the [roadmap](docs/roadmap.md).

## Features

| Package | Contents |
| --- | --- |
| `TwitchSdk.Core` | HTTP transport with bounded retries and shared rate-limit handling, pagination, `TwitchApiException`, token abstractions, scope constants and local authorization preflight |
| `TwitchSdk.Authentication` | Client credentials, authorization code, implicit grant, device code with polling, OpenID Connect (ID token validation, UserInfo), callback parsing with constant-time state checks, serialized refresh, hourly validation, revocation |
| `TwitchSdk.Helix` | Every Helix group: users, channels, streams, chat, moderation, Channel Points, Bits, subscriptions, polls, predictions, schedule, clips, videos, extensions (with Extension JWTs), entitlements, Guest Star, conduits and more |
| `TwitchSdk.EventSub` | Typed subscription factories and events for every type, event registry, router, WebSocket client with migration and reconnects, webhook verifier and framework-independent webhook handler, batching |
| `TwitchSdk.Chat` | `TwitchChatClient` over EventSub + Helix, and an IRC transport (`TwitchIrcClient`, IRCv3 parser, typed views, router, rate limiters, WebSocket or TCP) |
| `TwitchSdk.DependencyInjection` | `AddTwitchSdk`, hosted token validation (`AddTwitchTokenValidation`), `AddTwitchIrc` |

Design: `async` APIs with `CancellationToken` everywhere, source-generated JSON (no reflection; trimming and native AOT compatible), local scope and identity checks before requests, redacted credentials in `ToString()`, no hidden background work, and no third-party dependencies: only `Microsoft.Extensions.Logging.Abstractions`, plus `Microsoft.Extensions.Http` and `Microsoft.Extensions.Hosting.Abstractions` in the DI package.

## Install

The packages are not on NuGet yet. Reference the projects in `src/` from a clone, or pack them into a local feed:

```sh
dotnet pack TwitchSdk.slnx -c Release -o artifacts/packages
dotnet nuget add source "$PWD/artifacts/packages" --name twitchsdk-local
dotnet add package TwitchSdk.DependencyInjection --version 1.0.0-rc.1
```

Building needs the .NET 10 SDK (C# 14); the libraries target net8.0 and net10.0.

## Quickstart

App token and a Helix call:

```csharp
using Microsoft.Extensions.DependencyInjection;
using TwitchSdk.Authentication;
using TwitchSdk.Core;
using TwitchSdk.DependencyInjection;
using TwitchSdk.Helix;

var clientId = Environment.GetEnvironmentVariable("TWITCH_CLIENT_ID") ?? throw new InvalidOperationException("Set TWITCH_CLIENT_ID.");
var clientSecret = Environment.GetEnvironmentVariable("TWITCH_CLIENT_SECRET") ?? throw new InvalidOperationException("Set TWITCH_CLIENT_SECRET.");

var services = new ServiceCollection();
services.AddTwitchSdk(new TwitchHttpOptions { ClientId = clientId }, sp =>
{
    var oauth = sp.GetRequiredService<TwitchOAuthClient>();
    return new RefreshingTokenProvider((_, ct) => oauth.GetAppTokenAsync(clientId, clientSecret, ct));
});
using var provider = services.BuildServiceProvider();
var helix = provider.GetRequiredService<HelixClient>();

var users = await helix.GetUsersAsync(new() { Logins = ["twitchdev"] });
Console.WriteLine(users.Data[0].DisplayName);
```

EventSub over WebSocket with typed events (needs a user token, for example from the [device code flow](docs/authentication.md#device-code)):

```csharp
using TwitchSdk.EventSub;

var router = new EventSubEventRouter()
    .On(EventSubEvents.ChannelChatMessageV1, (chat, _, _) => { Console.WriteLine($"{chat.ChatterUserName}: {chat.Message.Text}"); return Task.CompletedTask; })
    .On(EventSubEvents.StreamOnlineV1, (online, _, _) => { Console.WriteLine($"{online.BroadcasterUserName} is live"); return Task.CompletedTask; });

var socket = provider.GetRequiredService<EventSubWebSocketClient>();
await socket.RunAsync(
    async (session, resubscribe, ct) =>
    {
        if (!resubscribe) return; // a migrated session keeps its subscriptions
        await helix.SubscribeWebSocketAsync(EventSubSubscriptions.ChannelChatMessageV1(broadcasterId, botUserId), session.Id, ct);
        await helix.SubscribeWebSocketAsync(EventSubSubscriptions.StreamOnlineV1(broadcasterId), session.Id, ct);
    },
    (message, ct) => router.DispatchAsync(message, ct),
    cancellationToken);
```

Webhooks: the handler verifies the signature on the raw body, answers the challenge, drops duplicates and dispatches to the same router:

```csharp
var handler = new EventSubWebhookHandler(new EventSubWebhookVerifier(webhookSecret), router);
var result = await handler.HandleAsync(EventSubWebhookRequest.FromHeaders(name => request.Headers[name].ToString(), rawBody), cancellationToken);
// Write result.StatusCode, result.ContentType and result.Body to the HTTP response.
```

Runnable versions: [samples](docs/samples.md) (quickstart console app, EventSub chat bot, ASP.NET Core webhook host).

## Documentation

The [documentation index](docs/README.md) links everything. Most used:

- [Quickstart](docs/quickstart.md) and [authentication](docs/authentication.md)
- [EventSub overview](docs/eventsub.md) and [chat over IRC](docs/chat-irc.md)
- Helix group references, starting at [users, streams and channels](docs/helix-foundation.md)
- [Architecture](docs/architecture.md), [testing](docs/testing.md), [API coverage](docs/coverage.md), [releases](docs/releases.md)

## Build and test

```sh
dotnet build TwitchSdk.slnx -c Release
dotnet test tests/TwitchSdk.Tests/TwitchSdk.Tests.csproj -c Release --no-build
dotnet test tests/TwitchSdk.IntegrationTests/TwitchSdk.IntegrationTests.csproj -c Release   # skipped without the Twitch CLI
```

Details, including the package smoke test and native AOT, are in [testing](docs/testing.md). No test needs Twitch credentials. Never commit access tokens, refresh tokens, client secrets or webhook secrets.

## Contributing, security and license

See [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md) and the [changelog](CHANGELOG.md). Licensed under the [MIT license](LICENSE). Twitch is a trademark of Twitch Interactive, Inc.; this project is not affiliated with or endorsed by Twitch.
