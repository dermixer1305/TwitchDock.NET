# Polls and predictions

`helix.Polls` and `helix.Predictions` provide create, list and end operations. Each method accepts a final `CancellationToken` and requires a user token belonging to the specified broadcaster. Read methods accept either the group's `channel:read:*` or `channel:manage:*` scope; creation and ending require the corresponding manage scope. App tokens are rejected when their kind is known. Twitch validates content, current state and channel eligibility.

## Polls

```csharp
var created = await helix.Polls.CreatePollAsync(new()
{
    BroadcasterId = "123", Title = "What next?", Duration = 60,
    Choices = [new() { Title = "Building" }, new() { Title = "Exploring" }],
    ChannelPointsVotingEnabled = true, ChannelPointsPerVote = 10
}, cancellationToken);
var pollId = created.Data.Single().Id;

var page = await helix.Polls.GetPollsAsync(new()
{
    BroadcasterId = "123", Ids = [pollId], First = 20, After = "cursor"
}, cancellationToken);
await foreach (var poll in helix.Polls.EnumeratePollsAsync(new()
{
    BroadcasterId = "123", First = 20
}, cancellationToken))
    Console.WriteLine($"{poll.Title}: {poll.Status}");

await helix.Polls.EndPollAsync(new()
{
    BroadcasterId = "123", Id = pollId, Status = "ARCHIVED"
}, cancellationToken);
```

Creating a poll starts it immediately, and only one poll may run at a time. The title accepts up to 60 characters, there must be 2–5 choices of up to 25 characters, and duration is 15–1800 seconds. Additional Channel Points votes are optional: when enabled, supply a cost of 1–1000000 points; when disabled, omit the cost. Explicit false is retained. Bits voting is no longer used and Twitch removed its response fields from the reference; the SDK keeps `BitsVotingEnabled`, `BitsPerVote` and `BitsVotes` as obsolete members (false/zero), and there are no Bits-voting request options. Twitch answers 403 when the broadcaster is not a partner or affiliate, and 400 when Channel Points voting is enabled for a channel without Channel Points.

Poll listing accepts up to 20 IDs, `First` 1–20 and an `After` cursor. Twitch retains polls for 90 days. Without IDs, results are newest first; with IDs, they follow request order. Duplicate IDs and polls belonging to other broadcasters are ignored; no matching IDs can result in 404. Forward enumeration snapshots the ID list and preserves the initial cursor.

Responses include broadcaster identity, choices with total and Channel Points vote counts, voting settings, status, duration and timestamps. `EndedAt` is null while active (Twitch omits the field). `TERMINATED` ends an active poll but keeps it visible; `ARCHIVED` hides a poll that is active, completed or terminated. Other response statuses such as `COMPLETED` are not valid ending commands. Ending returns 403 for a poll of another broadcaster or a deleted (`MODERATED`) poll and 404 for an unknown poll. Twitch verifies that the poll is active; the SDK does not perform a race-prone read before sending the transition.

## Predictions

```csharp
var created = await helix.Predictions.CreatePredictionAsync(new()
{
    BroadcasterId = "123", Title = "Will we finish?", PredictionWindow = 120,
    Outcomes = [new() { Title = "Yes" }, new() { Title = "No" }]
}, cancellationToken);
var prediction = created.Data.Single();

var page = await helix.Predictions.GetPredictionsAsync(new()
{
    BroadcasterId = "123", Ids = [prediction.Id], First = 25, After = "cursor"
}, cancellationToken);
await foreach (var item in helix.Predictions.EnumeratePredictionsAsync(new()
{
    BroadcasterId = "123", First = 25
}, cancellationToken))
    Console.WriteLine($"{item.Title}: {item.Status}");

await helix.Predictions.EndPredictionAsync(new()
{
    BroadcasterId = "123", Id = prediction.Id, Status = "LOCKED"
}, cancellationToken);
await helix.Predictions.EndPredictionAsync(new()
{
    BroadcasterId = "123", Id = prediction.Id,
    Status = "RESOLVED", WinningOutcomeId = prediction.Outcomes[0].Id
}, cancellationToken);
// Alternatively, use Status = "CANCELED" to refund participants.
```

Creating a prediction starts it immediately, and a broadcaster may have only one unresolved prediction. Titles accept up to 45 characters, outcomes 2–10 choices of up to 25 characters, and the prediction window is 30–1800 seconds. The current request table permits ten outcomes; the stale error-description sentence referring to exactly two outcomes is not used as the validation rule.

Listing accepts at most 25 IDs and page sizes of 1–25, with `After` pagination and newest-first ordering. Both manual pages and a forward enumerator are available. Duplicate/non-owned IDs are ignored by Twitch. Response models preserve the outcome list, viewer counts, Channel Points totals, colors, nullable top-predictor lists, winner ID and lifecycle timestamps. Points use 64-bit integers. `ChannelPointsWon` is nullable so an undistributed value can remain unknown. Outcome colors are `BLUE` or `PINK`. `LockedAt` is null until the prediction is locked.

Ending supports `LOCKED`, `CANCELED` and `RESOLVED`. Resolving requires `WinningOutcomeId`. An active prediction can move to any of these states; a locked prediction can only be resolved or canceled. Twitch enforces valid transitions and the 24-hour resolution deadline after the window closes, after which unresolved predictions are canceled and points refunded. The SDK does not schedule resolution, infer winners, or hide state errors.

`PollsAndPredictionsTests` and `helix-polls-predictions.json` verify every documented response field, nullable states, all request/query fields, limits, alternative scopes, ownership, pagination and mutation failures. HTTP errors retain their Twitch message/status in `TwitchApiException`; mutations are not replayed after ambiguous server failures. The fixtures are synthetic field/state coverage, not recordings of live poll or prediction creation. Credentialed integration remains a stable-release requirement.

Sources: pinned official [Get Polls](https://dev.twitch.tv/docs/api/reference/#get-polls), [Create Poll](https://dev.twitch.tv/docs/api/reference/#create-poll), [End Poll](https://dev.twitch.tv/docs/api/reference/#end-poll), [Get Predictions](https://dev.twitch.tv/docs/api/reference/#get-predictions), [Create Prediction](https://dev.twitch.tv/docs/api/reference/#create-prediction), [End Prediction](https://dev.twitch.tv/docs/api/reference/#end-prediction).
