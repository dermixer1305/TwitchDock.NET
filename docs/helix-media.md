# Helix: Clips, Videos, Charity and Teams

Use `helix.Clips`, `helix.Videos`, `helix.Charity` and `helix.Teams`. These clients share cancellation, error handling, bounded retries and token metadata preflight. Contracts were reviewed against the official snapshot of 2026-10-09. Tests cover every documented response field, nullability examples, routes/parameters, alternative scopes and errors. Live Twitch integration remains a release-level requirement.

## Clips

```csharp
var captured = await helix.Clips.CreateClipAsync(new()
{
    BroadcasterId = broadcasterId, Title = "An interesting moment", Duration = 15.5m
}, ct);
var fromVod = await helix.Clips.CreateClipFromVodAsync(new()
{
    EditorId = editorId, BroadcasterId = broadcasterId, VodId = videoId,
    VodOffset = 100, Duration = 15.5m, Title = "An earlier moment"
}, ct);
```

CreateClip supports broadcaster_id, title and duration and requires a user token with `clips:edit`. It returns Id and EditUrl with HTTP 202. Creation is asynchronous: query GetClips with the returned ID to confirm it exists. Twitch says to treat creation as failed if it remains absent after 60 seconds. Channel clip restrictions still apply. [Create Clip](https://dev.twitch.tv/docs/api/reference/#create-clip).

CreateClipFromVod supports editor_id, broadcaster_id, vod_id, vod_offset, duration and title. It accepts documented app/user authorization and either `editor:manage:clips` **or** `channel:manage:clips`; both scopes are not required. EditorId must match a known user-token identity; Twitch checks channel/editor relationships. VodOffset is the **end** position and must be at least Duration (default 30). Both creation methods accept 5–60 seconds with one decimal place, using decimal and invariant query formatting. [Create Clip From VOD](https://dev.twitch.tv/docs/api/reference/#create-clip-from-vod).

```csharp
var clips = await helix.Clips.GetClipsAsync(new()
{
    BroadcasterId = broadcasterId,
    StartedAt = DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
    EndedAt = DateTimeOffset.Parse("2026-09-02T00:00:00Z"),
    IsFeatured = false, First = 100, After = cursor
}, ct);
var specificClips = await helix.Clips.GetClipsAsync(new() { Ids = [clipId] }, ct);
var downloads = await helix.Clips.GetClipsDownloadAsync(new()
{
    EditorId = editorId, BroadcasterId = broadcasterId, ClipIds = [clipId]
}, ct);
```

GetClips accepts exactly one of BroadcasterId, GameId, or repeated Ids (up to 100), plus StartedAt, EndedAt, First, Before, After and IsFeatured. App/user tokens need no additional scopes. Omitting IsFeatured means all clips; false means only unfeatured clips. VideoId may be empty and VodOffset may be null while a VOD is unavailable or pending; this returned offset describes the clip's **start**. `EnumerateClipsAsync` follows forward cursors. Twitch limits a query to approximately 1,000 clips, so split date windows when necessary. [Get Clips](https://dev.twitch.tv/docs/api/reference/#get-clips).

GetClipsDownload supports EditorId, BroadcasterId and 1–10 ClipIds, with the same alternative management scopes as VOD creation. It returns ClipId and nullable LandscapeDownloadUrl/PortraitDownloadUrl. These are temporary URLs; the SDK neither downloads them nor forwards authorization headers to their hosts. [Get Clips Download](https://dev.twitch.tv/docs/api/reference/#get-clips-download).

## Videos

```csharp
var specific = await helix.Videos.GetVideosAsync(new() { Ids = [videoId] }, ct);
var recent = await helix.Videos.GetVideosAsync(new()
{
    UserId = broadcasterId, Period = "week", Sort = "views", Type = "archive",
    First = 100, After = cursor
}, ct);
var byGame = await helix.Videos.GetVideosAsync(new()
{
    GameId = gameId, Language = "de", Period = "month", Sort = "time", Type = "all", First = 100
}, ct);
```

GetVideos accepts app/user tokens without extra scopes. Supply exactly one of Ids (up to 100), UserId, or GameId. Language applies only with GameId; Period, Sort, Type and First apply with UserId/GameId; After/Before are documented only with UserId. `EnumerateVideosAsync` pages user listings and yields a single result page for ID/game queries. Twitch caps category queries at 500 videos. [Get Videos](https://dev.twitch.tv/docs/api/reference/#get-videos).

TwitchVideo includes nullable StreamId and MutedSegments; each muted segment preserves Duration and Offset in seconds. Video Duration remains Twitch's text (for example `1h2m3s`) instead of assuming TimeSpan syntax. Thumbnail placeholders are retained for the application to substitute.

```csharp
var deletedIds = await helix.Videos.DeleteVideosAsync([videoId], ct);
```

DeleteVideos requires a user token with `channel:manage:videos`, accepts 1–5 IDs in a single request and returns IDs actually deleted. Twitch verifies ownership; one unauthorized video prevents deletion of the requested set. The SDK does not split deletion into requests or retry ambiguous mutation failures. [Delete Videos](https://dev.twitch.tv/docs/api/reference/#delete-videos).

## Charity

```csharp
var campaign = await helix.Charity.GetCharityCampaignAsync(broadcasterId, ct);
var donations = await helix.Charity.GetCharityCampaignDonationsAsync(
    new() { BroadcasterId = broadcasterId, First = 100, After = cursor }, ct);
await foreach (var donation in helix.Charity.EnumerateDonationsAsync(
    new() { BroadcasterId = broadcasterId, First = 100 }, ct))
    Console.WriteLine($"{donation.Amount.Value} minor units of {donation.Amount.Currency}");
```

Both require a user token with `channel:read:charity` and a matching BroadcasterId. Twitch checks partner/affiliate eligibility. Campaign data is available only while active; an empty list is valid. CharityCampaign preserves all broadcaster/charity metadata, CurrentAmount and nullable TargetAmount. Donations preserve Id, CampaignId, UserId/Login/Name and Amount; paging supports First and After. [Get Charity Campaign](https://dev.twitch.tv/docs/api/reference/#get-charity-campaign), [Get Charity Campaign Donations](https://dev.twitch.tv/docs/api/reference/#get-charity-campaign-donations).

CharityAmount stores integer minor units in Value, DecimalPlaces and the currency code. Do not treat Value as major currency units or assume every currency uses two decimal places.

## Teams

```csharp
var memberships = await helix.Teams.GetChannelTeamsAsync(broadcasterId, ct);
var teamByName = await helix.Teams.GetTeamsAsync(new() { Name = "example" }, ct);
var teamById = await helix.Teams.GetTeamsAsync(new() { Id = teamId }, ct);
```

Both accept app/user tokens without extra scopes. GetChannelTeams uses BroadcasterId and may return an empty list. GetTeams accepts exactly one of Name/Id and includes typed TeamMember entries. All team fields are represented, including nullable BackgroundImageUrl/Banner and CreatedAt/UpdatedAt. Info may contain HTML, Markdown and newlines; the SDK preserves it as text. Applications decide how to safely display it. [Get Channel Teams](https://dev.twitch.tv/docs/api/reference/#get-channel-teams), [Get Teams](https://dev.twitch.tv/docs/api/reference/#get-teams).

## Errors and scope constants

TwitchApiException retains HTTP status and Twitch error/message. Handle restrictions, missing media/resources, unavailable campaigns, insufficient authorization and rate limits explicitly. Local checks use TwitchAuthorizationException for known metadata. MissingScopes contains required scopes; RequiredAnyOfScopes contains alternatives of which at least one is needed. App grants and roles are verified by Twitch.

`TwitchScopes` supplies constants generated from the official scope table, including `TwitchScopes.EditorManageClips`. The catalog also includes documented legacy scopes; a constant does not imply support for an obsolete transport.
