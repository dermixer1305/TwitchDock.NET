using TwitchSdk.Core;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Helix.Clients;

// Chat catalog: chatters, emotes and chat badges.
public sealed partial class ChatClient
{
    private const int CatalogMaxChattersPerPage = 1000;
    private const int CatalogMaxEmoteSetIds = 25;
    private static readonly TwitchAuthorizationRequirement CatalogAnyToken = new([], allowAppToken: true);

    /// <summary>User token with moderator:read:chatters for ModeratorId, or an app token authorized by that moderator.</summary>
    public Task<HelixPage<Chatter>> GetChattersAsync(GetChattersRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModeratorId);
        if (request.First is < 1 or > CatalogMaxChattersPerPage) throw new ArgumentOutOfRangeException(nameof(request), "Page size must be between 1 and 1000.");
        if (request.After is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.After);
        return _transport.SendAsync(HttpMethod.Get, "chat/chatters", HelixJsonContext.Default.HelixPageChatter,
            new HelixQuery().AddValue("broadcaster_id", request.BroadcasterId).AddValue("moderator_id", request.ModeratorId)
                .AddValue("first", request.First).AddValue("after", request.After),
            authorization: new([TwitchScopes.ModeratorReadChatters], allowAppToken: true, requiredUserId: request.ModeratorId), cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<Chatter> EnumerateChattersAsync(GetChattersRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return HelixPagination.EnumerateAsync((cursor, ct) => GetChattersAsync(request with { After = cursor ?? request.After }, ct), cancellationToken);
    }

    public Task<ChatEmotesResponse<ChannelEmote>> GetChannelEmotesAsync(string broadcasterId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        return _transport.SendAsync(HttpMethod.Get, "chat/emotes", HelixJsonContext.Default.ChatEmotesResponseChannelEmote,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId), authorization: CatalogAnyToken, cancellationToken: cancellationToken);
    }

    public Task<ChatEmotesResponse<GlobalEmote>> GetGlobalEmotesAsync(CancellationToken cancellationToken = default)
        => _transport.SendAsync(HttpMethod.Get, "chat/emotes/global", HelixJsonContext.Default.ChatEmotesResponseGlobalEmote,
            authorization: CatalogAnyToken, cancellationToken: cancellationToken);

    /// <summary>Accepts 1 to 25 emote set IDs. Twitch returns only found sets and ignores duplicates.</summary>
    public Task<ChatEmotesResponse<EmoteSetEmote>> GetEmoteSetsAsync(IReadOnlyList<string> emoteSetIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(emoteSetIds);
        if (emoteSetIds.Count == 0) throw new ArgumentException("At least one emote set ID is required.", nameof(emoteSetIds));
        return _transport.SendAsync(HttpMethod.Get, "chat/emotes/set", HelixJsonContext.Default.ChatEmotesResponseEmoteSetEmote,
            new HelixQuery().AddValues("emote_set_id", emoteSetIds, CatalogMaxEmoteSetIds), authorization: CatalogAnyToken, cancellationToken: cancellationToken);
    }

    /// <summary>Requires a user token with user:read:emotes that belongs to UserId.</summary>
    public Task<UserEmotesResponse> GetUserEmotesAsync(GetUserEmotesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        if (request.BroadcasterId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        if (request.After is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.After);
        return _transport.SendAsync(HttpMethod.Get, "chat/emotes/user", HelixJsonContext.Default.UserEmotesResponse,
            new HelixQuery().AddValue("user_id", request.UserId).AddValue("after", request.After).AddValue("broadcaster_id", request.BroadcasterId),
            authorization: new([TwitchScopes.UserReadEmotes], requiredUserId: request.UserId), cancellationToken: cancellationToken);
    }

    /// <summary>Follows cursors. The URL template is only available on pages from GetUserEmotesAsync.</summary>
    public IAsyncEnumerable<UserEmote> EnumerateUserEmotesAsync(GetUserEmotesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return HelixPagination.EnumerateAsync(async (cursor, ct) =>
        {
            var page = await GetUserEmotesAsync(request with { After = cursor ?? request.After }, ct).ConfigureAwait(false);
            return new HelixPage<UserEmote> { Data = page.Data, Pagination = page.Pagination };
        }, cancellationToken);
    }

    /// <summary>Sorted by set ID and then version ID. Empty when the broadcaster has no custom badges.</summary>
    public Task<HelixPage<ChatBadgeSet>> GetChannelChatBadgesAsync(string broadcasterId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        return _transport.SendAsync(HttpMethod.Get, "chat/badges", HelixJsonContext.Default.HelixPageChatBadgeSet,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId), authorization: CatalogAnyToken, cancellationToken: cancellationToken);
    }

    public Task<HelixPage<ChatBadgeSet>> GetGlobalChatBadgesAsync(CancellationToken cancellationToken = default)
        => _transport.SendAsync(HttpMethod.Get, "chat/badges/global", HelixJsonContext.Default.HelixPageChatBadgeSet,
            authorization: CatalogAnyToken, cancellationToken: cancellationToken);
}
