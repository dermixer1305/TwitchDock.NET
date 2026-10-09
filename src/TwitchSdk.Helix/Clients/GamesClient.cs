using TwitchSdk.Core;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Helix.Clients;

/// <summary>Game/category queries using an app or user access token.</summary>
public sealed class GamesClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public Task<HelixPage<TwitchGame>> GetGamesAsync(GetGamesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Ids);
        ArgumentNullException.ThrowIfNull(request.Names);
        ArgumentNullException.ThrowIfNull(request.IgdbIds);
        var count = (long)request.Ids.Count + request.Names.Count + request.IgdbIds.Count;
        if (count is < 1 or > 100) throw new ArgumentException("Supply between 1 and 100 game IDs, names and IGDB IDs combined.", nameof(request));
        return _transport.SendAsync(HttpMethod.Get, "games", HelixJsonContext.Default.HelixPageTwitchGame,
            new HelixQuery().AddValues("id", request.Ids).AddValues("name", request.Names).AddValues("igdb_id", request.IgdbIds), cancellationToken: cancellationToken);
    }

    public Task<HelixPage<TwitchGame>> GetTopGamesAsync(GetTopGamesRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new();
        return _transport.SendAsync(HttpMethod.Get, "games/top", HelixJsonContext.Default.HelixPageTwitchGame,
            new HelixQuery().AddPage(request.First, request.After, request.Before), cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<TwitchGame> EnumerateTopGamesAsync(GetTopGamesRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new();
        if (request.Before is not null) throw new ArgumentException("Forward enumeration cannot use before.", nameof(request));
        return HelixPagination.EnumerateAsync((cursor, ct) => GetTopGamesAsync(request with { After = cursor ?? request.After }, ct), cancellationToken);
    }
}
