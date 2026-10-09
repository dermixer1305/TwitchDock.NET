using System.Text.Json;
using TwitchSdk.Core;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Helix.Clients;

public sealed class AdsClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    /// <summary>Requires channel:edit:commercial and a live partner/affiliate broadcaster's authorization.</summary>
    public Task<HelixPage<CommercialResult>> StartCommercialAsync(StartCommercialRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        if (request.Length < 1) throw new ArgumentOutOfRangeException(nameof(request), "Commercial length must be positive. Twitch caps lengths above 180 seconds.");
        return _transport.SendAsync(HttpMethod.Post, "channels/commercial", HelixJsonContext.Default.HelixPageCommercialResult,
            jsonBody: JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.StartCommercialRequest),
            authorization: new(["channel:edit:commercial"], true, request.BroadcasterId), cancellationToken: cancellationToken);
    }

    /// <summary>Requires channel:read:ads granted by the specified broadcaster.</summary>
    public Task<HelixPage<AdSchedule>> GetAdScheduleAsync(string broadcasterId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        return _transport.SendAsync(HttpMethod.Get, "channels/ads", HelixJsonContext.Default.HelixPageAdSchedule,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId), authorization: new(["channel:read:ads"], true, broadcasterId), cancellationToken: cancellationToken);
    }

    /// <summary>Requires channel:manage:ads granted by the specified broadcaster.</summary>
    public Task<HelixPage<AdSnoozeResult>> SnoozeNextAdAsync(string broadcasterId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        return _transport.SendAsync(HttpMethod.Post, "channels/ads/schedule/snooze", HelixJsonContext.Default.HelixPageAdSnoozeResult,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId), authorization: new(["channel:manage:ads"], true, broadcasterId), cancellationToken: cancellationToken);
    }
}
