# Security

## Reporting a vulnerability

Do not open a public issue for security problems. Use [GitHub's private vulnerability reporting](https://github.com/dermixer1305/TwitchDock.NET/security/advisories/new). Include a minimal reproduction, affected version and impact without credentials.

Never include access tokens, refresh tokens, client secrets, webhook secrets, extension secrets or stream keys in a report. Revoke any credential that was exposed.

## Supported versions

Only the latest release receives fixes. The current version is the 1.0.0-rc.2 release candidate. Selected authentication, Helix and chat flows have been verified against Twitch; broader live verification remains outstanding ([report](docs/live-verification.md)). No independent security audit has been performed.

## What the SDK does for you

- **Credential redaction.** `ToString()` of tokens, OAuth responses, device authorizations, OAuth callbacks, transport requests with webhook secrets, extension secrets, stream keys and the IRC `PASS` line returns redacted text. OAuth error bodies are reduced to known machine-readable codes because raw responses can contain credentials. Tokens are sent only in headers, never in URLs.
- **Origin protection.** Helix calls accept only relative endpoint paths, so a bearer token cannot be sent to another host through a crafted path. The DI registrations disable automatic redirects. EventSub `session_reconnect` URLs are followed only to the configured scheme, host and port.
- **Loopback-only plain endpoints.** `TwitchHttpOptions.BaseAddress` must use HTTPS and the EventSub WebSocket endpoint `wss://`; `http://` and `ws://` are accepted only on loopback hosts, for local tests against the Twitch CLI.
- **Webhook verification.** `EventSubWebhookVerifier` checks the HMAC-SHA256 signature over message ID, timestamp and the exact raw body in constant time, rejects timestamps older than ten minutes or more than one minute ahead and bodies over 1 MiB, and parses JSON only after verification. `EventSubWebhookHandler` answers 403 for invalid signatures.
- **OAuth callbacks.** `TwitchOAuthCallbacks` compares the state in constant time before trusting any other value and rejects repeated parameters.
- **OpenID Connect.** `ValidateIdTokenAsync` requires the nonce, verifies the RS256 signature against Twitch's published keys (shared cache with expiry and refresh limits) and checks issuer, audience, authorized party, expiry, issue time, `at_hash` when an access token is supplied, and a 16 KiB size limit. Claims of a token that fails validation are never returned.
- **Token destination.** `TwitchHttpClient` accepts only allow-listed endpoint paths and checks that the final URI keeps the configured scheme, host and base path before a token is attached. Plain `http://`/`ws://` endpoints are accepted only for loopback addresses (local mock servers).
- **Resource limits.** Helix responses are capped (`MaxResponseContentBytes`, 32 MiB by default), OAuth responses at 1 MiB, EventSub WebSocket messages and webhook bodies at 1 MiB, IRC lines at 64 KiB, and ID tokens at 16 KiB.
- **Webhook replay and header confusion.** Deduplication covers the freshness window, and the unsigned message-type header must agree with the signed body.
- **Authorization preflight.** Known token kind, client ID, user and scopes are checked before a request is sent, which avoids sending tokens to operations they cannot authorize.

## What you must do

- Store tokens and secrets encrypted or in a secret manager; read them from the environment or configuration providers, never from source.
- Protect OAuth `state` (and the OpenID `nonce`) with a single-use binding to the user's browser session. A random value alone is not enough.
- Use one `RefreshingTokenProvider` and client set per authorization, and verify the expected client ID during validation. Coordinate refreshes across processes yourself.
- Validate user tokens at startup and hourly (`AddTwitchTokenValidation` or `TokenValidationLoop`), and end the associated sessions when validation fails.
- Disable automatic redirects on custom `HttpClient` handlers.
- For webhooks, verify on the raw bytes before any side effect, limit the request body size, and use a shared durable inbox for deduplication across replicas or restarts. The built-in `MessageDeduplicator` is in memory and is not exactly-once delivery.
- Do not log request bodies of subscription or conduit calls: webhook secrets are necessarily part of the JSON sent to Twitch.
- Revoke tokens on sign-out and delete stored refresh tokens.
