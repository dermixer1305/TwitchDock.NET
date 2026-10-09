using System.Text.Json;
using TwitchSdk.Core;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Helix.Clients;

public sealed class PollsClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public Task<HelixPage<TwitchPoll>> GetPollsAsync(GetPollsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        if (request.First is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(request), "Poll page size must be between 1 and 20.");
        return _transport.SendAsync(HttpMethod.Get, "polls", HelixJsonContext.Default.HelixPageTwitchPoll,
            new HelixQuery().AddValue("broadcaster_id", request.BroadcasterId).AddValues("id", request.Ids, 20).AddPage(request.First, request.After),
            authorization: new([], requiredUserId: request.BroadcasterId, anyUserScopes: [TwitchScopes.ChannelReadPolls, TwitchScopes.ChannelManagePolls]), cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<TwitchPoll> EnumeratePollsAsync(GetPollsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Ids);
        var snapshot = request with { Ids = request.Ids.ToArray() };
        return HelixPagination.EnumerateAsync((cursor, ct) => GetPollsAsync(snapshot with { After = cursor ?? snapshot.After }, ct), cancellationToken);
    }

    public Task<HelixPage<TwitchPoll>> CreatePollAsync(CreatePollRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        HelixValidation.Text(request.Title, 60, nameof(request.Title));
        ArgumentNullException.ThrowIfNull(request.Choices);
        if (request.Choices.Count is < 2 or > 5) throw new ArgumentException("A poll needs 2 to 5 choices.", nameof(request));
        foreach (var choice in request.Choices) { ArgumentNullException.ThrowIfNull(choice); HelixValidation.Text(choice.Title, 25, nameof(request.Choices)); }
        if (request.Duration is < 15 or > 1800) throw new ArgumentOutOfRangeException(nameof(request), "Poll duration must be between 15 and 1800 seconds.");
        if (request.ChannelPointsVotingEnabled == true && request.ChannelPointsPerVote is not (>= 1 and <= 1000000)) throw new ArgumentOutOfRangeException(nameof(request), "Enabled extra votes cost between 1 and 1000000 points.");
        if (request.ChannelPointsPerVote.HasValue && request.ChannelPointsVotingEnabled != true) throw new ArgumentException("ChannelPointsPerVote requires enabled Channel Points voting.", nameof(request));
        return _transport.SendAsync(HttpMethod.Post, "polls", HelixJsonContext.Default.HelixPageTwitchPoll,
            jsonBody: JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.CreatePollRequest),
            authorization: new([TwitchScopes.ChannelManagePolls], requiredUserId: request.BroadcasterId), cancellationToken: cancellationToken);
    }

    public Task<HelixPage<TwitchPoll>> EndPollAsync(EndPollRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Id);
        if (request.Status is not "TERMINATED" and not "ARCHIVED") throw new ArgumentException("Poll status must be TERMINATED or ARCHIVED.", nameof(request));
        return _transport.SendAsync(HttpMethod.Patch, "polls", HelixJsonContext.Default.HelixPageTwitchPoll,
            jsonBody: JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.EndPollRequest),
            authorization: new([TwitchScopes.ChannelManagePolls], requiredUserId: request.BroadcasterId), cancellationToken: cancellationToken);
    }
}
