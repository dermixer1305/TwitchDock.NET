# Streams and markers

`helix.Streams` groups all five Streams endpoints. The existing `helix.GetStreamsAsync` and `helix.EnumerateStreamsAsync` methods delegate to this group. All methods accept a final `CancellationToken`; see [foundation contracts](helix-foundation.md) for stream search filters and [quickstart](quickstart.md) for client setup.

## Stream key and followed streams

```csharp
var credentials = await helix.Streams.GetStreamKeyAsync("123", cancellationToken);
// Pass credentials.Data.Single().StreamKey only to the authorized streaming configuration.

var page = await helix.Streams.GetFollowedStreamsAsync(new()
{
    UserId = "123", First = 100, After = "cursor"
}, cancellationToken);
await foreach (var stream in helix.Streams.EnumerateFollowedStreamsAsync(new()
{
    UserId = "123"
}, cancellationToken))
    Console.WriteLine(stream.Title);
```

The stream key requires a user token with `channel:read:stream_key` and a matching broadcaster ID. Treat the returned key as a credential. `StreamKeyResult.ToString()` redacts it; explicit JSON serialization or accessing `StreamKey` still exposes its value. A 403 response can mean Twitch requires additional account setup; its message is preserved in `TwitchApiException`.

Followed streams require `user:read:follows` and a user token belonging to `UserId`. `First` accepts 1–100; omitted values use Twitch's default of 100. `After` starts from a cursor. Manual pages expose pagination and the forward enumerator follows it. This returns only currently live followed broadcasters and uses the complete `TwitchStream` model. Ranking changes can cause duplicate or missing streams while paging; the SDK preserves Twitch's results without deduplication. Tags and deprecated fields follow the same rules as Get Streams.

## Create a marker

```csharp
var created = await helix.Streams.CreateStreamMarkerAsync(new()
{
    UserId = "123", Description = "Highlight begins"
}, cancellationToken);
var marker = created.Data.Single();
Console.WriteLine($"{marker.Id} at {marker.PositionSeconds}s");
```

Requires a user token with `channel:manage:broadcast`. The token owner must be the broadcaster or one of the broadcaster's editors; the SDK allows different user IDs and lets Twitch verify the editor role. The broadcaster must be live with VOD enabled, and the stream cannot be a rerun. `Description` is optional and limited to 140 characters. Null omits it; an explicit empty string is preserved. The response contains the marker ID, timestamp, position in seconds from stream start and description. HTTP 404 can represent a non-live stream, unavailable user or disabled VOD.

## Read markers

```csharp
var recent = await helix.Streams.GetStreamMarkersAsync(new()
{
    UserId = "123", First = 20, Before = "previous-cursor"
}, cancellationToken);
var video = await helix.Streams.GetStreamMarkersAsync(new()
{
    VideoId = "video-id", First = 100, After = "cursor"
}, cancellationToken);
await foreach (var group in helix.Streams.EnumerateStreamMarkersAsync(new()
{
    VideoId = "video-id", First = 100
}, cancellationToken))
    foreach (var markedVideo in group.Videos)
        foreach (var marker in markedVideo.Markers)
            Console.WriteLine($"{group.UserName}: {marker.Description} — {marker.Url}");
```

Requires a user token with **either** `user:read:broadcast` **or** `channel:manage:broadcast`. Twitch verifies video ownership or broadcaster editor status. Specify exactly one of `UserId` (most recent video) and `VideoId` (a specific VOD). Pages accept `First` 1–100 and either `Before` or `After`. Forward enumeration disallows `Before` and preserves an initial `After` cursor.

Responses are grouped by the user who created the markers, then by video. Each marker includes its ID, creation timestamp, possibly empty description, stream-relative position and Highlighter URL. Enumeration yields the original groups from each page; it does not merge repeated creator/video groups across pages. Markers within a video are ordered by creation time ascending.

`StreamsTests` and `helix-streams.json` check every documented response field, both selectors, query/body encoding, boundaries, alternative scopes, editor access, key redaction, pagination and errors. Non-success responses remain `TwitchApiException`; shared transport retry/cancellation rules apply. Tests use simulated responses; live integration remains required before a stable release.

Sources: pinned official [Get Stream Key](https://dev.twitch.tv/docs/api/reference/#get-stream-key), [Get Streams](https://dev.twitch.tv/docs/api/reference/#get-streams), [Get Followed Streams](https://dev.twitch.tv/docs/api/reference/#get-followed-streams), [Create Stream Marker](https://dev.twitch.tv/docs/api/reference/#create-stream-marker), [Get Stream Markers](https://dev.twitch.tv/docs/api/reference/#get-stream-markers).
