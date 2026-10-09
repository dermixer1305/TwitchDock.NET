using TwitchDock.Core;
using TwitchDock.Helix.Models;

namespace TwitchDock.Helix.Clients;

/// <summary>Searches categories and channels using an app or user access token.</summary>
public sealed class SearchClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public Task<HelixPage<CategorySearchResult>> SearchCategoriesAsync(SearchCategoriesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);
        return _transport.SendAsync(HttpMethod.Get, "search/categories", HelixJsonContext.Default.HelixPageCategorySearchResult,
            new HelixQuery().AddValue("query", request.Query).AddPage(request.First, request.After), cancellationToken: cancellationToken);
    }

    public Task<HelixPage<ChannelSearchResult>> SearchChannelsAsync(SearchChannelsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);
        return _transport.SendAsync(HttpMethod.Get, "search/channels", HelixJsonContext.Default.HelixPageChannelSearchResult,
            new HelixQuery().AddValue("query", request.Query).AddValue("live_only", request.LiveOnly).AddPage(request.First, request.After), cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<CategorySearchResult> EnumerateCategoriesAsync(SearchCategoriesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return HelixPagination.EnumerateAsync((cursor, ct) => SearchCategoriesAsync(request with { After = cursor ?? request.After }, ct), cancellationToken);
    }

    public IAsyncEnumerable<ChannelSearchResult> EnumerateChannelsAsync(SearchChannelsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return HelixPagination.EnumerateAsync((cursor, ct) => SearchChannelsAsync(request with { After = cursor ?? request.After }, ct), cancellationToken);
    }
}
