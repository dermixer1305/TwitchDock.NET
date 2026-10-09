using System.Text.Json;
using TwitchDock.Core;
using TwitchDock.Helix.Models;

namespace TwitchDock.Helix.Clients;

/// <summary>
/// Drops entitlements. Both endpoints accept an app or user token whose client is owned by a member of the organization that owns the game;
/// Twitch verifies organization and game ownership.
/// </summary>
public sealed class EntitlementsClient(TwitchHttpClient transport)
{
    private const string DropsPath = "entitlements/drops";
    private const int MaximumIds = 100;
    private const int MaximumPageSize = 1000;
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    /// <summary>
    /// Gets entitlements, unsorted. App tokens may filter by UserId and/or GameId; user tokens see only their own entitlements and may not
    /// specify UserId (rejected locally when the token kind is known).
    /// </summary>
    public Task<HelixPage<DropsEntitlement>> GetDropsEntitlementsAsync(GetDropsEntitlementsRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new();
        ArgumentNullException.ThrowIfNull(request.Ids);
        if (request.UserId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        if (request.GameId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.GameId);
        ValidateStatus(request.FulfillmentStatus);
        if (request.First is < 1 or > MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(request), "Page size must be between 1 and 1000.");
        return _transport.SendAsync(HttpMethod.Get, DropsPath, HelixJsonContext.Default.HelixPageDropsEntitlement,
            new HelixQuery().AddValues("id", request.Ids, MaximumIds).AddValue("user_id", request.UserId).AddValue("game_id", request.GameId)
                .AddValue("fulfillment_status", request.FulfillmentStatus).AddValue("after", request.After).AddValue("first", request.First),
            authorization: new([], allowAppToken: true, allowUserToken: request.UserId is null), cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<DropsEntitlement> EnumerateDropsEntitlementsAsync(GetDropsEntitlementsRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new();
        ArgumentNullException.ThrowIfNull(request.Ids);
        var snapshot = request with { Ids = request.Ids.ToArray() };
        return HelixPagination.EnumerateAsync((cursor, ct) => GetDropsEntitlementsAsync(snapshot with { After = cursor ?? snapshot.After }, ct), cancellationToken);
    }

    /// <summary>
    /// Sets the fulfillment status. App tokens update matching entitlements owned by the organization; user tokens only the user's own.
    /// Check every returned group: UPDATE_FAILED is transient. Mutations are not retried after ambiguous failures.
    /// </summary>
    public Task<HelixPage<DropsEntitlementUpdate>> UpdateDropsEntitlementsAsync(UpdateDropsEntitlementsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.EntitlementIds is { } ids)
        {
            if (ids.Count is 0 or > MaximumIds) throw new ArgumentException("Specify 1 to 100 entitlement IDs, or omit the list.", nameof(request));
            foreach (var id in ids) ArgumentException.ThrowIfNullOrWhiteSpace(id);
        }
        ValidateStatus(request.FulfillmentStatus);
        return _transport.SendAsync(HttpMethod.Patch, DropsPath, HelixJsonContext.Default.HelixPageDropsEntitlementUpdate,
            jsonBody: JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.UpdateDropsEntitlementsRequest),
            authorization: new([], allowAppToken: true), cancellationToken: cancellationToken);
    }

    private static void ValidateStatus(string? status)
    {
        if (status is not null and not DropsFulfillmentStatuses.Claimed and not DropsFulfillmentStatuses.Fulfilled)
            throw new ArgumentException("Fulfillment status must be CLAIMED or FULFILLED.", nameof(status));
    }
}
