# EventSub: charity, goals, Hype Train, users, system and Guest Star

Typed subscriptions and events for the community and system subscription types. Each type has a factory on `EventSubSubscriptions` that builds the documented condition and authorization metadata, and a definition with the same name on `EventSubEvents` that binds the type and version to its event model in `TwitchSdk.EventSub.Events`. All definitions are registered in `EventSubEvents.All` and resolve through `EventSubEvents.TryGetDefinition`.

| Type@version | Factory / definition | Event | Authorization | Transports |
| --- | --- | --- | --- | --- |
| `channel.charity_campaign.donate@1` | `ChannelCharityCampaignDonateV1(broadcasterUserId)` | `ChannelCharityCampaignDonateEvent` | broadcaster, `channel:read:charity` | all |
| `channel.charity_campaign.start@1` | `ChannelCharityCampaignStartV1(broadcasterUserId)` | `ChannelCharityCampaignStartEvent` | broadcaster, `channel:read:charity` | all |
| `channel.charity_campaign.progress@1` | `ChannelCharityCampaignProgressV1(broadcasterUserId)` | `ChannelCharityCampaignProgressEvent` | broadcaster, `channel:read:charity` | all |
| `channel.charity_campaign.stop@1` | `ChannelCharityCampaignStopV1(broadcasterUserId)` | `ChannelCharityCampaignStopEvent` | broadcaster, `channel:read:charity` | all |
| `channel.goal.begin@1` | `ChannelGoalBeginV1(broadcasterUserId)` | `ChannelGoalBeginEvent` | broadcaster, `channel:read:goals` | all |
| `channel.goal.progress@1` | `ChannelGoalProgressV1(broadcasterUserId)` | `ChannelGoalProgressEvent` | broadcaster, `channel:read:goals` | all |
| `channel.goal.end@1` | `ChannelGoalEndV1(broadcasterUserId)` | `ChannelGoalEndEvent` | broadcaster, `channel:read:goals` | all |
| `channel.hype_train.begin@2` | `ChannelHypeTrainBeginV2(broadcasterUserId)` | `ChannelHypeTrainBeginEvent` | broadcaster, `channel:read:hype_train` | all |
| `channel.hype_train.progress@2` | `ChannelHypeTrainProgressV2(broadcasterUserId)` | `ChannelHypeTrainProgressEvent` | broadcaster, `channel:read:hype_train` | all |
| `channel.hype_train.end@2` | `ChannelHypeTrainEndV2(broadcasterUserId)` | `ChannelHypeTrainEndEvent` | broadcaster, `channel:read:hype_train` | all |
| `user.authorization.grant@1` | `UserAuthorizationGrantV1(clientId)` | `UserAuthorizationGrantEvent` | app token, client ID = `client_id` | webhook, conduit |
| `user.authorization.revoke@1` | `UserAuthorizationRevokeV1(clientId)` | `UserAuthorizationRevokeEvent` | app token, client ID = `client_id` | webhook, conduit |
| `user.update@1` | `UserUpdateV1(userId)` | `UserUpdateEvent` | none (`user:read:email` adds the email) | all |
| `user.whisper.message@1` | `UserWhisperMessageV1(userId)` | `UserWhisperMessageEvent` | receiving user, `user:read:whispers` or `user:manage:whispers` | all |
| `conduit.shard.disabled@1` | `ConduitShardDisabledV1(clientId, conduitId?)` | `ConduitShardDisabledEvent` | app token, client ID = `client_id`, owner of `conduit_id` | webhook, conduit |
| `drop.entitlement.grant@1` | `DropEntitlementGrantV1(organizationId, categoryId?, campaignId?)` | `IReadOnlyList<DropEntitlementGrantEvent>` (batched) | app token owned by a member of the organization | webhook, conduit |
| `extension.bits_transaction.create@1` | `ExtensionBitsTransactionCreateV1(extensionClientId)` | `ExtensionBitsTransactionCreateEvent` | app token, client ID = extension client ID | webhook, conduit |
| `channel.guest_star_session.begin@beta` | `ChannelGuestStarSessionBeginBeta(broadcasterUserId, moderatorUserId)` | `ChannelGuestStarSessionBeginEvent` | moderator/broadcaster, any Guest Star scope | all |
| `channel.guest_star_session.end@beta` | `ChannelGuestStarSessionEndBeta(broadcasterUserId, moderatorUserId)` | `ChannelGuestStarSessionEndEvent` | moderator/broadcaster, any Guest Star scope | all |
| `channel.guest_star_guest.update@beta` | `ChannelGuestStarGuestUpdateBeta(broadcasterUserId, moderatorUserId)` | `ChannelGuestStarGuestUpdateEvent` | moderator/broadcaster, any Guest Star scope | all |
| `channel.guest_star_settings.update@beta` | `ChannelGuestStarSettingsUpdateBeta(broadcasterUserId, moderatorUserId)` | `ChannelGuestStarSettingsUpdateEvent` | moderator/broadcaster, any Guest Star scope | all |
| `stream.online@1` | `StreamOnlineV1(broadcasterUserId)` | `StreamOnlineEvent` | none | all |
| `stream.offline@1` | `StreamOfflineV1(broadcasterUserId)` | `StreamOfflineEvent` | none | all |

