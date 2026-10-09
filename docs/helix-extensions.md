# Extensions

All twelve extension endpoints are available through `helix.Extensions` (`ExtensionsClient`) and accept a final `CancellationToken`. Twitch authenticates them in two different ways, so an Extension Backend Service (EBS) normally uses **two** `HelixClient` instances:

| Method | Endpoint | Authentication |
| --- | --- | --- |
| `GetExtensionConfigurationSegmentsAsync` | `GET extensions/configurations` | EBS JWT |
| `SetExtensionConfigurationSegmentAsync` | `PUT extensions/configurations` | EBS JWT |
| `SetExtensionRequiredConfigurationAsync` | `PUT extensions/required_configuration` | EBS JWT (`user_id` = owner) |
| `SendExtensionPubSubMessageAsync` | `POST extensions/pubsub` | EBS JWT + `channel_id`, `pubsub_perms` |
| `GetExtensionSecretsAsync` / `CreateExtensionSecretAsync` | `GET`/`POST extensions/jwt/secrets` | EBS JWT |
| `SendExtensionChatMessageAsync` | `POST extensions/chat` | EBS JWT + `channel_id` |
| `GetExtensionsAsync` | `GET extensions` | EBS JWT |
| `GetExtensionLiveChannelsAsync` / `EnumerateExtensionLiveChannelsAsync` | `GET extensions/live` | App or user token |
| `GetReleasedExtensionsAsync` | `GET extensions/released` | App or user token |
| `GetExtensionBitsProductsAsync` / `UpdateExtensionBitsProductAsync` | `GET`/`PUT bits/extensions` | App token of the extension's client ID |

## The EBS JWT client

JWT endpoints expect `Authorization: Bearer <JWT>` plus `Client-Id: <extension client ID>`. `ExtensionJwtTokenProvider` signs a new token for every request, so the client needs no token cache or refresh logic:

```csharp
using TwitchDock.Core;
using TwitchDock.Helix;
using TwitchDock.Helix.Extensions;

var extensionClientId = Environment.GetEnvironmentVariable("TWITCH_EXTENSION_CLIENT_ID")
    ?? throw new InvalidOperationException("Set TWITCH_EXTENSION_CLIENT_ID.");
var ownerUserId = Environment.GetEnvironmentVariable("TWITCH_EXTENSION_OWNER_ID")
    ?? throw new InvalidOperationException("Set TWITCH_EXTENSION_OWNER_ID.");
var secret = ExtensionSecret.FromBase64(Environment.GetEnvironmentVariable("TWITCH_EXTENSION_SECRET")
    ?? throw new InvalidOperationException("Set TWITCH_EXTENSION_SECRET."));

// Keep redirects disabled so bearer tokens never follow a redirect.
using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
var jwtProvider = new ExtensionJwtTokenProvider(extensionClientId, ownerUserId, secret);
var ebs = new HelixClient(new TwitchHttpClient(http, jwtProvider,
    new TwitchHttpOptions { ClientId = extensionClientId }));

var extension = await ebs.Extensions.GetExtensionsAsync(extensionClientId, cancellationToken: cancellationToken);
```

Rules worth knowing:

- **Client ID.** `TwitchHttpOptions.ClientId` must equal the provider's `ExtensionClientId`. Tokens carry that client ID and the transport rejects a mismatch with `TwitchAuthorizationException` before sending anything.
- **Separate authorizations.** Keep the JWT client apart from your OAuth client (the one `AddTwitchDock` registers). Register a second `TwitchHttpClient`/`HelixClient` pair for the EBS, for example as a keyed service. Calling a JWT endpoint on the OAuth client fails locally: known app/user tokens are rejected (the exception text names the other OAuth token kind, but the fix is to use the JWT client). Without this check Twitch would answer 401 and trigger a needless OAuth refresh. Calling an OAuth endpoint on the JWT client is sent and rejected by Twitch with 401.
- **Claims.** Every token contains `exp`, `user_id` (the extension owner) and `role: "external"`, signed with HS256 using the base64-decoded secret. `SendExtensionChatMessageAsync` adds `channel_id` = broadcaster; `SendExtensionPubSubMessageAsync` adds `channel_id` (broadcaster or `all`) and `pubsub_perms.send` = the message targets. These request-specific claims are scoped to that call only, including concurrent calls.
- **Lifetime.** The default lifetime is three minutes; pass 1 second to 1 hour as `lifetime`. Supply a `TimeProvider` for tests. Tokens have `TwitchTokenKind.Unknown`, so the SDK performs no scope preflight for them and validates request inputs instead; Twitch verifies the signature, owner and roles.
- **401 handling.** After HTTP 401 the provider signs a new token. The transport retries once only if that token differs (for example after the clock moved past an expiry); otherwise the 401 surfaces as `TwitchApiException`.
- **Secrets stay private.** `ExtensionSecret`, the provider, `AccessToken` and `ExtensionSharedSecret` redact their secret in `ToString()`. `ExtensionSecret.FromBase64` rejects empty or non-base64 input without echoing it.

