# Architecture

TwitchSdk is an independent implementation. No TwitchLib source or architecture is reused.

## Modules

| Module | References | Owns |
| --- | --- | --- |
| Core | `Microsoft.Extensions.Logging.Abstractions` | `AccessToken` and token providers, `TwitchHttpClient` (headers, bounded retries, shared rate-limit coordination, one refresh on 401, error mapping), `HelixPage`/`HelixPagination`, `TwitchAuthorizationRequirement`, generated `TwitchScopes`, `EmptyStringAsNullDateTimeOffsetConverter` |
| Authentication | Core | OAuth grants, callback parsing, OpenID Connect (JWKS, ID token validation, UserInfo), `RefreshingTokenProvider`, `TokenValidationLoop` |
| Helix | Core | One client per API group behind `HelixClient`, request validation, wire models, `HelixJsonContext`, Extension JWTs (`ExtensionSecret`, `ExtensionJwt`, `ExtensionJwtTokenProvider`) |
| EventSub | Helix | Typed subscription specs (`EventSubSubscriptions`), event definitions and registry (`EventSubEvents`, `EventSubEventsJsonContext`), `EventSubEventRouter`, `EventSubWebSocketClient`, `EventSubWebhookVerifier`, `EventSubWebhookHandler`, `MessageDeduplicator`, the typed `CreateEventSubSubscriptionAsync`/`SubscribeWebSocketAsync` extensions |
| Chat | EventSub | `TwitchChatClient` (EventSub + Helix) and the IRC transport in `TwitchSdk.Chat.Irc` |
| DependencyInjection | Authentication, Chat, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Hosting.Abstractions` | `AddTwitchSdk`, `AddTwitchTokenValidation` (`TwitchTokenValidationService`), `AddTwitchIrc` |

Dependencies flow from DI to Chat, EventSub, Helix and Core; Authentication depends only on Core. Core knows nothing about grant implementations. There are no third-party dependencies.

## Conventions

- Public async methods end in `Async` and take `CancellationToken` last. Library awaits use `ConfigureAwait(false)`. No method blocks on async work.
- IDs are strings. Evolving discriminators (statuses, types, actions) are strings so values added by Twitch are preserved.
- Request models are separate from response models. EventSub transports have separate request (`EventSubTransportRequest`) and response (`EventSubTransport`) types so secrets never round-trip.
- Public collections are read-only interfaces. Token collections and pagination filters are copied where their lifetime matters.
- `HttpClient` comes from the host or `IHttpClientFactory`; default request headers are never mutated. Custom handlers must disable redirects; DI does this. Tokens never go into URLs.
- A `TwitchHttpClient` and its token provider belong to **one authorization**. Concurrent calls share rate-limit observations and one refresh gate. Independent authorizations need independent clients.
- Only GET and HEAD retry server errors. HTTP 429 retries wait for the shared reset. Mutations never retry ambiguous 5xx or network failures. All waits are cancellable and all retry counts and delays are bounded.
- Every reviewed endpoint declares a `TwitchAuthorizationRequirement` (scopes, alternative scopes, token kind, owning user). Known token metadata is checked before sending; app grants, roles and ownership stay server-authoritative. Token validation updates provider metadata without overwriting a concurrently rotated token.
- Types that hold tokens, secrets, authorization codes or stream keys redact them in `ToString()`; the IRC `PASS` line is redacted too.
- No background work starts implicitly. Long-running loops (`EventSubWebSocketClient.RunAsync`, `TwitchIrcClient.RunAsync`, `TokenValidationLoop.RunAsync`) run only when the host calls them; the hosted validation service exists only when registered.

## JSON

Each module has explicit source-generated `JsonSerializerContext` metadata with snake_case names; the clients never need reflection-based serialization. Optional numbers, booleans and timestamps are nullable so an omitted field stays distinguishable from false or zero.

The source generator assigns every init-only property while constructing an object and passes null for fields missing from the payload, which would overwrite initializers such as `= []`. Properties with defaults therefore coalesce null through the C# 14 `field` keyword:

```csharp
public IReadOnlyList<string> Scopes { get; init => field = value ?? []; } = [];
```

This requires `LangVersion` 14 and the .NET 10 SDK (the compiled assemblies still run on .NET 8). A reflection test guards every public non-nullable init property. The same pattern makes null request lists mean "no filter".

Models also tolerate documented payload variants that real producers send: charity events accept `broadcaster_user_*` as well as the documented `broadcaster_*` fields, the `stream.offline` and `channel.unban_request.create` IDs are nullable because some payloads omit them, and `EmptyStringAsNullDateTimeOffsetConverter` maps empty-string timestamps (for example schedule vacations and unresolved unban requests) to null.

## EventSub

Three layers build on each other:

1. **Typed specs.** `EventSubSubscriptions` factories produce an `EventSubSubscriptionSpec`: type, version, condition and the authorization Twitch checks (required and alternative scopes, authorizing user, transports, batching). `CreateEventSubSubscriptionAsync` turns the spec into a preflight requirement for WebSocket user tokens and requires app tokens for webhooks and conduits.
2. **Typed events.** `EventSubEvents` binds each type and version to an event class and its generated metadata. A frozen registry (`All`, `TryGetDefinition`) supports dynamic lookups. Batched payloads (`events` arrays) are read transparently.
3. **Delivery.** `EventSubEventRouter` dispatches notifications and revocations to typed handlers for both transports. `EventSubWebSocketClient` owns the connection: the welcome callback creates subscriptions on a fresh session; during a server-requested migration the old socket keeps receiving until the replacement welcome arrives, and the callback then gets `resubscribe = false`; a lost or silent connection becomes a fresh session after bounded backoff with `resubscribe = true`. Reconnect URLs are followed only on the configured origin. Callbacks are sequential.

`EventSubWebhookVerifier` authenticates the exact raw bytes: HMAC-SHA256 over message ID, timestamp and body, compared in constant time, with timestamp freshness and a 1 MiB body limit checked before parsing. Nanosecond timestamp text is kept verbatim for the signature. `EventSubWebhookHandler` adds the challenge response, deduplication and routing, and returns a framework-independent status/content type/body, so ASP.NET Core or any other host maps it in a few lines. `MessageDeduplicator` is bounded and in memory; when full it fails instead of evicting replay protection. It is not a durable inbox or an exactly-once guarantee.

## Chat

`TwitchChatClient` combines the `channel.chat.message` subscription and Helix Send Chat Message. The IRC transport is separate: `IrcMessage` parses and serializes IRCv3 lines, typed views (`IrcChatMessage`, `IrcUserNotice`, ...) read Twitch's tags, `IrcMessageRouter` dispatches them, and `TwitchIrcClient` owns login, joins, keepalive, reconnects and the documented rate limits over a pluggable `IIrcConnection` (WebSocket or TCP). Details: [chat over IRC](chat-irc.md).

## Authentication

OAuth requests go through `TwitchOAuthClient`, which tags responses with token kind and client ID. Error bodies are reduced to known machine-readable codes because raw OAuth responses can contain credentials. Callback parsing checks the state in constant time before any other value and rejects repeated parameters. ID tokens are validated against Twitch's JWKS (RS256) with issuer, audience, lifetime and nonce checks; signing keys are cached and refetched at most every five minutes for unknown key IDs. `RefreshingTokenProvider` serializes refresh per authorization and keeps rotated credentials in memory even when persistence fails. `TwitchTokenValidationService` runs `TokenValidationLoop` as a hosted service with bounded retries for transient failures; an invalid token faults the service, which stops the host under the default host settings.

## Loopback endpoints

`TwitchHttpOptions.BaseAddress` must be HTTPS, and the EventSub WebSocket endpoint `wss://`. Plain `http://` and `ws://` are accepted only for loopback hosts, so tests and local development can target the Twitch CLI mock servers without opening a path for tokens to travel unencrypted over a network.

## Native AOT and trimming

All library projects set `IsAotCompatible`, which enables the trimming, single-file and AOT analyzers; with warnings as errors, any reflection-dependent code fails the build. The package smoke test runs the packed SDK with JSON reflection disabled and as a native AOT executable on net8.0 and net10.0 in CI ([testing](testing.md#package-smoke-test-and-native-aot)).

## Public API compatibility

`tests/TwitchSdk.Tests/PublicApi/*.txt` lists every public type and member per assembly. `PublicApiTests` fails on any difference, so every API change is an explicit, reviewed snapshot update; removals and signature changes are breaking changes under the [versioning policy](releases.md#versioning).

## API inventory

`docs/api/coverage.json` is the authoritative scope: every Helix endpoint and EventSub type/version from the pinned official documentation with source URL, hashes, status, availability and evidence. `tools/Update-ApiInventory.ps1` refreshes it, `tools/Update-ScopeConstants.ps1` regenerates `TwitchScopes`, `tools/Test-ApiCoverage.ps1` validates it and `tools/New-CoverageReport.ps1` renders [coverage.md](coverage.md).
