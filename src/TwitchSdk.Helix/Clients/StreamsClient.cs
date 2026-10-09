using System.Text;
using System.Text.Json;
using TwitchSdk.Core;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Helix.Clients;

public sealed class StreamsClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public Task<HelixPage<StreamKeyResult>> GetStreamKeyAsync(string broadcasterId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        return _transport.SendAsync(HttpMethod.Get, "streams/key", HelixJsonContext.Default.HelixPageStreamKeyResult,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId),
            authorization: new([TwitchScopes.ChannelReadStreamKey], requiredUserId: broadcasterId), cancellationToken: cancellationToken);
    }

    public Task<HelixPage<TwitchStream>> GetStreamsAsync(GetStreamsRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new();
        if (request.Type is not null and not "all" and not "live") throw new ArgumentException("Stream type must be all or live.", nameof(request));
        var query = new HelixQuery().AddValues("user_id", request.UserIds).AddValues("user_login", request.UserLogins)
            .AddValues("game_id", request.GameIds).AddValues("language", request.Languages)
            .AddValue("type", request.Type).AddPage(request.First, request.After, request.Before);
        return _transport.SendAsync(HttpMethod.Get, "streams", HelixJsonContext.Default.HelixPageTwitchStream, query, cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<TwitchStream> EnumerateStreamsAsync(GetStreamsRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new();
        if (request.Before is not null) throw new ArgumentException("Forward enumeration cannot use before.", nameof(request));
        ArgumentNullException.ThrowIfNull(request.UserIds);
        ArgumentNullException.ThrowIfNull(request.UserLogins);
        ArgumentNullException.ThrowIfNull(request.GameIds);
        ArgumentNullException.ThrowIfNull(request.Languages);
        var snapshot = request with { UserIds = request.UserIds.ToArray(), UserLogins = request.UserLogins.ToArray(), GameIds = request.GameIds.ToArray(), Languages = request.Languages.ToArray() };
        return HelixPagination.EnumerateAsync((cursor, ct) => GetStreamsAsync(snapshot with { After = cursor ?? snapshot.After }, ct), cancellationToken);
    }

    public Task<HelixPage<TwitchStream>> GetFollowedStreamsAsync(GetFollowedStreamsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        return _transport.SendAsync(HttpMethod.Get, "streams/followed", HelixJsonContext.Default.HelixPageTwitchStream,
            new HelixQuery().AddValue("user_id", request.UserId).AddPage(request.First, request.After),
            authorization: new([TwitchScopes.UserReadFollows], requiredUserId: request.UserId), cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<TwitchStream> EnumerateFollowedStreamsAsync(GetFollowedStreamsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return HelixPagination.EnumerateAsync((cursor, ct) => GetFollowedStreamsAsync(request with { After = cursor ?? request.After }, ct), cancellationToken);
    }

    public Task<HelixPage<CreatedStreamMarker>> CreateStreamMarkerAsync(CreateStreamMarkerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        if (request.Description?.EnumerateRunes().Count() > 140) throw new ArgumentException("Marker descriptions may contain at most 140 Unicode code points.", nameof(request));
        // Editors may create markers for another broadcaster; Twitch checks that role.
        return _transport.SendAsync(HttpMethod.Post, "streams/markers", HelixJsonContext.Default.HelixPageCreatedStreamMarker,
            jsonBody: JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.CreateStreamMarkerRequest),
            authorization: new([TwitchScopes.ChannelManageBroadcast]), cancellationToken: cancellationToken);
    }

    public Task<HelixPage<StreamMarkerGroup>> GetStreamMarkersAsync(GetStreamMarkersRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((request.UserId is null) == (request.VideoId is null)) throw new ArgumentException("Specify exactly one of user ID or video ID.", nameof(request));
        if (request.UserId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        if (request.VideoId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.VideoId);
        return _transport.SendAsync(HttpMethod.Get, "streams/markers", HelixJsonContext.Default.HelixPageStreamMarkerGroup,
            new HelixQuery().AddValue("user_id", request.UserId).AddValue("video_id", request.VideoId).AddPage(request.First, request.After, request.Before),
            authorization: new([], anyUserScopes: [TwitchScopes.UserReadBroadcast, TwitchScopes.ChannelManageBroadcast]), cancellationToken: cancellationToken);
    }

    /// <summary>Yields marker groups as returned on each page; groups are not merged across pages.</summary>
    public IAsyncEnumerable<StreamMarkerGroup> EnumerateStreamMarkersAsync(GetStreamMarkersRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Before is not null) throw new ArgumentException("Forward enumeration cannot use before.", nameof(request));
        return HelixPagination.EnumerateAsync((cursor, ct) => GetStreamMarkersAsync(request with { After = cursor ?? request.After }, ct), cancellationToken);
    }
}