To sign tokens yourself, for example for a custom provider, use `ExtensionJwt.CreateExternal(secret, ownerUserId, channelId, lifetime, timeProvider)` or `ExtensionJwt.CreateForPubSub(secret, ownerUserId, channelId, sendTargets, ...)`. The results are bearer credentials: never log them.

## Shared secrets and rotation

```csharp
var current = await ebs.Extensions.GetExtensionSecretsAsync(extensionClientId, cancellationToken);
var created = await ebs.Extensions.CreateExtensionSecretAsync(extensionClientId, delaySeconds: 600, cancellationToken);
var next = created.Data.Single().Secrets.MaxBy(s => s.ActiveAt)!;
// Once next.ActiveAt has passed, switch signing without rebuilding the client.
jwtProvider.ReplaceSecret(ExtensionSecret.FromBase64(next.Content));
```

Responses contain `FormatVersion` and secrets with `Content`, `ActiveAt` and `ExpiresAt`. Creating a secret takes the current ones out of service after the activation delay: at least 300 seconds, which is also Twitch's default when `delaySeconds` is null. Creation is a mutation and is never retried after an ambiguous failure.

## Configuration segments and required configuration

```csharp
var segments = await ebs.Extensions.GetExtensionConfigurationSegmentsAsync(new()
{
    ExtensionId = extensionClientId, BroadcasterId = "123",
    Segments = [ExtensionConfigurationSegmentTypes.Broadcaster, ExtensionConfigurationSegmentTypes.Global]
}, cancellationToken);

await ebs.Extensions.SetExtensionConfigurationSegmentAsync(new()
{
    ExtensionId = extensionClientId, Segment = "broadcaster", BroadcasterId = "123",
    Content = "{\"theme\":\"dark\"}", Version = "1.0"
}, cancellationToken);

await ebs.Extensions.SetExtensionRequiredConfigurationAsync("123", new()
{
    ExtensionId = extensionClientId, ExtensionVersion = "0.0.1", RequiredConfiguration = "configured-v1"
}, cancellationToken);
```

Segments are case-sensitive: `broadcaster`, `developer`, `global`. Reads accept several segments (repeated `segment` parameters), and Twitch ignores duplicates and answers in request order. `BroadcasterId` is required when broadcaster or developer is requested and must be omitted when only global is requested. Responses include `BroadcasterId` only for broadcaster and developer segments. `Content` is plain text or string-encoded JSON, at most 5 KB (checked locally as 5120 UTF-8 bytes); a null `Version` updates the latest definition. Active extension instances do not receive an update automatically. Twitch allows 20 reads per segment and 20 updates per minute.

Required configuration needs the broadcaster ID (query) and extension ID, version and the string (body), all mandatory. It only applies when the extension uses Custom/My Own Service configuration.

## PubSub and chat messages

```csharp
await ebs.Extensions.SendExtensionPubSubMessageAsync(new()
{
    Target = [ExtensionPubSubTargets.Broadcast, ExtensionPubSubTargets.Whisper("456")],
    BroadcasterId = "123", Message = "{\"score\":42}"
}, cancellationToken);

await ebs.Extensions.SendExtensionPubSubMessageAsync(new()
{
    Target = [ExtensionPubSubTargets.Global], IsGlobalBroadcast = true, Message = "maintenance at 18:00"
}, cancellationToken);

await ebs.Extensions.SendExtensionChatMessageAsync("123", new()
{
    Text = "Round 3 starts now!", ExtensionId = extensionClientId, ExtensionVersion = "0.0.1"
}, cancellationToken);
```

PubSub targets are `broadcast`, `global` or `whisper-<user-id>`; broadcast and global are mutually exclusive. A global broadcast requires `IsGlobalBroadcast = true`, the single target `global` and no broadcaster ID; every other message needs `BroadcasterId`. Messages are limited to 5 KB (5120 UTF-8 bytes); Twitch answers 422 if it still considers a message too large and 403 if the JWT channel does not match. The limit is 100 messages per minute per extension client ID and broadcaster.

Chat messages appear under the extension's name, require Chat Capabilities and accept up to 280 characters. Twitch allows 12 messages per minute per channel and returns 401 if the JWT `channel_id` does not match `broadcaster_id`, which the provider sets for you.

## Extension details and live channels

```csharp
// OAuth app or user token (for example the quickstart client).
var released = await helix.Extensions.GetReleasedExtensionsAsync("extension-id", "0.0.9", cancellationToken);
await foreach (var channel in helix.Extensions.EnumerateExtensionLiveChannelsAsync(new()
{
    ExtensionId = "extension-id", First = 100
}, cancellationToken))
    Console.WriteLine($"{channel.BroadcasterName}: {channel.Title}");
```

