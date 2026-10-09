using System.Globalization;
using TwitchDock.Core;
using TwitchDock.Helix.Models;

namespace TwitchDock.Helix.Clients;

public sealed class AnalyticsClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    /// <summary>Requires analytics:read:extensions on a user token; reports are limited to extensions owned by that user.</summary>
    public Task<HelixPage<ExtensionAnalyticsReport>> GetExtensionAnalyticsAsync(GetExtensionAnalyticsRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new();
        var query = BuildQuery(request.Type, request.StartedAt, request.EndedAt, request.First, request.After).AddValue("extension_id", request.ExtensionId);
        return _transport.SendAsync(HttpMethod.Get, "analytics/extensions", HelixJsonContext.Default.HelixPageExtensionAnalyticsReport, query,
            authorization: new(["analytics:read:extensions"]), cancellationToken: cancellationToken);
    }

    /// <summary>Requires analytics:read:games on a user token; reports are limited to that user's games.</summary>
    public Task<HelixPage<GameAnalyticsReport>> GetGameAnalyticsAsync(GetGameAnalyticsRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new();
        var query = BuildQuery(request.Type, request.StartedAt, request.EndedAt, request.First, request.After).AddValue("game_id", request.GameId);
        return _transport.SendAsync(HttpMethod.Get, "analytics/games", HelixJsonContext.Default.HelixPageGameAnalyticsReport, query,
            authorization: new(["analytics:read:games"]), cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<ExtensionAnalyticsReport> EnumerateExtensionAnalyticsAsync(GetExtensionAnalyticsRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new();
        return HelixPagination.EnumerateAsync(async (cursor, ct) =>
        {
            var page = await GetExtensionAnalyticsAsync(request with { After = cursor ?? request.After }, ct).ConfigureAwait(false);
            // Twitch ignores after when an extension ID is specified.
            return request.ExtensionId is null ? page : new HelixPage<ExtensionAnalyticsReport> { Data = page.Data };
        }, cancellationToken);
    }

    public IAsyncEnumerable<GameAnalyticsReport> EnumerateGameAnalyticsAsync(GetGameAnalyticsRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new();
        return HelixPagination.EnumerateAsync(async (cursor, ct) =>
        {
            var page = await GetGameAnalyticsAsync(request with { After = cursor ?? request.After }, ct).ConfigureAwait(false);
            return request.GameId is null ? page : new HelixPage<GameAnalyticsReport> { Data = page.Data };
        }, cancellationToken);
    }

    private static HelixQuery BuildQuery(string? type, DateOnly? startedAt, DateOnly? endedAt, int? first, string? after)
    {
        if (type is not null and not "overview_v2") throw new ArgumentException("The supported analytics report type is overview_v2.", nameof(type));
        if (startedAt.HasValue != endedAt.HasValue) throw new ArgumentException("Analytics start and end dates must be supplied together.");
        if (startedAt > endedAt) throw new ArgumentException("The analytics end date must not precede its start date.");
        return new HelixQuery().AddValue("type", type)
            .AddValue("started_at", startedAt.HasValue ? startedAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00Z" : null)
            .AddValue("ended_at", endedAt.HasValue ? endedAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00Z" : null)
            .AddPage(first, after);
    }
}
