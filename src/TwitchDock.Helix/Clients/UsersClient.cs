using System.Globalization;
using System.Text;
using System.Text.Json;
using TwitchDock.Core;
using TwitchDock.Helix.Models;

namespace TwitchDock.Helix.Clients;

public sealed class UsersClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public Task<HelixPage<TwitchUser>> GetUsersAsync(GetUsersRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new();
        ArgumentNullException.ThrowIfNull(request.Ids);
        ArgumentNullException.ThrowIfNull(request.Logins);
        if (request.Ids.Count + request.Logins.Count > 100) throw new ArgumentException("At most 100 IDs and logins combined are allowed.", nameof(request));
        return _transport.SendAsync(HttpMethod.Get, "users", HelixJsonContext.Default.HelixPageTwitchUser,
            new HelixQuery().AddValues("id", request.Ids).AddValues("login", request.Logins),
            authorization: request.Ids.Count + request.Logins.Count == 0 ? new([]) : null, cancellationToken: cancellationToken);
    }

    public Task<HelixPage<TwitchUser>> UpdateUserAsync(UpdateUserRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new();
        if (request.Description?.EnumerateRunes().Count() > 300) throw new ArgumentException("Description may contain at most 300 Unicode code points.", nameof(request));
        return _transport.SendAsync(HttpMethod.Put, "users", HelixJsonContext.Default.HelixPageTwitchUser,
            new HelixQuery().AddValue("description", request.Description), authorization: new([TwitchScopes.UserEdit]), cancellationToken: cancellationToken);
    }

    public Task<HelixPage<BlockedUser>> GetUserBlockListAsync(GetUserBlockListRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        return _transport.SendAsync(HttpMethod.Get, "users/blocks", HelixJsonContext.Default.HelixPageBlockedUser,
            new HelixQuery().AddValue("broadcaster_id", request.BroadcasterId).AddPage(request.First, request.After),
            authorization: new([TwitchScopes.UserReadBlockedUsers], requiredUserId: request.BroadcasterId), cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<BlockedUser> EnumerateUserBlockListAsync(GetUserBlockListRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return HelixPagination.EnumerateAsync((cursor, ct) => GetUserBlockListAsync(request with { After = cursor ?? request.After }, ct), cancellationToken);
    }

    public Task BlockUserAsync(BlockUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetUserId);
        if (request.SourceContext is not null and not "chat" and not "whisper") throw new ArgumentException("Source context must be chat or whisper.", nameof(request));
        if (request.Reason is not null and not "harassment" and not "spam" and not "other") throw new ArgumentException("Unknown block reason.", nameof(request));
        return _transport.SendAsync(HttpMethod.Put, "users/blocks",
            new HelixQuery().AddValue("target_user_id", request.TargetUserId).AddValue("source_context", request.SourceContext).AddValue("reason", request.Reason),
            authorization: new([TwitchScopes.UserManageBlockedUsers]), cancellationToken: cancellationToken);
    }

    public Task UnblockUserAsync(string targetUserId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUserId);
        return _transport.SendAsync(HttpMethod.Delete, "users/blocks", new HelixQuery().AddValue("target_user_id", targetUserId),
            authorization: new([TwitchScopes.UserManageBlockedUsers]), cancellationToken: cancellationToken);
    }

    /// <summary>Requires an app token. Accepts 1 to 10 user IDs and reports the scopes each user granted to this client ID.</summary>
    public Task<HelixPage<UserAuthorization>> GetAuthorizationByUserAsync(IReadOnlyList<string> userIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count == 0) throw new ArgumentException("At least one user ID is required.", nameof(userIds));
        return _transport.SendAsync(HttpMethod.Get, "authorization/users", HelixJsonContext.Default.HelixPageUserAuthorization,
            new HelixQuery().AddValues("user_id", userIds, 10), authorization: new([], allowAppToken: true, allowUserToken: false), cancellationToken: cancellationToken);
    }

    /// <summary>user:read:broadcast or user:edit:broadcast; inactive extensions require the edit scope.</summary>
    public Task<HelixPage<InstalledUserExtension>> GetUserExtensionsAsync(CancellationToken cancellationToken = default)
        => _transport.SendAsync(HttpMethod.Get, "users/extensions/list", HelixJsonContext.Default.HelixPageInstalledUserExtension,
            authorization: new([], anyUserScopes: [TwitchScopes.UserReadBroadcast, TwitchScopes.UserEditBroadcast]), cancellationToken: cancellationToken);

    public Task<UserActiveExtensionsResponse> GetUserActiveExtensionsAsync(string? userId = null, CancellationToken cancellationToken = default)
    {
        if (userId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return _transport.SendAsync(HttpMethod.Get, "users/extensions", HelixJsonContext.Default.UserActiveExtensionsResponse,
            new HelixQuery().AddValue("user_id", userId), authorization: userId is null ? new([]) : null, cancellationToken: cancellationToken);
    }

    public Task<UserActiveExtensionsResponse> UpdateUserExtensionsAsync(UpdateUserExtensionsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Data);
        ValidateSlots(request.Data.Panel);
        ValidateSlots(request.Data.Overlay);
        ValidateSlots(request.Data.Component);
        return _transport.SendAsync(HttpMethod.Put, "users/extensions", HelixJsonContext.Default.UserActiveExtensionsResponse,
            jsonBody: JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.UpdateUserExtensionsRequest),
            authorization: new([TwitchScopes.UserEditBroadcast]), cancellationToken: cancellationToken);
    }

    private static void ValidateSlots<T>(IReadOnlyDictionary<string, T>? slots) where T : UserExtensionActivation
    {
        if (slots is null) return;
        foreach (var (key, slot) in slots)
        {
            if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index < 1) throw new ArgumentException("Extension slot keys must be positive integers.", nameof(slots));
            ArgumentNullException.ThrowIfNull(slot);
            if (!slot.Active) continue;
            ArgumentException.ThrowIfNullOrWhiteSpace(slot.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(slot.Version);
            if (slot is UserComponentExtensionActivation component && (!component.X.HasValue || !component.Y.HasValue))
                throw new ArgumentException("Active component extensions require x and y coordinates.", nameof(slots));
        }
    }
}
