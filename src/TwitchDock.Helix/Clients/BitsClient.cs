using TwitchDock.Core;
using TwitchDock.Helix.Models;

namespace TwitchDock.Helix.Clients;

public sealed class BitsClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public Task<BitsLeaderboardResponse> GetBitsLeaderboardAsync(GetBitsLeaderboardRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new();
        if (request.Count is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(request), "Count must be between 1 and 100.");
        if (request.Period is not null and not "day" and not "week" and not "month" and not "year" and not "all") throw new ArgumentException("Unknown leaderboard period.", nameof(request));
        if (request.StartedAt.HasValue && request.Period is null) throw new ArgumentException("StartedAt requires an explicit period.", nameof(request));
        if (request.Period is not null and not "all" && !request.StartedAt.HasValue) throw new ArgumentException("A bounded leaderboard period requires StartedAt.", nameof(request));
        if (request.UserId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        return _transport.SendAsync(HttpMethod.Get, "bits/leaderboard", HelixJsonContext.Default.BitsLeaderboardResponse,
            new HelixQuery().AddValue("count", request.Count).AddValue("period", request.Period).AddValue("started_at", request.StartedAt).AddValue("user_id", request.UserId),
            authorization: new([TwitchScopes.BitsRead]), cancellationToken: cancellationToken);
    }

    public Task<HelixPage<Cheermote>> GetCheermotesAsync(string? broadcasterId = null, CancellationToken cancellationToken = default)
    {
        if (broadcasterId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        return _transport.SendAsync(HttpMethod.Get, "bits/cheermotes", HelixJsonContext.Default.HelixPageCheermote,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId), cancellationToken: cancellationToken);
    }

    /// <summary>Requires the broadcaster's user token with bits:read. If none of the requested IDs exist, Twitch returns 404.</summary>
    public Task<HelixPage<CustomPowerUp>> GetCustomPowerUpsAsync(GetCustomPowerUpsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        return _transport.SendAsync(HttpMethod.Get, "bits/custom_power_ups", HelixJsonContext.Default.HelixPageCustomPowerUp,
            new HelixQuery().AddValue("broadcaster_id", request.BroadcasterId).AddValues("id", request.Ids, 50),
            authorization: new([TwitchScopes.BitsRead], requiredUserId: request.BroadcasterId), cancellationToken: cancellationToken);
    }

    /// <summary>Requires the extension's app token. Twitch verifies that ExtensionId matches that token's client ID.</summary>
    public Task<HelixPage<ExtensionTransaction>> GetExtensionTransactionsAsync(GetExtensionTransactionsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExtensionId);
        return _transport.SendAsync(HttpMethod.Get, "extensions/transactions", HelixJsonContext.Default.HelixPageExtensionTransaction,
            new HelixQuery().AddValue("extension_id", request.ExtensionId).AddValues("id", request.Ids).AddPage(request.First, request.After),
            authorization: new([], allowAppToken: true, allowUserToken: false), cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<ExtensionTransaction> EnumerateExtensionTransactionsAsync(GetExtensionTransactionsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Ids);
        var snapshot = request with { Ids = request.Ids.ToArray() };
        return HelixPagination.EnumerateAsync((cursor, ct) => GetExtensionTransactionsAsync(snapshot with { After = cursor ?? snapshot.After }, ct), cancellationToken);
    }
}
