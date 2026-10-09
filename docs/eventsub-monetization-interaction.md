# EventSub monetization and interaction

Typed subscriptions and events for Bits, subscriptions, Channel Points, custom Power-ups, polls and predictions. Each `EventSubSubscriptions` factory builds the documented condition and records the authorization Twitch checks. Each `EventSubEvents` definition with the same name binds the type and version to its event class in `TwitchDock.EventSub.Events`.

Every type here takes `broadcaster_user_id` and is authorized by that broadcaster: for WebSocket subscriptions the user token must belong to the broadcaster and carry the listed scope. For webhook and conduit subscriptions an app token is used, and Twitch checks that the broadcaster granted the scope to your client. All types accept WebSocket, webhook and conduit transports.

| Type@version | Factory / definition | Event class | Authorization | Transports |
|---|---|---|---|---|
| `channel.bits.use@1` | `ChannelBitsUseV1` | `ChannelBitsUseEvent` | `bits:read` | all |
| `channel.subscribe@1` | `ChannelSubscribeV1` | `ChannelSubscribeEvent` | `channel:read:subscriptions` | all |
| `channel.subscription.end@1` | `ChannelSubscriptionEndV1` | `ChannelSubscriptionEndEvent` | `channel:read:subscriptions` | all |
| `channel.subscription.gift@1` | `ChannelSubscriptionGiftV1` | `ChannelSubscriptionGiftEvent` | `channel:read:subscriptions` | all |
| `channel.subscription.message@1` | `ChannelSubscriptionMessageV1` | `ChannelSubscriptionMessageEvent` | `channel:read:subscriptions` | all |
| `channel.cheer@1` | `ChannelCheerV1` | `ChannelCheerEvent` | `bits:read` | all |
| `channel.channel_points_automatic_reward_redemption.add@1` | `ChannelPointsAutomaticRewardRedemptionAddV1` | `ChannelPointsAutomaticRewardRedemptionAddEvent` | `channel:read:redemptions` or `channel:manage:redemptions` | all |
| `channel.channel_points_automatic_reward_redemption.add@2` | `ChannelPointsAutomaticRewardRedemptionAddV2` | `ChannelPointsAutomaticRewardRedemptionAddEventV2` | `channel:read:redemptions` or `channel:manage:redemptions` | all |
| `channel.channel_points_custom_reward.add@1` | `ChannelPointsCustomRewardAddV1` | `ChannelPointsCustomRewardAddEvent` | `channel:read:redemptions` or `channel:manage:redemptions` | all |
| `channel.channel_points_custom_reward.update@1` | `ChannelPointsCustomRewardUpdateV1` (optional `rewardId`) | `ChannelPointsCustomRewardUpdateEvent` | `channel:read:redemptions` or `channel:manage:redemptions` | all |
| `channel.channel_points_custom_reward.remove@1` | `ChannelPointsCustomRewardRemoveV1` (optional `rewardId`) | `ChannelPointsCustomRewardRemoveEvent` | `channel:read:redemptions` or `channel:manage:redemptions` | all |
| `channel.channel_points_custom_reward_redemption.add@1` | `ChannelPointsCustomRewardRedemptionAddV1` (optional `rewardId`) | `ChannelPointsCustomRewardRedemptionAddEvent` | `channel:read:redemptions` or `channel:manage:redemptions` | all |
| `channel.channel_points_custom_reward_redemption.update@1` | `ChannelPointsCustomRewardRedemptionUpdateV1` (optional `rewardId`) | `ChannelPointsCustomRewardRedemptionUpdateEvent` | `channel:read:redemptions` or `channel:manage:redemptions` | all |
| `channel.custom_power_up_redemption.add@1` | `ChannelCustomPowerUpRedemptionAddV1` (optional `rewardId`) | `ChannelCustomPowerUpRedemptionAddEvent` | `bits:read` | all |
| `channel.poll.begin@1` | `ChannelPollBeginV1` | `ChannelPollBeginEvent` | `channel:read:polls` or `channel:manage:polls` | all |
| `channel.poll.progress@1` | `ChannelPollProgressV1` | `ChannelPollProgressEvent` | `channel:read:polls` or `channel:manage:polls` | all |
| `channel.poll.end@1` | `ChannelPollEndV1` | `ChannelPollEndEvent` | `channel:read:polls` or `channel:manage:polls` | all |
| `channel.prediction.begin@1` | `ChannelPredictionBeginV1` | `ChannelPredictionBeginEvent` | `channel:read:predictions` or `channel:manage:predictions` | all |
| `channel.prediction.progress@1` | `ChannelPredictionProgressV1` | `ChannelPredictionProgressEvent` | `channel:read:predictions` or `channel:manage:predictions` | all |
| `channel.prediction.lock@1` | `ChannelPredictionLockV1` | `ChannelPredictionLockEvent` | `channel:read:predictions` or `channel:manage:predictions` | all |
| `channel.prediction.end@1` | `ChannelPredictionEndV1` | `ChannelPredictionEndEvent` | `channel:read:predictions` or `channel:manage:predictions` | all |

"A or B" scopes are exposed as `AnyOfScopes`; single scopes as `RequiredScopes`. `AuthorizingUserId` is the broadcaster ID, so a WebSocket subscription made with another user's token fails the preflight with `TwitchAuthorizationException` before any request is sent.

