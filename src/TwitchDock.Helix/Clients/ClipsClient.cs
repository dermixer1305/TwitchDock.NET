using TwitchDock.Core;
using TwitchDock.Helix.Models;

namespace TwitchDock.Helix.Clients;

public sealed class ClipsClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    /// <summary>Requires clips:edit on a user token. The result starts asynchronous capture; it does not confirm publication.</summary>
    public Task<HelixPage<CreatedClip>> CreateClipAsync(CreateClipRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        ValidateDuration(request.Duration);
        return _transport.SendAsync(HttpMethod.Post, "clips", HelixJsonContext.Default.HelixPageCreatedClip,
            new HelixQuery().AddValue("broadcaster_id", request.BroadcasterId).AddValue("title", request.Title).AddValue("duration", request.Duration),
            authorization: new([TwitchScopes.ClipsEdit]), cancellationToken: cancellationToken);
    }

    /// <summary>Requires editor:manage:clips or channel:manage:clips, and the corresponding editor/broadcaster authorization.</summary>
    public Task<HelixPage<CreatedClip>> CreateClipFromVodAsync(CreateClipFromVodRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EditorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.VodId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Title);
        ValidateDuration(request.Duration);
        if (request.VodOffset < (request.Duration ?? 30)) throw new ArgumentOutOfRangeException(nameof(request), "The clip end offset must be at least its duration.");
        return _transport.SendAsync(HttpMethod.Post, "videos/clips", HelixJsonContext.Default.HelixPageCreatedClip,
            new HelixQuery().AddValue("editor_id", request.EditorId).AddValue("broadcaster_id", request.BroadcasterId)
                .AddValue("vod_id", request.VodId).AddValue("vod_offset", request.VodOffset).AddValue("duration", request.Duration).AddValue("title", request.Title),
            authorization: ClipManagement(request.EditorId), cancellationToken: cancellationToken);
    }

    public Task<HelixPage<TwitchClip>> GetClipsAsync(GetClipsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Ids);
        if ((request.Ids.Count > 0 ? 1 : 0) + (request.BroadcasterId is not null ? 1 : 0) + (request.GameId is not null ? 1 : 0) != 1)
            throw new ArgumentException("Specify exactly one of clip IDs, broadcaster ID or game ID.", nameof(request));
        if (request.BroadcasterId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        if (request.GameId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.GameId);
        if (request.StartedAt > request.EndedAt) throw new ArgumentException("The end date must not precede the start date.", nameof(request));
        return _transport.SendAsync(HttpMethod.Get, "clips", HelixJsonContext.Default.HelixPageTwitchClip,
            new HelixQuery().AddValue("broadcaster_id", request.BroadcasterId).AddValue("game_id", request.GameId).AddValues("id", request.Ids)
                .AddValue("started_at", request.StartedAt).AddValue("ended_at", request.EndedAt).AddPage(request.First, request.After, request.Before)
                .AddValue("is_featured", request.IsFeatured), cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<TwitchClip> EnumerateClipsAsync(GetClipsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Before is not null) throw new ArgumentException("Forward enumeration cannot use before.", nameof(request));
        var snapshot = request with { Ids = request.Ids.ToArray() };
        return HelixPagination.EnumerateAsync((cursor, ct) => GetClipsAsync(snapshot with { After = cursor ?? snapshot.After }, ct), cancellationToken);
    }

    /// <summary>Returns temporary URLs without downloading or forwarding Twitch credentials to a media host.</summary>
    public Task<HelixPage<ClipDownload>> GetClipsDownloadAsync(GetClipsDownloadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EditorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        ArgumentNullException.ThrowIfNull(request.ClipIds);
        if (request.ClipIds.Count == 0) throw new ArgumentException("At least one clip ID is required.", nameof(request));
        return _transport.SendAsync(HttpMethod.Get, "clips/downloads", HelixJsonContext.Default.HelixPageClipDownload,
            new HelixQuery().AddValue("editor_id", request.EditorId).AddValue("broadcaster_id", request.BroadcasterId).AddValues("clip_id", request.ClipIds, 10),
            authorization: ClipManagement(request.EditorId), cancellationToken: cancellationToken);
    }

    private static TwitchAuthorizationRequirement ClipManagement(string editorId)
        => new([], allowAppToken: true, requiredUserId: editorId, anyUserScopes: [TwitchScopes.EditorManageClips, TwitchScopes.ChannelManageClips]);

    private static void ValidateDuration(decimal? duration)
    {
        if (duration is { } value && (value is < 5 or > 60 || decimal.Round(value, 1) != value))
            throw new ArgumentOutOfRangeException(nameof(duration), "Clip duration must be 5–60 seconds with at most one decimal place.");
    }
}
