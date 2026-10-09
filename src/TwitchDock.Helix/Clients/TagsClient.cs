using TwitchDock.Core;
using TwitchDock.Helix.Models;

namespace TwitchDock.Helix.Clients;

/// <summary>Legacy Twitch-defined stream tags. Both endpoints are deprecated by Twitch in favor of channel-defined tags.</summary>
public sealed class TagsClient(TwitchHttpClient transport)
{
    private const string DeprecationMessage = "Twitch replaced Twitch-defined stream tags with channel-defined tags. Since February 28, 2023 this endpoint returns an empty list, "
        + "and Twitch documents HTTP 410 from July 13, 2023. Use the Tags of Get Channel Information or Get Streams instead.";
    private static readonly TwitchAuthorizationRequirement AnyToken = new([], allowAppToken: true);
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    [Obsolete(DeprecationMessage)]
    public Task<HelixPage<StreamTag>> GetAllStreamTagsAsync(GetAllStreamTagsRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new();
        if (request.After is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.After);
        return _transport.SendAsync(HttpMethod.Get, "tags/streams", HelixJsonContext.Default.HelixPageStreamTag,
            new HelixQuery().AddValues("tag_id", request.TagIds, 100).AddPage(request.First, request.After),
            authorization: AnyToken, cancellationToken: cancellationToken);
    }

    [Obsolete(DeprecationMessage)]
    public IAsyncEnumerable<StreamTag> EnumerateAllStreamTagsAsync(GetAllStreamTagsRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new();
        ArgumentNullException.ThrowIfNull(request.TagIds);
        var snapshot = request with { TagIds = request.TagIds.ToArray() };
        return HelixPagination.EnumerateAsync((cursor, ct) => GetAllStreamTagsAsync(snapshot with { After = cursor ?? snapshot.After }, ct), cancellationToken);
    }

    [Obsolete(DeprecationMessage)]
    public Task<HelixPage<StreamTag>> GetStreamTagsAsync(string broadcasterId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        return _transport.SendAsync(HttpMethod.Get, "streams/tags", HelixJsonContext.Default.HelixPageStreamTag,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId), authorization: AnyToken, cancellationToken: cancellationToken);
    }
}
