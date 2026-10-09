# Security

Never include access tokens, refresh tokens, webhook secrets or OAuth client secrets in issue reports. Use encrypted storage and a secret manager for deployments. Token ToString methods redact credentials, but explicit serialization of token response models is sensitive and must not be logged.

Disable automatic redirects on custom HttpClient handlers. The DI setup does this. Use separate clients/providers per authorization and verify the client ID when validating tokens. Protect OAuth state with a single-use browser-session binding; generating a random value alone is insufficient.

Verify webhook signatures on raw bytes before any side effects. Use persistent deduplication for distributed or restart-safe processing. Do not treat an in-memory cache as exactly-once delivery. Observe callback/validation-loop failures and terminate invalid OAuth sessions.

The SDK is pre-release. No live Twitch integration, independent security audit, or production-readiness claim has been completed. A private vulnerability reporting channel must be configured with the future GitHub repository before public release.