`GetExtensionsAsync` (EBS JWT) returns any version you own: without a version it returns the latest released one, and the list is empty if nothing was released. `GetReleasedExtensionsAsync` (app or user token) returns only released extensions; 404 means unknown or unreleased. Both return `TwitchExtension` with author, Bits/chat flags, configuration location (`hosted`, `custom`, `none`), descriptions, URLs, `IconUrls` keyed by size, screenshots, `State`, `SubscriptionsSupportLevel`, version, allowlisted URLs and `Views`. A view (`Mobile`, `Panel`, `VideoOverlay`, `Component`, `Config`) is null when the extension does not define it.

Live channels need the extension ID, accept `First` (1–100) and `After`, and return broadcaster ID/name, game and title (title may be empty). Unlike other lists, Twitch documents `pagination` as a bare cursor string, so `ExtensionLiveChannelsResponse.Pagination` is a `string?`; a standard `{"cursor": ...}` object is also accepted. The list lags by a few minutes, and 404 means the extension is unknown or not used in a live stream.

## Bits products

```csharp
// extensionApp: a HelixClient whose provider returns app access tokens (client credentials)
// for the extension's own client ID, with TwitchHttpOptions.ClientId set to that ID.
var products = await extensionApp.Extensions.GetExtensionBitsProductsAsync(shouldIncludeAll: true, cancellationToken);
await extensionApp.Extensions.UpdateExtensionBitsProductAsync(new()
{
    Sku = "power-up.1", Cost = new() { Amount = 100 }, DisplayName = "Power up",
    InDevelopment = true, IsBroadcast = true, Expiration = DateTimeOffset.UtcNow.AddMonths(3)
}, cancellationToken);
```

User tokens are rejected locally; Twitch verifies that the token's client ID belongs to the extension (400 otherwise). Products come back in ascending SKU order; `shouldIncludeAll` adds disabled and expired products. The update adds a product or replaces every field except the SKU. SKUs have 1–255 ASCII letters, digits, `-`, `_` or `.`; `Cost.Type` is `bits` with amounts 1–10000; display names have at most 255 characters. Null optional fields are omitted so Twitch applies its defaults: not in development, not broadcast, no expiry. A past `Expiration` disables the product. A product without expiry returns `Expiration = null` even if Twitch sends an empty string. The response cost reuses `ExtensionProductCost`, the type also used by extension transactions.

## Errors and tests

Non-success responses become `TwitchApiException` with status, `Error`, message and `RequestId` (Twitch-Trace-Id). Typical cases are 400 for missing or invalid parameters, 401 for invalid JWTs/tokens or a client ID mismatch, 403 for PubSub channel mismatches, 404 for unknown or unreleased extensions, 422 for oversized PubSub messages and 429 for rate limits (bounded retries are handled by the transport). Mutations are not retried after ambiguous 5xx or network failures.

`ExtensionsTests` covers every documented response field, exact queries and bodies, JWT header/payload/signature (checked independently with `HMACSHA256` and against a token computed outside .NET), base64url without padding, expiry with a fixed `TimeProvider`, per-request channel claims (also concurrently), secret rotation and redaction, authorization preflight, validation limits, string-cursor pagination and error preservation. Fixtures are synthetic contract cases; live verification against Twitch is still required before a stable release.

Sources: pinned official reference for [Get Extension Configuration Segment](https://dev.twitch.tv/docs/api/reference/#get-extension-configuration-segment), [Set Extension Configuration Segment](https://dev.twitch.tv/docs/api/reference/#set-extension-configuration-segment), [Set Extension Required Configuration](https://dev.twitch.tv/docs/api/reference/#set-extension-required-configuration), [Send Extension PubSub Message](https://dev.twitch.tv/docs/api/reference/#send-extension-pubsub-message), [Get Extension Live Channels](https://dev.twitch.tv/docs/api/reference/#get-extension-live-channels), [Get Extension Secrets](https://dev.twitch.tv/docs/api/reference/#get-extension-secrets), [Create Extension Secret](https://dev.twitch.tv/docs/api/reference/#create-extension-secret), [Send Extension Chat Message](https://dev.twitch.tv/docs/api/reference/#send-extension-chat-message), [Get Extensions](https://dev.twitch.tv/docs/api/reference/#get-extensions), [Get Released Extensions](https://dev.twitch.tv/docs/api/reference/#get-released-extensions), [Get Extension Bits Products](https://dev.twitch.tv/docs/api/reference/#get-extension-bits-products), [Update Extension Bits Product](https://dev.twitch.tv/docs/api/reference/#update-extension-bits-product).
