using TwitchDock.Core;
using TwitchDock.Helix.Models;

namespace TwitchDock.Helix.Clients;

/// <summary>Requires a user token with channel:manage:raids granted by the initiating broadcaster.</summary>
public sealed class RaidsClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    public Task<HelixPage<RaidResult>> StartRaidAsync(string fromBroadcasterId, string toBroadcasterId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromBroadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toBroadcasterId);
        if (fromBroadcasterId == toBroadcasterId) throw new ArgumentException("A broadcaster cannot raid their own channel.", nameof(toBroadcasterId));
        return _transport.SendAsync(HttpMethod.Post, "raids", HelixJsonContext.Default.HelixPageRaidResult,
            new HelixQuery().AddValue("from_broadcaster_id", fromBroadcasterId).AddValue("to_broadcaster_id", toBroadcasterId),
            authorization: new(["channel:manage:raids"], requiredUserId: fromBroadcasterId), cancellationToken: cancellationToken);
    }

    public Task CancelRaidAsync(string broadcasterId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        return _transport.SendAsync(HttpMethod.Delete, "raids", new HelixQuery().AddValue("broadcaster_id", broadcasterId),
            authorization: new(["channel:manage:raids"], requiredUserId: broadcasterId), cancellationToken: cancellationToken);
    }
}
