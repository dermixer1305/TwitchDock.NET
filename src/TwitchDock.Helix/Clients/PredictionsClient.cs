using System.Text.Json;
using TwitchDock.Core;
using TwitchDock.Helix.Models;

namespace TwitchDock.Helix.Clients;

public sealed class PredictionsClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public Task<HelixPage<TwitchPrediction>> GetPredictionsAsync(GetPredictionsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        if (request.First is < 1 or > 25) throw new ArgumentOutOfRangeException(nameof(request), "Prediction page size must be between 1 and 25.");
        return _transport.SendAsync(HttpMethod.Get, "predictions", HelixJsonContext.Default.HelixPageTwitchPrediction,
            new HelixQuery().AddValue("broadcaster_id", request.BroadcasterId).AddValues("id", request.Ids, 25).AddPage(request.First, request.After),
            authorization: new([], requiredUserId: request.BroadcasterId, anyUserScopes: [TwitchScopes.ChannelReadPredictions, TwitchScopes.ChannelManagePredictions]), cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<TwitchPrediction> EnumeratePredictionsAsync(GetPredictionsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Ids);
        var snapshot = request with { Ids = request.Ids.ToArray() };
        return HelixPagination.EnumerateAsync((cursor, ct) => GetPredictionsAsync(snapshot with { After = cursor ?? snapshot.After }, ct), cancellationToken);
    }

    public Task<HelixPage<TwitchPrediction>> CreatePredictionAsync(CreatePredictionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        HelixValidation.Text(request.Title, 45, nameof(request.Title));
        ArgumentNullException.ThrowIfNull(request.Outcomes);
        if (request.Outcomes.Count is < 2 or > 10) throw new ArgumentException("A prediction needs 2 to 10 outcomes.", nameof(request));
        foreach (var outcome in request.Outcomes) { ArgumentNullException.ThrowIfNull(outcome); HelixValidation.Text(outcome.Title, 25, nameof(request.Outcomes)); }
        if (request.PredictionWindow is < 30 or > 1800) throw new ArgumentOutOfRangeException(nameof(request), "Prediction window must be between 30 and 1800 seconds.");
        return _transport.SendAsync(HttpMethod.Post, "predictions", HelixJsonContext.Default.HelixPageTwitchPrediction,
            jsonBody: JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.CreatePredictionRequest),
            authorization: new([TwitchScopes.ChannelManagePredictions], requiredUserId: request.BroadcasterId), cancellationToken: cancellationToken);
    }

    public Task<HelixPage<TwitchPrediction>> EndPredictionAsync(EndPredictionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Id);
        if (request.Status is not "LOCKED" and not "CANCELED" and not "RESOLVED") throw new ArgumentException("Prediction status must be LOCKED, CANCELED or RESOLVED.", nameof(request));
        if (request.Status == "RESOLVED" || request.WinningOutcomeId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.WinningOutcomeId);
        return _transport.SendAsync(HttpMethod.Patch, "predictions", HelixJsonContext.Default.HelixPageTwitchPrediction,
            jsonBody: JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.EndPredictionRequest),
            authorization: new([TwitchScopes.ChannelManagePredictions], requiredUserId: request.BroadcasterId), cancellationToken: cancellationToken);
    }
}
