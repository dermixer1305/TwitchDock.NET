# Authentication

`TwitchSdk.Authentication` implements Twitch's OAuth 2.0 and OpenID Connect flows against `https://id.twitch.tv/oauth2/`. Everything goes through `TwitchOAuthClient`, which wraps an `HttpClient` you own (or the typed client registered by `AddTwitchSdk`) and an optional `TimeProvider`. Every method accepts a final `CancellationToken`.

```csharp
using TwitchSdk.Authentication;
using TwitchSdk.Core;

// Disable redirects on custom handlers; AddTwitchSdk configures this for you.
using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
var oauth = new TwitchOAuthClient(http);
```

| Flow | Use it for | Token | Methods |
| --- | --- | --- | --- |
| [Client credentials](#client-credentials-app-token) | Server-to-server calls, webhooks, conduits, public data | App | `GetAppTokenAsync` |
| [Authorization code](#authorization-code) | Web apps with a backend that keeps the client secret | User + refresh | `CreateAuthorizationUri`, `TwitchOAuthCallbacks.ParseAuthorizationCode`, `ExchangeCodeAsync` |
| [Implicit grant](#implicit-grant) | Clients without a backend; no refresh token | User | `CreateAuthorizationUri(implicitGrant: true)`, `TwitchOAuthCallbacks.ParseImplicitGrant` |
| [Device code](#device-code) | CLIs, bots, devices without a convenient browser | User + refresh | `StartDeviceAuthorizationAsync`, `WaitForDeviceAuthorizationAsync` |
| [OpenID Connect](#openid-connect) | Sign-in and verified identity | ID token (+ user token) | `CreateOpenIdAuthorizationUri`, `ValidateIdTokenAsync`, `GetUserInfoAsync` |

Grant methods return `OAuthTokenResponse` with `AccessToken`, `RefreshToken`, `ExpiresIn`, `Scope`, `IdToken`, and the SDK-assigned `Kind` (app or user) and `ClientId`. Failures throw `TwitchApiException` (`StatusCode`, machine-readable `Error`, for example `invalid_refresh_token`); the raw OAuth response body is never kept because it can contain credentials.

## Client credentials (app token)

```csharp
var app = await oauth.GetAppTokenAsync(clientId, clientSecret, cancellationToken);
// app.Kind == TwitchTokenKind.App. App tokens have no refresh token: request a new one instead.
var appTokens = new RefreshingTokenProvider((_, ct) => oauth.GetAppTokenAsync(clientId, clientSecret, ct));
```

Without an initial token, `RefreshingTokenProvider` acquires one on first use and acquires a new one after Twitch rejects it with HTTP 401.

## Authorization code

1. Create a random state, store it in the initiating browser session and use it once.
2. Redirect to the authorization URI. Register the redirect URI with Twitch exactly as you pass it.
3. On the callback, parse and check the redirect, then exchange the code.

```csharp
var state = TwitchOAuthClient.CreateState(); // store in the user's session
var authorizeUri = TwitchOAuthClient.CreateAuthorizationUri(clientId, redirectUri,
    [TwitchScopes.UserReadChat, TwitchScopes.UserWriteChat], state, forceVerify: false);

// Later, in the callback handler (callbackUri is the full request URI, storedState the value saved in step 1):
AuthorizationCodeCallback callback;
try
{
    callback = TwitchOAuthCallbacks.ParseAuthorizationCode(callbackUri, storedState);
}
catch (TwitchOAuthCallbackException ex) when (ex.Error == "access_denied")
{
    return; // The user declined.
}
var userToken = await oauth.ExchangeCodeAsync(clientId, clientSecret, callback.Code, redirectUri, cancellationToken);
```

`ParseAuthorizationCode` compares the state in constant time before trusting any other value, rejects repeated parameters, and throws `TwitchOAuthCallbackException` (with `Error` and `ErrorDescription`) for denials, state mismatches and missing codes. Consume the stored state whatever the outcome. `callback.Scopes` lists the scopes the user granted. `forceVerify: true` shows the consent screen again even when the user already authorized the app.

## Implicit grant

```csharp
var implicitUri = TwitchOAuthClient.CreateAuthorizationUri(clientId, redirectUri, [TwitchScopes.UserReadChat], state, implicitGrant: true);

// The token arrives in the URI fragment, which browsers never send to a server. Capture the full redirect URI
// in the client itself, for example in an embedded browser or a page that hands the fragment to your app.
var grant = TwitchOAuthCallbacks.ParseImplicitGrant(redirectedUri, storedState);
var implicitToken = new AccessToken(grant.AccessToken, scopes: grant.Scopes, kind: TwitchTokenKind.User, clientId: clientId);
```

Denials arrive in the query string, also for this flow; `ParseImplicitGrant` checks both. The implicit grant issues no refresh token. Validate the token (`ValidateAsync`) to learn its user ID and expiry.

## Device code

```csharp
string[] scopes = [TwitchScopes.UserReadChat, TwitchScopes.UserWriteChat];
var device = await oauth.StartDeviceAuthorizationAsync(clientId, scopes, cancellationToken);
Console.WriteLine($"Open {device.VerificationUri} and enter the code {device.UserCode}.");
var deviceToken = await oauth.WaitForDeviceAuthorizationAsync(clientId, device, scopes, cancellationToken);
```

`WaitForDeviceAuthorizationAsync` polls at the advertised `Interval`, keeps waiting on `authorization_pending`, adds five seconds on `slow_down`, throws `TimeoutException` when the code expires and `TwitchApiException` for any other rejection (for example `Error == "invalid_device_code"`). Use `ExchangeDeviceCodeAsync` to poll yourself. The flow needs no client secret, so public clients can use it; refresh with `RefreshAsync(clientId, refreshToken)` and omit the secret.

## OpenID Connect

```csharp
var oidcState = TwitchOAuthClient.CreateState();
var nonce = TwitchOAuthClient.CreateState(); // store both in the session
var signInUri = TwitchOAuthClient.CreateOpenIdAuthorizationUri(clientId, redirectUri, [TwitchScopes.UserReadEmail], oidcState, nonce,
    claims: new OpenIdClaimsRequest { IdToken = ["email", "email_verified", "preferred_username"], UserInfo = ["picture"] });

// Callback (the default response type is the authorization code flow):
var code = TwitchOAuthCallbacks.ParseAuthorizationCode(callbackUri, storedState).Code;
var oidcToken = await oauth.ExchangeCodeAsync(clientId, clientSecret, code, redirectUri, cancellationToken);
var claims = await oauth.ValidateIdTokenAsync(oidcToken.IdToken!, clientId, storedNonce, oidcToken.AccessToken, cancellationToken);
Console.WriteLine($"Signed in as {claims.PreferredUsername} (user ID {claims.Subject})");

var userInfo = await oauth.GetUserInfoAsync(oidcToken.AccessToken, cancellationToken);
```

`CreateOpenIdAuthorizationUri` adds the `openid` scope when it is missing. `OpenIdResponseType.IdToken` and `TokenIdToken` select the implicit variants; `ParseImplicitGrant` then returns `IdToken`.

`ValidateIdTokenAsync(idToken, clientId, expectedNonce, accessToken?)` requires the nonce you stored for the authorization request and compares it in constant time. It verifies the RS256 signature against Twitch's published keys, the issuer (`TwitchOAuthClient.OpenIdIssuer`), the audience and a present `azp`, expiry and issue time with five minutes of clock skew, `at_hash` when you pass the access token from the same response, and a nonempty subject; tokens over 16 KiB are rejected before parsing. Only for flows that sent no nonce, use the explicitly named `ValidateIdTokenWithoutNonceAsync`. Any failure throws `TwitchIdTokenException`; never use claims from a token that failed validation. Optional claims (email, picture, preferred username, update time) are present only when requested and granted.

Signing keys are cached in an `OpenIdSigningKeyCache` shared by all `TwitchOAuthClient` instances (`OpenIdSigningKeyCache.Shared` unless you pass your own, which matters because DI creates a typed client per resolution): one fetch at a time, keys expire after an hour, an unknown key ID triggers at most one refresh per five minutes, and failed fetches are remembered for 30 seconds.

## Token providers and refresh

Helix, EventSub subscription management and IRC read tokens from an `IAccessTokenProvider`:

- `StaticAccessTokenProvider` returns one fixed `AccessToken`. It cannot refresh.
- `RefreshingTokenProvider` serializes acquisition and refresh for **one** authorization. Share one instance across the clients of that authorization.

```csharp
var userTokens = new RefreshingTokenProvider(
    (refreshToken, ct) => oauth.RefreshAsync(clientId, refreshToken ?? throw new InvalidOperationException("No refresh token."), clientSecret, ct),
    initialToken: userToken,
    persist: (rotated, ct) => SaveEncryptedAsync(rotated, ct)); // your encrypted store
```

When Helix answers HTTP 401, `TwitchHttpClient` calls `RefreshTokenAsync` once and retries with the new token. Concurrent rejections share one refresh, and a caller holding an already replaced token receives the current one without another refresh. A rotated refresh token replaces the old one in memory before `persist` runs, so a failing store never causes reuse of an invalidated refresh token; the persistence exception still propagates. Coordinate refreshes across processes yourself; the SDK has no distributed lock. Dispose the provider only after every client using it has stopped.

## Validation (startup and hourly)

Twitch requires applications to validate user tokens when they start and then hourly.

```csharp
var validation = await oauth.ValidateAsync(userToken.AccessToken, cancellationToken);
// ClientId, Login, UserId, Scopes and ExpiresIn. ToAccessToken attaches this metadata for local preflight checks.
var validated = validation.ToAccessToken(userToken.AccessToken);
```

`TokenValidationLoop.RunAsync(oauth, provider, expectedClientId, onValidated, timeProvider, cancellationToken)` validates immediately and then every hour, also while idle. It updates `RefreshingTokenProvider` metadata (scopes, user ID, token kind) without overwriting a concurrently rotated token, tries one refresh when Twitch answers 401, and ends with an exception when the token stays invalid or belongs to another client ID. Observe the returned task and end the associated sessions when it fails.

In a generic host, register the hosted service instead:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddTwitchSdk(new TwitchHttpOptions { ClientId = clientId }, _ => userTokens);
builder.Services.AddTwitchTokenValidation(new TwitchTokenValidationOptions
{
    ExpectedClientId = clientId,
    OnValidated = (result, ct) => { Console.WriteLine($"Token valid for {result.ExpiresIn} s"); return Task.CompletedTask; },
});
```

`TwitchTokenValidationService` runs the loop in the background without blocking startup. Network failures, HTTP 429 and 5xx responses are retried up to `MaxConsecutiveTransientFailures` (default 5) times with `TransientRetryDelay` (default 30 s) between attempts. Any other failure, such as a token that cannot be refreshed, faults the service; with the default host settings the host then stops instead of serving with a dead credential.

## Revocation

```csharp
await oauth.RevokeAsync(clientId, userToken.AccessToken, cancellationToken);
```

Revoke tokens on sign-out or when an account is unlinked, and delete the stored refresh token from your store as well. Failures throw `TwitchApiException`.

## Scope preflight and TwitchAuthorizationException

Every reviewed Helix method and EventSub subscription declares what Twitch checks: required scopes, alternative scopes ("A or B"), the accepted token kind and the user who must own the token (for example `broadcaster_id` or `moderator_id`). Before sending, `TwitchHttpClient` compares this with the token's known metadata:

- The token's client ID must equal `TwitchHttpOptions.ClientId`.
- App tokens are rejected where only user tokens work, and vice versa, when the token kind is known.
- The user ID must match when both sides are known.
- Scopes are checked only when known (`AccessToken.ScopesKnown`), which is the case for tokens from grant responses, `ToAccessToken` and the validation loop.

A failed check throws `TwitchAuthorizationException` (an `InvalidOperationException`) with `MissingScopes` and `RequiredAnyOfScopes`, and nothing is sent. Unknown metadata, app grants (`user:bot`, `channel:bot`), moderator roles and resource ownership remain Twitch's decision and surface as `TwitchApiException` (usually 401 or 403). `TwitchScopes` contains a constant for every scope in the pinned official scope table.

```csharp
try
{
    await helix.SendChatMessageAsync(new() { BroadcasterId = broadcasterId, SenderId = botUserId, Message = "Hello" }, cancellationToken);
}
catch (TwitchAuthorizationException ex)
{
    Console.WriteLine($"Not sent. Missing: {string.Join(", ", ex.MissingScopes)}; any of: {string.Join(", ", ex.RequiredAnyOfScopes)}");
}
catch (TwitchApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
{
    Console.WriteLine($"Twitch refused the request: {ex.Message} (trace {ex.RequestId})");
}
```

Extension endpoints that require an Extension JWT use `ExtensionJwtTokenProvider` instead of OAuth tokens; see [Extensions](helix-extensions.md).

## Handling credentials

`ToString()` redacts credentials in `AccessToken`, `OAuthTokenResponse`, `DeviceAuthorization`, `AuthorizationCodeCallback`, `ImplicitGrantCallback`, `EventSubTransportRequest`, `ConduitShardTransportRequest`, `StreamKeyResult`, `ExtensionSecret`, `ExtensionJwtTokenProvider` and the IRC `PASS` line. Explicitly serializing a token response is still sensitive. Keep tokens and secrets in encrypted storage or a secret manager, never in source, logs, URLs or fixtures. See [SECURITY.md](../SECURITY.md).
