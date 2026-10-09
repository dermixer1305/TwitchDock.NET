using TwitchDock.Core;
using TwitchDock.Helix.Models;

namespace TwitchDock.Helix.Clients;

public sealed class SubscriptionsClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public Task<BroadcasterSubscriptionsResponse> GetBroadcasterSubscriptionsAsync(GetBroadcasterSubscriptionsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        ArgumentNullException.ThrowIfNull(request.UserIds);
        if (request.UserIds.Count > 0 && (request.After is not null || request.Before is not null))
            throw new ArgumentException("Subscriber IDs cannot be combined with pagination cursors.", nameof(request));
        return _transport.SendAsync(HttpMethod.Get, "subscriptions", HelixJsonContext.Default.BroadcasterSubscriptionsResponse,
            new HelixQuery().AddValue("broadcaster_id", request.BroadcasterId).AddValues("user_id", request.UserIds).AddPage(request.First, request.After, request.Before),
            // Extension app tokens are supported when the broadcaster granted access through the Extensions manager.
            authorization: new([TwitchScopes.ChannelReadSubscriptions], allowAppToken: true, requiredUserId: request.BroadcasterId), cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<BroadcasterSubscription> EnumerateBroadcasterSubscriptionsAsync(GetBroadcasterSubscriptionsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.UserIds);
        if (request.Before is not null) throw new ArgumentException("Forward enumeration cannot use before.", nameof(request));
        var snapshot = request with { UserIds = request.UserIds.ToArray() };
        return HelixPagination.EnumerateAsync(async (cursor, ct) =>
        {
            var page = await GetBroadcasterSubscriptionsAsync(snapshot with { After = cursor ?? snapshot.After }, ct).ConfigureAwait(false);
            return new HelixPage<BroadcasterSubscription> { Data = page.Data, Total = page.Total, Pagination = snapshot.UserIds.Count == 0 ? page.Pagination : null };
        }, cancellationToken);
    }

    /// <summary>HTTP 404 (not subscribed) is exposed as TwitchApiException with StatusCode NotFound.</summary>
    public Task<HelixPage<UserSubscription>> CheckUserSubscriptionAsync(CheckUserSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        return _transport.SendAsync(HttpMethod.Get, "subscriptions/user", HelixJsonContext.Default.HelixPageUserSubscription,
            new HelixQuery().AddValue("broadcaster_id", request.BroadcasterId).AddValue("user_id", request.UserId),
            authorization: new([TwitchScopes.UserReadSubscriptions], requiredUserId: request.UserId), cancellationToken: cancellationToken);
    }
}
