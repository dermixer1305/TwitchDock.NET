using System.Text.Json;
using TwitchSdk.Core;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Helix.Clients;

public sealed partial class ModerationClient
{
    /// <summary>Channels the user moderates. Accepts the user's token or an app token authorized by that user.</summary>
    public Task<HelixPage<ModeratedChannel>> GetModeratedChannelsAsync(GetModeratedChannelsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        return _transport.SendAsync(HttpMethod.Get, "moderation/channels", HelixJsonContext.Default.HelixPageModeratedChannel,
            new HelixQuery().AddValue("user_id", request.UserId).AddPage(request.First, request.After),
            authorization: new([TwitchScopes.UserReadModeratedChannels], allowAppToken: true, requiredUserId: request.UserId), cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<ModeratedChannel> EnumerateModeratedChannelsAsync(GetModeratedChannelsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return HelixPagination.EnumerateAsync((cursor, ct) => GetModeratedChannelsAsync(request with { After = cursor ?? request.After }, ct), cancellationToken);
    }

    /// <summary>Broadcaster token with moderation:read or channel:manage:moderators.</summary>
    public Task<HelixPage<ChannelModerator>> GetModeratorsAsync(GetModeratorsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        return _transport.SendAsync(HttpMethod.Get, RoleEndpoints.Moderators, HelixJsonContext.Default.HelixPageChannelModerator,
            new HelixQuery().AddValue("broadcaster_id", request.BroadcasterId).AddValues("user_id", request.UserIds).AddPage(request.First, request.After),
            authorization: new([], requiredUserId: request.BroadcasterId, anyUserScopes: [TwitchScopes.ModerationRead, TwitchScopes.ChannelManageModerators]),
            cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<ChannelModerator> EnumerateModeratorsAsync(GetModeratorsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.UserIds);
        var snapshot = request with { UserIds = request.UserIds.ToArray() };
        return HelixPagination.EnumerateAsync((cursor, ct) => GetModeratorsAsync(snapshot with { After = cursor ?? snapshot.After }, ct), cancellationToken);
    }

    /// <summary>Broadcaster token with channel:manage:moderators. Twitch allows 10 additions per 10 seconds; VIPs must be removed as VIP first (HTTP 422).</summary>
    public Task AddChannelModeratorAsync(string broadcasterId, string userId, CancellationToken cancellationToken = default)
        => SendRoleChangeAsync(HttpMethod.Post, RoleEndpoints.Moderators, broadcasterId, userId, RoleEndpoints.ManageModerators(broadcasterId), cancellationToken);

    /// <summary>Broadcaster token with channel:manage:moderators. Twitch allows 10 removals per 10 seconds.</summary>
    public Task RemoveChannelModeratorAsync(string broadcasterId, string userId, CancellationToken cancellationToken = default)
        => SendRoleChangeAsync(HttpMethod.Delete, RoleEndpoints.Moderators, broadcasterId, userId, RoleEndpoints.ManageModerators(broadcasterId), cancellationToken);

    /// <summary>Broadcaster token with channel:read:vips or channel:manage:vips.</summary>
    public Task<HelixPage<ChannelVip>> GetVipsAsync(GetVipsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        return _transport.SendAsync(HttpMethod.Get, RoleEndpoints.Vips, HelixJsonContext.Default.HelixPageChannelVip,
            new HelixQuery().AddValue("broadcaster_id", request.BroadcasterId).AddValues("user_id", request.UserIds).AddPage(request.First, request.After),
            authorization: new([], requiredUserId: request.BroadcasterId, anyUserScopes: [TwitchScopes.ChannelReadVips, TwitchScopes.ChannelManageVips]),
            cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<ChannelVip> EnumerateVipsAsync(GetVipsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.UserIds);
        var snapshot = request with { UserIds = request.UserIds.ToArray() };
        return HelixPagination.EnumerateAsync((cursor, ct) => GetVipsAsync(snapshot with { After = cursor ?? snapshot.After }, ct), cancellationToken);
    }

    /// <summary>Broadcaster token with channel:manage:vips. Twitch allows 10 additions per 10 seconds and reports missing VIP slots as HTTP 409.</summary>
    public Task AddChannelVipAsync(string broadcasterId, string userId, CancellationToken cancellationToken = default)
        => SendRoleChangeAsync(HttpMethod.Post, RoleEndpoints.Vips, broadcasterId, userId, new([TwitchScopes.ChannelManageVips], requiredUserId: broadcasterId), cancellationToken);

    /// <summary>
    /// Requires channel:manage:vips from the broadcaster, or from the VIP removing their own status. Because either user may call it,
    /// only the scope is checked locally. Twitch allows 10 removals per 10 seconds.
    /// </summary>
    public Task RemoveChannelVipAsync(string broadcasterId, string userId, CancellationToken cancellationToken = default)
        => SendRoleChangeAsync(HttpMethod.Delete, RoleEndpoints.Vips, broadcasterId, userId, new([TwitchScopes.ChannelManageVips]), cancellationToken);

    /// <summary>The moderator ID must match the user token, or an app token authorized by that moderator.</summary>
    public Task<HelixPage<ShieldModeStatus>> UpdateShieldModeStatusAsync(string broadcasterId, string moderatorId, bool isActive, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moderatorId);
        return _transport.SendAsync(HttpMethod.Put, RoleEndpoints.ShieldMode, HelixJsonContext.Default.HelixPageShieldModeStatus, RoleEndpoints.ModeratorQuery(broadcasterId, moderatorId),
            JsonSerializer.SerializeToUtf8Bytes(new UpdateShieldModeStatusRequest { IsActive = isActive }, HelixJsonContext.Default.UpdateShieldModeStatusRequest),
            authorization: new([TwitchScopes.ModeratorManageShieldMode], allowAppToken: true, requiredUserId: moderatorId), cancellationToken: cancellationToken);
    }

    /// <summary>Accepts moderator:read:shield_mode or moderator:manage:shield_mode. Moderator fields are empty and LastActivatedAt is null if never activated.</summary>
    public Task<HelixPage<ShieldModeStatus>> GetShieldModeStatusAsync(string broadcasterId, string moderatorId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moderatorId);
        return _transport.SendAsync(HttpMethod.Get, RoleEndpoints.ShieldMode, HelixJsonContext.Default.HelixPageShieldModeStatus, RoleEndpoints.ModeratorQuery(broadcasterId, moderatorId),
            authorization: new([], allowAppToken: true, requiredUserId: moderatorId, anyUserScopes: [TwitchScopes.ModeratorReadShieldMode, TwitchScopes.ModeratorManageShieldMode]),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// A new warning replaces an existing one. The moderator ID must match the user token, or an app token authorized by that moderator.
    /// HTTP 409 means another warning-state update is in progress and the call may be retried.
    /// </summary>
    public Task<HelixPage<ChatUserWarning>> WarnChatUserAsync(string broadcasterId, string moderatorId, WarnChatUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moderatorId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        HelixValidation.Text(request.Reason, 500, nameof(request.Reason));
        return _transport.SendAsync(HttpMethod.Post, "moderation/warnings", HelixJsonContext.Default.HelixPageChatUserWarning, RoleEndpoints.ModeratorQuery(broadcasterId, moderatorId),
            JsonSerializer.SerializeToUtf8Bytes(new WarnChatUserBody { Data = request }, HelixJsonContext.Default.WarnChatUserBody),
            authorization: new([TwitchScopes.ModeratorManageWarnings], allowAppToken: true, requiredUserId: moderatorId), cancellationToken: cancellationToken);
    }

    /// <summary>Accepts a user or app token with moderator:manage:suspicious_users. Status must be ACTIVE_MONITORING or RESTRICTED.</summary>
    public Task<HelixPage<SuspiciousChatUserStatus>> AddSuspiciousStatusToChatUserAsync(string broadcasterId, string moderatorId, AddSuspiciousStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moderatorId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        if (request.Status is not "ACTIVE_MONITORING" and not "RESTRICTED") throw new ArgumentException("Status must be ACTIVE_MONITORING or RESTRICTED.", nameof(request));
        return _transport.SendAsync(HttpMethod.Post, RoleEndpoints.SuspiciousUsers, HelixJsonContext.Default.HelixPageSuspiciousChatUserStatus, RoleEndpoints.ModeratorQuery(broadcasterId, moderatorId),
            JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.AddSuspiciousStatusRequest),
            authorization: RoleEndpoints.ManageSuspiciousUsers, cancellationToken: cancellationToken);
    }

    /// <summary>Accepts a user or app token with moderator:manage:suspicious_users. The returned status is NO_TREATMENT.</summary>
    public Task<HelixPage<SuspiciousChatUserStatus>> RemoveSuspiciousStatusFromChatUserAsync(string broadcasterId, string moderatorId, string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moderatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return _transport.SendAsync(HttpMethod.Delete, RoleEndpoints.SuspiciousUsers, HelixJsonContext.Default.HelixPageSuspiciousChatUserStatus,
            RoleEndpoints.ModeratorQuery(broadcasterId, moderatorId).AddValue("user_id", userId), authorization: RoleEndpoints.ManageSuspiciousUsers, cancellationToken: cancellationToken);
    }

    // Private helpers live in a nested type so they cannot collide with members of other ModerationClient partial files.
    private static class RoleEndpoints
    {
        public const string Moderators = "moderation/moderators";
        public const string Vips = "channels/vips";
        public const string ShieldMode = "moderation/shield_mode";
        public const string SuspiciousUsers = "moderation/suspicious_users";

        // The reference does not require moderator_id to match the token user here, so identity is left to Twitch.
        public static readonly TwitchAuthorizationRequirement ManageSuspiciousUsers = new([TwitchScopes.ModeratorManageSuspiciousUsers], allowAppToken: true);

        public static TwitchAuthorizationRequirement ManageModerators(string broadcasterId) => new([TwitchScopes.ChannelManageModerators], requiredUserId: broadcasterId);

        public static HelixQuery ModeratorQuery(string broadcasterId, string moderatorId) => new HelixQuery().AddValue("broadcaster_id", broadcasterId).AddValue("moderator_id", moderatorId);
    }

    private Task SendRoleChangeAsync(HttpMethod method, string path, string broadcasterId, string userId, TwitchAuthorizationRequirement authorization, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return _transport.SendAsync(method, path, new HelixQuery().AddValue("broadcaster_id", broadcasterId).AddValue("user_id", userId),
            authorization: authorization, cancellationToken: cancellationToken);
    }
}
