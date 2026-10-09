using System.Text;
using System.Text.Json;
using TwitchSdk.Core;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Helix.Clients;

public sealed class ChannelPointsClient(TwitchHttpClient transport)
{
    private const string RewardsPath = "channel_points/custom_rewards";
    private const string RedemptionsPath = "channel_points/custom_rewards/redemptions";
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public Task<HelixPage<CustomReward>> CreateCustomRewardAsync(string broadcasterId, CreateCustomRewardRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Title);
        ValidateOptions(request, request.Title, request.Cost, creating: true);
        return _transport.SendAsync(HttpMethod.Post, RewardsPath, HelixJsonContext.Default.HelixPageCustomReward,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId),
            JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.CreateCustomRewardRequest),
            authorization: Manage(broadcasterId), cancellationToken: cancellationToken);
    }

    /// <summary>Only the creating app may delete the reward. Twitch fulfills its outstanding unfulfilled redemptions.</summary>
    public Task DeleteCustomRewardAsync(string broadcasterId, string rewardId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rewardId);
        return _transport.SendAsync(HttpMethod.Delete, RewardsPath,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId).AddValue("id", rewardId),
            authorization: Manage(broadcasterId), cancellationToken: cancellationToken);
    }

    public Task<HelixPage<CustomReward>> GetCustomRewardsAsync(GetCustomRewardsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        return _transport.SendAsync(HttpMethod.Get, RewardsPath, HelixJsonContext.Default.HelixPageCustomReward,
            new HelixQuery().AddValue("broadcaster_id", request.BroadcasterId).AddValues("id", request.Ids, 50).AddValue("only_manageable_rewards", request.OnlyManageableRewards),
            authorization: Read(request.BroadcasterId), cancellationToken: cancellationToken);
    }

    public Task<HelixPage<CustomReward>> UpdateCustomRewardAsync(string broadcasterId, string rewardId, UpdateCustomRewardRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rewardId);
        ArgumentNullException.ThrowIfNull(request);
        ValidateOptions(request, request.Title, request.Cost, creating: false);
        return _transport.SendAsync(HttpMethod.Patch, RewardsPath, HelixJsonContext.Default.HelixPageCustomReward,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId).AddValue("id", rewardId),
            JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.UpdateCustomRewardRequest),
            authorization: Manage(broadcasterId), cancellationToken: cancellationToken);
    }

    public Task<HelixPage<CustomRewardRedemption>> GetCustomRewardRedemptionsAsync(GetCustomRewardRedemptionsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RewardId);
        ArgumentNullException.ThrowIfNull(request.Ids);
        if (request.Ids.Count == 0 && request.Status is null) throw new ArgumentException("Status is required when no redemption IDs are supplied.", nameof(request));
        if (request.Status is not null and not "CANCELED" and not "FULFILLED" and not "UNFULFILLED") throw new ArgumentException("Unknown redemption status.", nameof(request));
        if (request.Sort is not null and not "OLDEST" and not "NEWEST") throw new ArgumentException("Sort must be OLDEST or NEWEST.", nameof(request));
        if (request.First is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(request), "Page size must be between 1 and 50.");
        return _transport.SendAsync(HttpMethod.Get, RedemptionsPath, HelixJsonContext.Default.HelixPageCustomRewardRedemption,
            new HelixQuery().AddValue("broadcaster_id", request.BroadcasterId).AddValue("reward_id", request.RewardId).AddValue("status", request.Status)
                .AddValues("id", request.Ids, 50).AddValue("sort", request.Sort).AddPage(request.First, request.After),
            authorization: Read(request.BroadcasterId), cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<CustomRewardRedemption> EnumerateCustomRewardRedemptionsAsync(GetCustomRewardRedemptionsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Ids);
        var snapshot = request with { Ids = request.Ids.ToArray() };
        return HelixPagination.EnumerateAsync((cursor, ct) => GetCustomRewardRedemptionsAsync(snapshot with { After = cursor ?? snapshot.After }, ct), cancellationToken);
    }

    public Task<HelixPage<CustomRewardRedemption>> UpdateRedemptionStatusAsync(UpdateRedemptionStatusRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RewardId);
        ArgumentNullException.ThrowIfNull(request.Ids);
        if (request.Ids.Count == 0) throw new ArgumentException("At least one redemption ID is required.", nameof(request));
        if (request.Status is not "CANCELED" and not "FULFILLED") throw new ArgumentException("Updated status must be CANCELED or FULFILLED.", nameof(request));
        return _transport.SendAsync(HttpMethod.Patch, RedemptionsPath, HelixJsonContext.Default.HelixPageCustomRewardRedemption,
            new HelixQuery().AddValue("broadcaster_id", request.BroadcasterId).AddValue("reward_id", request.RewardId).AddValues("id", request.Ids, 50),
            JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.UpdateRedemptionStatusRequest),
            authorization: Manage(request.BroadcasterId), cancellationToken: cancellationToken);
    }

    private static TwitchAuthorizationRequirement Manage(string broadcasterId) => new([TwitchScopes.ChannelManageRedemptions], requiredUserId: broadcasterId);
    private static TwitchAuthorizationRequirement Read(string broadcasterId) => new([], requiredUserId: broadcasterId,
        anyUserScopes: [TwitchScopes.ChannelReadRedemptions, TwitchScopes.ChannelManageRedemptions]);

    private static void ValidateOptions(CustomRewardOptions options, string? title, long? cost, bool creating)
    {
        if (title is not null && (title.Length == 0 || title.EnumerateRunes().Count() > 45)) throw new ArgumentException("Reward title must contain between 1 and 45 Unicode code points.", nameof(title));
        if (cost is < 1) throw new ArgumentOutOfRangeException(nameof(cost), "Cost must be at least 1 point.");
        if (options.Prompt?.EnumerateRunes().Count() > 200) throw new ArgumentException("Reward prompt may contain at most 200 Unicode code points.", nameof(options));
        if (options.BackgroundColor is { } color && (color.Length != 7 || color[0] != '#' || !color.AsSpan(1).ToArray().All(Uri.IsHexDigit)))
            throw new ArgumentException("Background color must have the form #RRGGBB.", nameof(options));
        ValidateLimit(options.IsMaxPerStreamEnabled, options.MaxPerStream, creating, "MaxPerStream");
        ValidateLimit(options.IsMaxPerUserPerStreamEnabled, options.MaxPerUserPerStream, creating, "MaxPerUserPerStream");
        ValidateLimit(options.IsGlobalCooldownEnabled, options.GlobalCooldownSeconds, creating, "GlobalCooldownSeconds");
        if (!creating && options.IsGlobalCooldownEnabled == true && options.GlobalCooldownSeconds > 604800)
            throw new ArgumentOutOfRangeException(nameof(options), "Updated cooldown may not exceed 604800 seconds.");
    }

    private static void ValidateLimit(bool? enabled, long? value, bool creating, string name)
    {
        // A PATCH may enable an already configured limit without resending its value.
        if (enabled == true && (value is < 1 || (creating && value is null)))
            throw new ArgumentOutOfRangeException(name, "An enabled limit must be at least 1.");
    }
}
