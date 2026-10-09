namespace TwitchSdk.Helix.Models;

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
    public string? Before { get; init; }
}

public sealed class TwitchGame
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string BoxArtUrl { get; init; }
    public required string IgdbId { get; init; }
}