## Notes

- **Reward filters.** The update/remove reward, redemption and custom Power-up factories accept an optional `rewardId`; when null it is omitted from the condition, and a blank value throws. `channel.channel_points_custom_reward.add` has no `reward_id` condition because the reward does not exist yet.
- **Anonymous gifts and cheers.** `ChannelSubscriptionGiftEvent.UserId/UserLogin/UserName` and `ChannelCheerEvent.UserId/UserLogin/UserName` are null when `IsAnonymous` is true. `CumulativeTotal` is null for anonymous gifts and when the gifter does not share it; `StreakMonths` is null when the subscriber does not share it.
- **Bits use.** `Type` is `cheer`, `power_up` or `custom_power_up`; `PowerUp` and `CustomPowerUp` are null unless they apply, and `Message` is null when no message was sent. Fragments carry `Cheermote` or `Emote` metadata.
- **Automatic rewards v1 vs v2.** v1 (`ChannelPointsAutomaticRewardRedemptionAddEvent`) reports `Reward.Cost`, an optional `UnlockedEmote`, `UserInput` and a message with emote positions (`MessageWithEmoteRanges`, inclusive `Begin`/`End` indexes). v2 (`...EventV2`) reports `Reward.ChannelPoints`, an optional `Reward.Emote` and an optional message made of fragments. Subscribe to the version whose payload you handle; the router keeps them apart.
- **Custom reward, redemption, poll and prediction families** share abstract bases (`ChannelPointsCustomRewardEventBase`, `ChannelPointsCustomRewardRedemptionEventBase`, `ChannelPollEventBase`, `ChannelPredictionEventBase`) so one handler can process several types. `Image` is null when no custom image was uploaded; `CooldownExpiresAt` (null or empty string) and `RedemptionsRedeemedCurrentStream` are null when not applicable. Redemption `UserInput` is an empty string when the user entered nothing.
- **Polls.** `channel.poll.begin` sends choices without vote counts, so they read as 0. Bits voting is no longer supported; `BitsVoting` and `BitsVotes` are false/0.
- **Predictions.** `channel.prediction.begin` outcomes carry only ID, title and color, so `Users`/`ChannelPoints` read as 0 and `TopPredictors` is empty. `ChannelPointsWon` is null in progress and lock events. `WinningOutcomeId` is null for canceled predictions.
- **Counters** such as Bits, Channel Points, costs and votes are `long`. Lists are never null: absent or null arrays read as empty. Discriminators (`Type`, `Tier`, `Status`, `Color`) stay strings so new Twitch values do not break deserialization.

## Example

```csharp
using TwitchDock.EventSub;

var router = new EventSubEventRouter()
    .On(EventSubEvents.ChannelCheerV1, (cheer, _, ct) =>
    {
        Console.WriteLine($"{cheer.UserName ?? "Anonymous"} cheered {cheer.Bits} Bits: {cheer.Message}");
        return Task.CompletedTask;
    })
    .On(EventSubEvents.ChannelSubscriptionGiftV1, (gift, _, ct) =>
    {
        Console.WriteLine($"{(gift.IsAnonymous ? "Anonymous" : gift.UserName)} gifted {gift.Total} tier {gift.Tier} subs");
        return Task.CompletedTask;
    })
    .On(EventSubEvents.ChannelPointsCustomRewardRedemptionAddV1, (redemption, _, ct) =>
    {
        Console.WriteLine($"{redemption.UserName} redeemed {redemption.Reward.Title}: {redemption.UserInput}");
        return Task.CompletedTask;
    })
    .On(EventSubEvents.ChannelPredictionEndV1, (prediction, _, ct) =>
    {
        Console.WriteLine($"{prediction.Title} {prediction.Status}, winner {prediction.WinningOutcomeId ?? "none"}");
        return Task.CompletedTask;
    })
    .OnRevocation((subscription, ct) =>
    {
        Console.WriteLine($"Revoked {subscription.Type}: {subscription.Status}");
        return Task.CompletedTask;
    });

// The user token belongs to broadcasterId and has bits:read, channel:read:subscriptions and channel:read:redemptions.
EventSubSubscriptionSpec[] subscriptions =
[
    EventSubSubscriptions.ChannelCheerV1(broadcasterId),
    EventSubSubscriptions.ChannelSubscriptionGiftV1(broadcasterId),
    EventSubSubscriptions.ChannelPointsCustomRewardRedemptionAddV1(broadcasterId, rewardId: hydrateRewardId),
    EventSubSubscriptions.ChannelPredictionEndV1(broadcasterId), // needs channel:read:predictions or channel:manage:predictions
];

await socket.RunAsync(
    async (session, resubscribe, ct) =>
    {
        if (!resubscribe) return;
        foreach (var subscription in subscriptions) await helix.SubscribeWebSocketAsync(subscription, session.Id, ct);
    },
    (message, ct) => router.DispatchAsync(message, ct),
    cancellationToken);
```

For webhooks, pass the same specs to `helix.CreateEventSubSubscriptionAsync(spec, transport)` with an app token, and dispatch verified payloads through `EventSubWebhookHandler` with the same router.
