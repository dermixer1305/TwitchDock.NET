# Stream schedules and iCalendar

All six endpoints are available through `helix.Schedule`, with a final `CancellationToken`. JSON responses keep the schedule object, broadcaster identity, segments, vacation and pagination; the forward enumerator yields individual segments. All modifying calls require `channel:manage:schedule` on the broadcaster's own user token. Twitch verifies channel eligibility and segment state.

## Read a schedule

```csharp
var page = await helix.Schedule.GetChannelStreamScheduleAsync(new()
{
    BroadcasterId = "123", Ids = ["segment-id"],
    StartTime = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero),
    First = 25, After = "cursor"
}, cancellationToken);
await foreach (var segment in helix.Schedule.EnumerateChannelStreamScheduleAsync(new()
{
    BroadcasterId = "123", First = 25
}, cancellationToken))
    Console.WriteLine($"{segment.StartTime:O}: {segment.Title}");
```

Reading accepts app or user tokens. Optional segment IDs are repeated query parameters, up to 100. `StartTime` selects the starting instant; when omitted Twitch starts after the current UTC time. Pages accept `First` 1–25 (default 20) and `After`; enumeration snapshots ID filters and preserves an initial cursor. The documented `utc_offset` parameter is explicitly unsupported by Twitch and is not exposed as a working option.

Each segment includes its ID, start/end instants, title, nullable cancellation timestamp, nullable category and recurring flag. The schedule's vacation is null when not enabled. For a canceled recurring occurrence, `CanceledUntil` is the segment's end timestamp. Use manual pages if you need broadcaster/vacation metadata as well as the segments. HTTP 404 can mean that no streaming schedule exists.

## Calendar text

```csharp
string calendar = await helix.Schedule.GetChannelICalendarAsync("123", cancellationToken);
await File.WriteAllTextAsync("schedule.ics", calendar, cancellationToken);
```

This endpoint is public: it does not require Client-Id or Authorization. The SDK's existing configured transport skips token acquisition, SDK credential headers and 401 token refresh for this call. This also works when the configured token provider currently has no token. Do not put credentials in `HttpClient.DefaultRequestHeaders`; the SDK normally adds them per authenticated request.

The response is `text/calendar` (RFC5545), returned as a string with its line endings and content preserved. It does not pass through JSON deserialization. The shared `TwitchHttpClient.SendTextAsync` path still handles cancellation, bounded GET/429 retries, error statuses and response disposal. The SDK does not interpret recurrence rules or write a file unless the caller does so.

## Vacation settings

```csharp
await helix.Schedule.UpdateChannelStreamScheduleAsync(new()
{
    BroadcasterId = "123", IsVacationEnabled = true,
    VacationStartTime = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero),
    VacationEndTime = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero),
    Timezone = "Europe/Berlin"
}, cancellationToken);
await helix.Schedule.UpdateChannelStreamScheduleAsync(new()
{
    BroadcasterId = "123", IsVacationEnabled = false
}, cancellationToken);
```

Enabling vacation requires start/end timestamps and an IANA timezone. The end must be after the start. Disabling it preserves explicit false and omits unused dates. All settings are query parameters on a bodyless PATCH, including encoded timezone and UTC instants. Success is HTTP 204. Timezone identifiers are checked by Twitch rather than the machine's platform-specific timezone registry.

## Create, update and delete segments

```csharp
var created = await helix.Schedule.CreateChannelStreamScheduleSegmentAsync("123", new()
{
    StartTime = new DateTimeOffset(2026, 10, 10, 14, 0, 0, TimeSpan.FromHours(2)),
    Timezone = "Europe/Berlin", Duration = 60, IsRecurring = true,
    CategoryId = "509658", Title = "Weekly stream"
}, cancellationToken);
string segmentId = created.Data.Segments.Single().Id;

await helix.Schedule.UpdateChannelStreamScheduleSegmentAsync("123", segmentId, new()
{
    Duration = 120, Title = "Longer stream", IsCanceled = false
}, cancellationToken);
await helix.Schedule.DeleteChannelStreamScheduleSegmentAsync("123", segmentId, cancellationToken);
```

Creation requires start time, IANA timezone and duration. Duration is exposed as an integer number of minutes, limited to 30–1380; the JSON wire value is a string, matching Twitch's request table and examples. Recurrence, category and title are optional. Titles accept up to 140 characters. Only partners and affiliates may create non-recurring segments.

Updates support start time, duration, category, title, canceled flag and timezone. Omitted values are left out of the JSON; explicit false and empty titles are preserved. Only partners/affiliates may change start time, and only for non-recurring segments. Changing a recurring segment's title, category, duration or timezone changes the recurring series. Canceling a recurring segment affects the first occurrence after the current UTC time, which may differ from the requested segment ID. Twitch enforces these rules; the SDK does not infer recurrence behavior from stale local data.

Deleting a recurring segment removes the entire recurring series, not just one occurrence. It sends a bodyless DELETE with broadcaster and segment IDs and accepts HTTP 204. To skip an occurrence, use the documented cancellation operation and account for the first-upcoming-occurrence rule instead. Create/update responses contain the schedule object with the affected segment.

`ScheduleTests` and `helix-schedule.json` verify complete response fields, all supported parameters, duration's wire type, nullable fields, vacation dependencies, scopes/identity, pagination and errors. Calendar tests prove that token acquisition/refresh is skipped, text is preserved, responses are disposed and shared GET retries/cancellation still apply. Errors retain Twitch's status/message through `TwitchApiException`; tests use simulated endpoints and do not change real schedules. Credentialed integration remains a stable-release requirement.

Sources: pinned official [Read Schedule](https://dev.twitch.tv/docs/api/reference/#get-channel-stream-schedule), [iCalendar](https://dev.twitch.tv/docs/api/reference/#get-channel-icalendar), [Vacation Settings](https://dev.twitch.tv/docs/api/reference/#update-channel-stream-schedule), [Create Segment](https://dev.twitch.tv/docs/api/reference/#create-channel-stream-schedule-segment), [Update Segment](https://dev.twitch.tv/docs/api/reference/#update-channel-stream-schedule-segment), [Delete Segment](https://dev.twitch.tv/docs/api/reference/#delete-channel-stream-schedule-segment).
