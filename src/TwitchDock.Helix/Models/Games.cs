namespace TwitchDock.Helix.Models;

public sealed record GetGamesRequest
{
    public IReadOnlyList<string> Ids { get; init => field = value ?? []; } = [];
    public IReadOnlyList<string> Names { get; init => field = value ?? []; } = [];
    public IReadOnlyList<string> IgdbIds { get; init => field = value ?? []; } = [];
}

public sealed record GetTopGamesRequest
{
    public int? First { get; init; }
    public string? After { get; init; }
    [Obsolete("Twitch no longer documents the before cursor for Get Top Games (removed from the reference in November 2026). Page forward with After. The SDK still forwards a value.")]
    public string? Before { get; init; }
}

public sealed class TwitchGame
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string BoxArtUrl { get; init; }
    public required string IgdbId { get; init; }
}
