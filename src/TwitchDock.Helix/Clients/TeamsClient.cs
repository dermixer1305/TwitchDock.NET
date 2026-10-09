using TwitchDock.Core;
using TwitchDock.Helix.Models;

namespace TwitchDock.Helix.Clients;

/// <summary>Queries teams using an app or user token without additional scopes.</summary>
public sealed class TeamsClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    public Task<HelixPage<ChannelTeam>> GetChannelTeamsAsync(string broadcasterId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        return _transport.SendAsync(HttpMethod.Get, "teams/channel", HelixJsonContext.Default.HelixPageChannelTeam,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId), cancellationToken: cancellationToken);
    }

    public Task<HelixPage<TwitchTeam>> GetTeamsAsync(GetTeamsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((request.Name is null) == (request.Id is null)) throw new ArgumentException("Specify exactly one of team name or ID.", nameof(request));
        if (request.Name is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        if (request.Id is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.Id);
        return _transport.SendAsync(HttpMethod.Get, "teams", HelixJsonContext.Default.HelixPageTwitchTeam,
            new HelixQuery().AddValue("name", request.Name).AddValue("id", request.Id), cancellationToken: cancellationToken);
    }
}
