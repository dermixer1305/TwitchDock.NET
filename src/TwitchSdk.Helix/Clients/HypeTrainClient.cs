using TwitchSdk.Core;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Helix.Clients;

public sealed class HypeTrainClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public Task<HelixPage<HypeTrainStatus>> GetHypeTrainStatusAsync(string broadcasterId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        return _transport.SendAsync(HttpMethod.Get, "hypetrain/status", HelixJsonContext.Default.HelixPageHypeTrainStatus,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId),
            authorization: new([TwitchScopes.ChannelReadHypeTrain], requiredUserId: broadcasterId), cancellationToken: cancellationToken);
    }
}
