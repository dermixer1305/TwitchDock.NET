using System.Text.Json;
using TwitchDock.Core;
using TwitchDock.Helix.Models;

namespace TwitchDock.Helix.Clients;

/// <summary>App-owned EventSub conduits. Every operation requires an app access token.</summary>
public sealed class ConduitsClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    private static readonly TwitchAuthorizationRequirement AppOnly = new([], allowAppToken: true, allowUserToken: false);

    public Task<HelixPage<Conduit>> GetConduitsAsync(CancellationToken cancellationToken = default)
        => _transport.SendAsync(HttpMethod.Get, "eventsub/conduits", HelixJsonContext.Default.HelixPageConduit,
            authorization: AppOnly, cancellationToken: cancellationToken);

    public Task<HelixPage<Conduit>> CreateConduitAsync(CreateConduitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.ShardCount);
        return _transport.SendAsync(HttpMethod.Post, "eventsub/conduits", HelixJsonContext.Default.HelixPageConduit,
            jsonBody: JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.CreateConduitRequest),
            authorization: AppOnly, cancellationToken: cancellationToken);
    }

    /// <summary>Reducing the count disables and removes shards whose IDs are outside the new range.</summary>
    public Task<HelixPage<Conduit>> UpdateConduitAsync(UpdateConduitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Id);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.ShardCount);
        return _transport.SendAsync(HttpMethod.Patch, "eventsub/conduits", HelixJsonContext.Default.HelixPageConduit,
            jsonBody: JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.UpdateConduitRequest),
            authorization: AppOnly, cancellationToken: cancellationToken);
    }

    public Task DeleteConduitAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _transport.SendAsync(HttpMethod.Delete, "eventsub/conduits", new HelixQuery().AddValue("id", id),
            authorization: AppOnly, cancellationToken: cancellationToken);
    }

    public Task<HelixPage<ConduitShard>> GetConduitShardsAsync(GetConduitShardsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConduitId);
        return _transport.SendAsync(HttpMethod.Get, "eventsub/conduits/shards", HelixJsonContext.Default.HelixPageConduitShard,
            new HelixQuery().AddValue("conduit_id", request.ConduitId).AddValue("status", request.Status).AddValue("after", request.After),
            authorization: AppOnly, cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<ConduitShard> EnumerateConduitShardsAsync(GetConduitShardsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return HelixPagination.EnumerateAsync((cursor, ct) => GetConduitShardsAsync(request with { After = cursor ?? request.After }, ct), cancellationToken);
    }

    /// <summary>Updates up to 100 shards. HTTP 202 is not proof that every shard succeeded; inspect Errors.</summary>
    public Task<UpdateConduitShardsResponse> UpdateConduitShardsAsync(UpdateConduitShardsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConduitId);
        ArgumentNullException.ThrowIfNull(request.Shards);
        if (request.Shards.Count is < 1 or > 100) throw new ArgumentException("Provide between 1 and 100 shard updates.", nameof(request));
        foreach (var shard in request.Shards)
        {
            ArgumentNullException.ThrowIfNull(shard);
            ArgumentException.ThrowIfNullOrWhiteSpace(shard.Id);
            ArgumentNullException.ThrowIfNull(shard.Transport);
            ValidateTransport(shard.Transport);
        }
        return _transport.SendAsync(HttpMethod.Patch, "eventsub/conduits/shards", HelixJsonContext.Default.UpdateConduitShardsResponse,
            jsonBody: JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.UpdateConduitShardsRequest),
            authorization: AppOnly, cancellationToken: cancellationToken);
    }

    private static void ValidateTransport(ConduitShardTransportRequest transport)
    {
        if (transport.Method is not (null or "webhook" or "websocket")) throw new ArgumentException("Shard transport must be webhook or websocket.", nameof(transport));
        if (transport.Callback is not null && (!Uri.TryCreate(transport.Callback, UriKind.Absolute, out var callback) || callback.Scheme != "https" || callback.Port != 443))
            throw new ArgumentException("Webhook callback must use HTTPS port 443.", nameof(transport));
        if (transport.Secret is not null && (transport.Secret.Length is < 10 or > 100 || transport.Secret.Any(c => c > 127)))
            throw new ArgumentException("Webhook secret must contain 10 to 100 ASCII characters.", nameof(transport));
        if (transport.Method == "webhook")
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(transport.Callback);
            ArgumentException.ThrowIfNullOrWhiteSpace(transport.Secret);
            if (transport.SessionId is not null) throw new ArgumentException("Webhook transport cannot specify session_id.", nameof(transport));
        }
        if (transport.Method == "websocket")
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(transport.SessionId);
            if (transport.Callback is not null || transport.Secret is not null) throw new ArgumentException("WebSocket transport cannot specify callback or secret.", nameof(transport));
        }
    }
}
