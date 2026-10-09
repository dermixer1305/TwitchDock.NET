# Hype Train status

`HelixClient.HypeTrain.GetHypeTrainStatusAsync` implements the current [Get Hype Train Status](https://dev.twitch.tv/docs/api/reference/#get-hype-train-status) endpoint. It requires the broadcaster's user token with `channel:read:hype_train`. Known app tokens, missing scopes and mismatched user IDs are rejected before HTTP; Twitch validates unknown metadata.

```csharp
var response = await helix.HypeTrain.GetHypeTrainStatusAsync(broadcasterId, ct);
foreach (var status in response.Data)
{
    if (status.Current is { } train)
        Console.WriteLine($"Level {train.Level}: {train.Progress}/{train.Goal}; expires {train.ExpiresAt:O}");
    if (status.AllTimeHigh is { } record)
        Console.WriteLine($"Record level {record.Level}, achieved {record.AchievedAt:O}");
}
```

`Current` is null when no Hype Train is active. `AllTimeHigh` and `SharedAllTimeHigh` are nullable for channels without the corresponding history. Current trains include broadcaster identity, level, points, top contributions, shared participants, start/expiry timestamps, type and shared flag. `SharedTrainParticipants` is nullable when the train is not shared. Point counters use 64-bit integers. Train types and contribution types are strings to preserve future values, including the documented contribution type `other`.

The call has only a `broadcaster_id` query parameter and no cursor or historical-events pagination. Timestamps use `DateTimeOffset` and .NET's 100-nanosecond precision; additional fractional digits in Twitch timestamps are truncated during deserialization. HTTP 400, 401 and 500 use the shared structured error and bounded refresh/retry handling. Account eligibility for starting a Hype Train is controlled by Twitch; the API reference lists no additional access gate for this status call.

Offline fixtures cover every documented field, shared and unshared trains, nullable records, large counters and timestamps with more than seven fractional digits. HTTP tests cover the request and authorization/error behavior; credentialed integration remains outstanding.
