using System.Text;
using System.Text.Json;
using TwitchDock.Core;
using TwitchDock.Helix.Models;

namespace TwitchDock.Helix.Clients;

public sealed class ChannelsClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public Task<HelixPage<ChannelInformation>> GetChannelInformationAsync(IReadOnlyList<string> broadcasterIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(broadcasterIds);
        if (broadcasterIds.Count == 0) throw new ArgumentException("At least one broadcaster ID is required.", nameof(broadcasterIds));
        return _transport.SendAsync(HttpMethod.Get, "channels", HelixJsonContext.Default.HelixPageChannelInformation,
            new HelixQuery().AddValues("broadcaster_id", broadcasterIds), cancellationToken: cancellationToken);
    }

    public Task ModifyChannelInformationAsync(ModifyChannelInformationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        if (request.GameId is null && request.BroadcasterLanguage is null && request.Title is null && request.Delay is null
            && request.Tags is null && request.ContentClassificationLabels is null && request.IsBrandedContent is null)
            throw new ArgumentException("At least one channel property must be supplied.", nameof(request));
        if (request.Title is not null && (request.Title.Length == 0 || request.Title.EnumerateRunes().Count() > 140))
            throw new ArgumentException("Title must contain between 1 and 140 Unicode code points.", nameof(request));
        if (request.Delay is < 0 or > 900) throw new ArgumentOutOfRangeException(nameof(request), "Delay must be between 0 and 900 seconds.");
        if (request.Tags is { } tags)
        {
            if (tags.Count > 10) throw new ArgumentException("At most 10 tags are allowed.", nameof(request));
            foreach (var tag in tags)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(tag);
                if (tag.EnumerateRunes().Count() > 25 || tag.Any(char.IsWhiteSpace)) throw new ArgumentException("Tags must contain at most 25 characters and no spaces.", nameof(request));
            }
        }
        if (request.ContentClassificationLabels is { } labels)
            foreach (var label in labels) { ArgumentNullException.ThrowIfNull(label); ArgumentException.ThrowIfNullOrWhiteSpace(label.Id); }
        return _transport.SendAsync(HttpMethod.Patch, "channels", new HelixQuery().AddValue("broadcaster_id", request.BroadcasterId),
            jsonBody: JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.ModifyChannelInformationRequest),
            authorization: new([TwitchScopes.ChannelManageBroadcast], requiredUserId: request.BroadcasterId), cancellationToken: cancellationToken);
    }

    public Task<HelixPage<ChannelEditor>> GetChannelEditorsAsync(string broadcasterId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        return _transport.SendAsync(HttpMethod.Get, "channels/editors", HelixJsonContext.Default.HelixPageChannelEditor,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId),
            authorization: new([TwitchScopes.ChannelReadEditors], requiredUserId: broadcasterId), cancellationToken: cancellationToken);
    }

    public Task<HelixPage<FollowedChannel>> GetFollowedChannelsAsync(GetFollowedChannelsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        if (request.BroadcasterId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        return _transport.SendAsync(HttpMethod.Get, "channels/followed", HelixJsonContext.Default.HelixPageFollowedChannel,
            new HelixQuery().AddValue("user_id", request.UserId).AddValue("broadcaster_id", request.BroadcasterId).AddPage(request.First, request.After),
            authorization: new([TwitchScopes.UserReadFollows], requiredUserId: request.UserId), cancellationToken: cancellationToken);
    }

    /// <summary>Without follower scope or broadcaster/moderator status, Twitch returns only the total. Filtering by user requires the scope.</summary>
    public Task<HelixPage<ChannelFollower>> GetChannelFollowersAsync(GetChannelFollowersRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        if (request.UserId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        return _transport.SendAsync(HttpMethod.Get, "channels/followers", HelixJsonContext.Default.HelixPageChannelFollower,
            new HelixQuery().AddValue("broadcaster_id", request.BroadcasterId).AddValue("user_id", request.UserId).AddPage(request.First, request.After),
            // Moderator status cannot be inferred from token metadata. Do not require broadcaster identity.
            authorization: new(request.UserId is null ? [] : [TwitchScopes.ModeratorReadFollowers]), cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<FollowedChannel> EnumerateFollowedChannelsAsync(GetFollowedChannelsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return HelixPagination.EnumerateAsync((cursor, ct) => GetFollowedChannelsAsync(request with { After = cursor ?? request.After }, ct), cancellationToken);
    }

    public IAsyncEnumerable<ChannelFollower> EnumerateFollowersAsync(GetChannelFollowersRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return HelixPagination.EnumerateAsync((cursor, ct) => GetChannelFollowersAsync(request with { After = cursor ?? request.After }, ct), cancellationToken);
    }
}
