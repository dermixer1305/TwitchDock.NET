# Architecture

TwitchSdk is an independent implementation. No TwitchLib source or architecture is reused.

| Module | Owns |
| --- | --- |
| Core | Immutable token snapshots, HTTP headers, bounded retries, rate-limit coordination, error mapping, cursor iteration |
| Authentication | Twitch OAuth requests, state helpers, serialized token refresh/rotation, startup/hourly validation loop |
| Helix | Endpoint methods, request validation, wire models and source-generated JSON metadata |
| EventSub | WebSocket lifecycle, webhook signature verification, envelopes, duplicate suppression |
| Chat | Typed chat messages and Helix/EventSub convenience methods |
| DependencyInjection | IHttpClientFactory integration and service lifetimes |

Dependencies flow from DI to the feature modules and then Core. EventSub depends on Helix for shared subscription models; Chat depends on EventSub and Helix. Core has no dependency on authentication grant implementations. The only production package families beyond the runtime are Microsoft.Extensions logging and HTTP/DI.

## Conventions

- Public async methods end in Async and accept CancellationToken last. Library awaits use ConfigureAwait(false).
- IDs remain strings. Evolving wire discriminators remain strings to tolerate newly added values.
- JSON uses explicit source-generated metadata and snake_case property names. Omitted optional fields remain distinguishable from false/zero. Reflection-based serialization is not required by the clients.
- Request models are separate from response models. Public collections use read-only interfaces; token collections and pagination filters are copied where lifetime matters.
- HttpClient is supplied by the host or factory. Default request headers are never mutated. Custom clients must disable redirects; DI configures this automatically. Tokens never belong in URLs.
- A TwitchHttpClient and RefreshingTokenProvider are shared for **one authorization**. Register independent service providers/clients for independent authorizations. Concurrent calls share rate-limit observations and one refresh gate.
- Only GET/HEAD requests retry server errors. HTTP 429 explicitly rejects the operation and may be retried with the same body. Mutations never retry ambiguous 5xx or network failures. All waits are cancellable and retry counts/delays are bounded.
- Scope candidates in the inventory are not an AND rule: user grants, app grants, moderation roles, resource ownership, and transport can change authorization requirements. Reviewed methods use TwitchAuthorizationRequirement for preflight checks when token metadata is known. App grants and ownership remain server-authoritative; unknown metadata is deferred to Twitch. Token validation updates provider metadata without overwriting a concurrently rotated token.

## EventSub semantics

The welcome callback creates subscriptions on a fresh connection. During a server-requested migration, the old socket continues receiving until the replacement welcome arrives; the callback then receives `resubscribe = false`. A lost connection creates a fresh session with `resubscribe = true`. Twitch does not replay events missed during a disconnection. Notification callbacks are sequential, so applications should hand work to a bounded queue promptly.

Webhook verification consumes the exact raw body bytes, signs message ID + timestamp + body using HMAC-SHA256, and compares in constant time. Nanosecond timestamp text is retained for signatures. Timestamp freshness and body size are checked before processing. Verification is framework independent; HTTP hosting, durable processing, and acknowledgment policy belong to the host until a dedicated adapter is implemented.

MessageDeduplicator is bounded and in-memory. It fails when full rather than evicting replay protection. It is not a durable inbox or exactly-once guarantee. Distributed webhook receivers must use shared persistent storage and acknowledge only after durable acceptance. Failed application processing must be handled explicitly.

## Native AOT

Source-generated serializers are provided. AOT compilation and platform smoke tests are still pending; compatibility is a design constraint, not yet a verified release claim.
