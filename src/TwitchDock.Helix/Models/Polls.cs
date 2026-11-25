namespace TwitchDock.Helix.Models;

public sealed record GetPollsRequest
{
    public required string BroadcasterId { get; init; }
    public IReadOnlyList<string> Ids { get; init => field = value ?? []; } = [];
    public int? First { get; init; }
    public string? After { get; init; }
}

public sealed class CreatePollRequest
{
    public required string BroadcasterId { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyList<PollChoiceRequest> Choices { get; init; }
    public required int Duration { get; init; }
    public bool? ChannelPointsVotingEnabled { get; init; }
    public int? ChannelPointsPerVote { get; init; }
}

public sealed class PollChoiceRequest
{
    public required string Title { get; init; }
}

public sealed class EndPollRequest
{
    public required string BroadcasterId { get; init; }
    public required string Id { get; init; }
    public required string Status { get; init; }
}

public sealed class TwitchPoll
{
    public required string Id { get; init; }
    public required string BroadcasterId { get; init; }
    public required string BroadcasterName { get; init; }
    public required string BroadcasterLogin { get; init; }
    public required string Title { get; init; }
    public IReadOnlyList<PollChoice> Choices { get; init => field = value ?? []; } = [];
    /// <summary>Unused by Twitch; always false.</summary>
    [Obsolete("Twitch removed bits_voting_enabled from the poll reference (November 2026); it was always false.")]
    public bool BitsVotingEnabled { get; init; }
    /// <summary>Unused by Twitch; always zero.</summary>
    [Obsolete("Twitch removed bits_per_vote from the poll reference (November 2026); it was always zero.")]
    public int BitsPerVote { get; init; }
    public bool ChannelPointsVotingEnabled { get; init; }
    public int ChannelPointsPerVote { get; init; }
    public required string Status { get; init; }
    public int Duration { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
}

public sealed class PollChoice
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public long Votes { get; init; }
    public long ChannelPointsVotes { get; init; }
    /// <summary>Unused by Twitch; always zero.</summary>
    [Obsolete("Twitch removed bits_votes from the poll reference (November 2026); it was always zero.")]
    public long BitsVotes { get; init; }
}
