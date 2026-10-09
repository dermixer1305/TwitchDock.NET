namespace TwitchDock.Helix.Models;

public sealed record GetCharityDonationsRequest
{
    public required string BroadcasterId { get; init; }
    public int? First { get; init; }
    public string? After { get; init; }
}

public sealed class CharityAmount
{
    /// <summary>Amount in minor currency units. Use DecimalPlaces to interpret it without floating-point rounding.</summary>
    public long Value { get; init; }
    public int DecimalPlaces { get; init; }
    public required string Currency { get; init; }
}

public sealed class CharityCampaign
{
    public required string Id { get; init; }
    public required string BroadcasterId { get; init; }
    public required string BroadcasterLogin { get; init; }
    public required string BroadcasterName { get; init; }
    public required string CharityName { get; init; }
    public required string CharityDescription { get; init; }
    public required string CharityLogo { get; init; }
    public required string CharityWebsite { get; init; }
    public required CharityAmount CurrentAmount { get; init; }
    public CharityAmount? TargetAmount { get; init; }
}

public sealed class CharityDonation
{
    public required string Id { get; init; }
    public required string CampaignId { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public required CharityAmount Amount { get; init; }
}