"Any Guest Star scope" means one of `channel:read:guest_star`, `channel:manage:guest_star`, `moderator:read:guest_star` or `moderator:manage:guest_star` (`AnyOfScopes`). For user-authorized types, `AuthorizingUserId` names the condition user whose token Twitch checks: the broadcaster, the whisper recipient or the Guest Star `moderator_user_id`. WebSocket subscriptions are preflighted against that user token's ID and known scopes. Webhook and conduit subscriptions use an app token, and Twitch checks the user's grant to your client server side.

## Notes

- **Webhook/conduit-only types.** `user.authorization.grant`, `user.authorization.revoke`, `drop.entitlement.grant` and `extension.bits_transaction.create` are documented as webhook and conduit only. `conduit.shard.disabled` requires an app access token, which rules out WebSockets. Their specs set `Transports = Webhook | Conduit` and no authorizing user: `SubscribeWebSocketAsync` throws `ArgumentException`, and a user token on webhook or conduit transports throws `TwitchAuthorizationException`, both before any HTTP request. Twitch validates that the app token's client ID matches `client_id` / `extension_client_id` and that the client may use the organization or conduit.
- **Batched Drops.** `drop.entitlement.grant` notifications are batched: one notification carries a JSON array of entitlement events, so the definition's event type is `IReadOnlyList<DropEntitlementGrantEvent>`. Each item has an EventSub `Id` and a `Data` object; de-duplicate by `Id` (event) and `Data.EntitlementId` (entitlement). Twitch expects roughly 0 to 5 requests per second with bodies up to 250 KB. Twitch delivers the batch in a payload property named `events` and requires `"is_batching_enabled": true` in the create request; see Batching below.
- **Guest Star is in public beta.** The version string is `beta`, so factories and definitions end in `Beta`. Twitch marks beta types as unstable, subject to change at any time and not meant for production; beta subscriptions are deleted 30 days after a generally available version ships. Twitch's examples and field tables disagree: the session events' examples include `moderator_user_*` while the tables list `host_user_*` for session end and guest update. All of these are modeled as nullable strings. Guest, slot, state, moderator and host media fields of `channel.guest_star_guest.update` are null when the documented condition applies (empty slot, guest-initiated update, not slotted).
- **Hype Train v2.** Models follow v2 exactly: `type` (`treasure`, `golden_kappa`, `regular`), `is_shared_train`, nullable `shared_train_participants`, and `all_time_high_level`/`all_time_high_total` on begin only. The v2 field tables have no `last_contribution`. Point counters use `long`.
- **Charity amounts** use `CharityCampaignAmount` with a `long` minor-unit `Value`, `DecimalPlaces` and ISO-4217 `Currency`; compute the major amount as `Value / 10^DecimalPlaces` with `decimal` arithmetic. Start and progress events can arrive out of order.
- **Goals.** `IsAchieved` and `EndedAt` exist only on `ChannelGoalEndEvent`. Begin and progress events can arrive out of order.
- **Users.** `UserUpdateEvent.Email` is nullable and empty unless the user granted `user:read:email` to your client; ignore `EmailVerified` when it is empty. `UserAuthorizationRevokeEvent.UserLogin`/`UserName` are null when the user no longer exists. Use the revoke event to meet data-deletion requirements (GDPR, LGPD, CCPA).
- **Conduit shard disabled.** `Transport.Callback` is set for webhook shards; `SessionId`, `ConnectedAt` and `DisconnectedAt` for WebSocket shards. Empty timestamp strings read as null.
- Enumerated strings (goal types, train types, contribution types, guest states, group layouts, shard statuses) stay `string` so new Twitch values do not break deserialization. Timestamps are `DateTimeOffset`; digits beyond 100 ns are truncated.

