using TwitchDock.Core;
using TwitchDock.Helix.Models;

namespace TwitchDock.Helix.Clients;

public sealed class GoalsClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    /// <summary>Requires a user token with channel:read:goals and matching broadcaster ID.</summary>
    public Task<HelixPage<CreatorGoal>> GetCreatorGoalsAsync(string broadcasterId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        return _transport.SendAsync(HttpMethod.Get, "goals", HelixJsonContext.Default.HelixPageCreatorGoal,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId), authorization: new(["channel:read:goals"], requiredUserId: broadcasterId), cancellationToken: cancellationToken);
    }
}
