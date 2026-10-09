using TwitchDock.Core;
using TwitchDock.Helix.Models;

namespace TwitchDock.Helix.Clients;

/// <summary>Requires a user token with channel:read:charity for the specified broadcaster's active campaign.</summary>
public sealed class CharityClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    public Task<HelixPage<CharityCampaign>> GetCharityCampaignAsync(string broadcasterId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        return _transport.SendAsync(HttpMethod.Get, "charity/campaigns", HelixJsonContext.Default.HelixPageCharityCampaign,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId), authorization: new(["channel:read:charity"], requiredUserId: broadcasterId), cancellationToken: cancellationToken);
    }

    public Task<HelixPage<CharityDonation>> GetCharityCampaignDonationsAsync(GetCharityDonationsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        return _transport.SendAsync(HttpMethod.Get, "charity/donations", HelixJsonContext.Default.HelixPageCharityDonation,
            new HelixQuery().AddValue("broadcaster_id", request.BroadcasterId).AddPage(request.First, request.After),
            authorization: new(["channel:read:charity"], requiredUserId: request.BroadcasterId), cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<CharityDonation> EnumerateDonationsAsync(GetCharityDonationsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return HelixPagination.EnumerateAsync((cursor, ct) => GetCharityCampaignDonationsAsync(request with { After = cursor ?? request.After }, ct), cancellationToken);
    }
}