## Batching

`DropEntitlementGrantV1` sets `EventSubSubscriptionSpec.IsBatchingEnabled`, so the create request carries the required `"is_batching_enabled": true`. Batched notifications deliver the array in the payload's `events` property; `EventSubPayload.Events`, `TryReadEvent` and `EventSubEventRouter` read it transparently.

## Example

```csharp
using TwitchSdk.EventSub;
using TwitchSdk.Helix.Models;

var router = new EventSubEventRouter()
    .On(EventSubEvents.ChannelHypeTrainBeginV2, (train, subscription, ct) =>
    {
        Console.WriteLine($"{train.Type} Hype Train level {train.Level} on {train.BroadcasterUserLogin}, shared: {train.IsSharedTrain}");
        return Task.CompletedTask;
    })
    .On(EventSubEvents.ChannelCharityCampaignDonateV1, (donation, _, _) =>
    {
        var amount = donation.Amount.Value / (decimal)Math.Pow(10, donation.Amount.DecimalPlaces);
        Console.WriteLine($"{donation.UserName} donated {amount} {donation.Amount.Currency} to {donation.CharityName}");
        return Task.CompletedTask;
    })
    .On(EventSubEvents.DropEntitlementGrantV1, async (batch, _, ct) =>
    {
        foreach (var entitlement in batch) await GrantInGameItemAsync(entitlement.Data.UserId, entitlement.Data.EntitlementId, ct);
    })
    .On(EventSubEvents.UserAuthorizationRevokeV1, (revoke, _, ct) => DeleteUserDataAsync(revoke.UserId, ct))
    .OnRevocation((subscription, _) =>
    {
        Console.WriteLine($"{subscription.Type} revoked: {subscription.Status}");
        return Task.CompletedTask;
    });

// Webhook and conduit subscriptions require a HelixClient configured with an app access token.
var webhook = new EventSubTransportRequest { Method = "webhook", Callback = "https://example.com/eventsub", Secret = webhookSecret };
await appHelix.CreateEventSubSubscriptionAsync(EventSubSubscriptions.UserAuthorizationRevokeV1(clientId), webhook, ct);
await appHelix.CreateEventSubSubscriptionAsync(EventSubSubscriptions.ChannelHypeTrainBeginV2(broadcasterId), webhook, ct);

// WebSocket subscriptions use the authorizing user's token (here the broadcaster with channel:read:charity).
await broadcasterHelix.SubscribeWebSocketAsync(EventSubSubscriptions.ChannelCharityCampaignDonateV1(broadcasterId), sessionId, ct);

// In the webhook endpoint: verify the signature, answer challenges, de-duplicate and dispatch.
var handler = new EventSubWebhookHandler(new EventSubWebhookVerifier(webhookSecret), router);
var response = await handler.HandleAsync(EventSubWebhookRequest.FromHeaders(name => request.Headers[name], body), ct);
```

## Tests

`tests/TwitchSdk.Tests/EventSubCommunitySystemTests.cs` round-trips every event through `Fixtures/eventsub-community-system.json` (keyed `type@version`, covering every documented field, nulls for nullable fields and a two-item drop batch), checks exact conditions, scopes, authorizing users and transports of every factory, verifies WebSocket preflight and the app-token-only rejections over HTTP, and dispatches a batched drop entitlement notification through `EventSubEventRouter`. `stream.online@1` and `stream.offline@1` were reviewed against the same reference and are covered by `EventSubTypedTests`.

Sources: local snapshots of Twitch's [EventSub subscription types](https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/) and [EventSub reference](https://dev.twitch.tv/docs/eventsub/eventsub-reference/).
