using TwitchSdk.Core;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Helix.Clients;

public sealed class VideosClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    public Task<HelixPage<TwitchVideo>> GetVideosAsync(GetVideosRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Ids);
        if ((request.Ids.Count > 0 ? 1 : 0) + (request.UserId is not null ? 1 : 0) + (request.GameId is not null ? 1 : 0) != 1)
            throw new ArgumentException("Specify exactly one of video IDs, user ID or game ID.", nameof(request));
        if (request.UserId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        if (request.GameId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.GameId);
        if (request.Language is not null && request.GameId is null) throw new ArgumentException("Language filtering requires game_id.", nameof(request));
        if ((request.Before is not null || request.After is not null) && request.UserId is null) throw new ArgumentException("Video cursors require user_id.", nameof(request));
        if (request.Ids.Count > 0 && (request.Period is not null || request.Sort is not null || request.Type is not null || request.First.HasValue))
            throw new ArgumentException("Video ID queries cannot include list filters or page size.", nameof(request));
        if (request.Period is not null and not "all" and not "day" and not "month" and not "week") throw new ArgumentException("Invalid video period.", nameof(request));
        if (request.Sort is not null and not "time" and not "trending" and not "views") throw new ArgumentException("Invalid video sort.", nameof(request));
        if (request.Type is not null and not "all" and not "archive" and not "highlight" and not "upload") throw new ArgumentException("Invalid video type.", nameof(request));
        return _transport.SendAsync(HttpMethod.Get, "videos", HelixJsonContext.Default.HelixPageTwitchVideo,
            new HelixQuery().AddValues("id", request.Ids).AddValue("user_id", request.UserId).AddValue("game_id", request.GameId)
                .AddValue("language", request.Language).AddValue("period", request.Period).AddValue("sort", request.Sort).AddValue("type", request.Type)
                .AddPage(request.First, request.After, request.Before), cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<TwitchVideo> EnumerateVideosAsync(GetVideosRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Before is not null) throw new ArgumentException("Forward enumeration cannot use before.", nameof(request));
        var snapshot = request with { Ids = request.Ids.ToArray() };
        return HelixPagination.EnumerateAsync(async (cursor, ct) =>
        {
            var page = await GetVideosAsync(snapshot with { After = cursor ?? snapshot.After }, ct).ConfigureAwait(false);
            return snapshot.UserId is not null ? page : new HelixPage<TwitchVideo> { Data = page.Data };
        }, cancellationToken);
    }

    /// <summary>Requires channel:manage:videos on a user token; at most five videos can be deleted per request.</summary>
    public Task<HelixPage<string>> DeleteVideosAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0) throw new ArgumentException("At least one video ID is required.", nameof(ids));
        return _transport.SendAsync(HttpMethod.Delete, "videos", HelixJsonContext.Default.HelixPageString, new HelixQuery().AddValues("id", ids, 5),
            authorization: new(["channel:manage:videos"]), cancellationToken: cancellationToken);
    }
}
