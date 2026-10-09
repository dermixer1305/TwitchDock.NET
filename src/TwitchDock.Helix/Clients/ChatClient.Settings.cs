using System.Text.Json;
using TwitchDock.Core;
using TwitchDock.Helix.Models;

namespace TwitchDock.Helix.Clients;

// Chat settings, announcements, Shoutouts, pinned messages, user chat colors and shared chat sessions.
public sealed partial class ChatClient
{
    private const string ChatSettingsPath = "chat/settings";
    private const string ChatPinsPath = "chat/pins";
    private const string UserChatColorPath = "chat/color";
    private const int MinimumPinSeconds = 30;
    private const int MaximumPinSeconds = 1800;
    private static readonly string[] AnnouncementColorNames = ["blue", "green", "orange", "purple", "primary"];
    private static readonly string[] UserChatColorNames =
    [
        "blue", "blue_violet", "cadet_blue", "chocolate", "coral", "dodger_blue", "firebrick", "golden_rod",
        "green", "hot_pink", "orange_red", "red", "sea_green", "spring_green", "yellow_green"
    ];

    /// <summary>App or user token. A moderator ID requires that moderator's user token and adds the non-moderator chat delay fields.</summary>
    public Task<HelixPage<ChatSettings>> GetChatSettingsAsync(string broadcasterId, string? moderatorId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        if (moderatorId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(moderatorId);
        return _transport.SendAsync(HttpMethod.Get, ChatSettingsPath, HelixJsonContext.Default.HelixPageChatSettings,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId).AddValue("moderator_id", moderatorId),
            authorization: new([], allowAppToken: moderatorId is null, requiredUserId: moderatorId), cancellationToken: cancellationToken);
    }

    /// <summary>Requires moderator:manage:chat_settings for the moderator. Only non-null fields are sent.</summary>
    public Task<HelixPage<ChatSettings>> UpdateChatSettingsAsync(string broadcasterId, string moderatorId, UpdateChatSettingsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moderatorId);
        ArgumentNullException.ThrowIfNull(request);
        ValidateChatSettings(request);
        return _transport.SendAsync(HttpMethod.Patch, ChatSettingsPath, HelixJsonContext.Default.HelixPageChatSettings,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId).AddValue("moderator_id", moderatorId),
            JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.UpdateChatSettingsRequest),
            authorization: new([TwitchScopes.ModeratorManageChatSettings], allowAppToken: true, requiredUserId: moderatorId), cancellationToken: cancellationToken);
    }

    /// <summary>Requires moderator:manage:announcements. ForSourceOnly is app-token only. Twitch allows one announcement every 2 seconds.</summary>
    public Task SendChatAnnouncementAsync(string broadcasterId, string moderatorId, SendChatAnnouncementRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moderatorId);
        ArgumentNullException.ThrowIfNull(request);
        HelixValidation.Text(request.Message, 500, nameof(request));
        if (request.Color is not null && !AnnouncementColorNames.Contains(request.Color, StringComparer.Ordinal))
            throw new ArgumentException("Announcement color must be blue, green, orange, purple or primary.", nameof(request));
        return _transport.SendAsync(HttpMethod.Post, "chat/announcements",
            new HelixQuery().AddValue("broadcaster_id", broadcasterId).AddValue("moderator_id", moderatorId),
            JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.SendChatAnnouncementRequest),
            authorization: new([TwitchScopes.ModeratorManageAnnouncements], allowAppToken: true, requiredUserId: moderatorId, allowUserToken: !request.ForSourceOnly.HasValue),
            cancellationToken: cancellationToken);
    }

    /// <summary>Requires moderator:manage:shoutouts. Twitch allows one Shoutout per 2 minutes and one per receiver per 60 minutes.</summary>
    public Task SendShoutoutAsync(string fromBroadcasterId, string toBroadcasterId, string moderatorId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromBroadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toBroadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moderatorId);
        if (string.Equals(fromBroadcasterId, toBroadcasterId, StringComparison.Ordinal))
            throw new ArgumentException("A broadcaster may not give themselves a Shoutout.", nameof(toBroadcasterId));
        return _transport.SendAsync(HttpMethod.Post, "chat/shoutouts",
            new HelixQuery().AddValue("from_broadcaster_id", fromBroadcasterId).AddValue("to_broadcaster_id", toBroadcasterId).AddValue("moderator_id", moderatorId),
            authorization: new([TwitchScopes.ModeratorManageShoutouts], allowAppToken: true, requiredUserId: moderatorId), cancellationToken: cancellationToken);
    }

    /// <summary>Requires moderator:read:chat_messages or moderator:manage:chat_messages. Data is empty when nothing is pinned.</summary>
    public Task<HelixPage<PinnedChatMessage>> GetPinnedChatMessageAsync(string broadcasterId, string moderatorId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moderatorId);
        return _transport.SendAsync(HttpMethod.Get, ChatPinsPath, HelixJsonContext.Default.HelixPagePinnedChatMessage,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId).AddValue("moderator_id", moderatorId),
            authorization: new([], allowAppToken: true, requiredUserId: moderatorId,
                anyUserScopes: [TwitchScopes.ModeratorReadChatMessages, TwitchScopes.ModeratorManageChatMessages]), cancellationToken: cancellationToken);
    }

    /// <summary>Replaces any existing mod-pinned message. Null duration pins until the stream ends; otherwise 30–1800 seconds.</summary>
    public Task PinChatMessageAsync(string broadcasterId, string moderatorId, string messageId, int? durationSeconds = null, CancellationToken cancellationToken = default)
        => SendPinChangeAsync(HttpMethod.Put, broadcasterId, moderatorId, messageId, durationSeconds, cancellationToken);

    /// <summary>Restarts the pin duration from now (30–1800 seconds); null keeps the message pinned until the stream ends.</summary>
    public Task UpdatePinnedChatMessageAsync(string broadcasterId, string moderatorId, string messageId, int? durationSeconds = null, CancellationToken cancellationToken = default)
        => SendPinChangeAsync(HttpMethod.Patch, broadcasterId, moderatorId, messageId, durationSeconds, cancellationToken);

    public Task UnpinChatMessageAsync(string broadcasterId, string moderatorId, string messageId, CancellationToken cancellationToken = default)
        => SendPinChangeAsync(HttpMethod.Delete, broadcasterId, moderatorId, messageId, null, cancellationToken);

    /// <summary>App or user token. Accepts 1–100 user IDs; Twitch ignores duplicates and unknown IDs.</summary>
    public Task<HelixPage<UserChatColor>> GetUserChatColorsAsync(IReadOnlyList<string> userIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count == 0) throw new ArgumentException("At least one user ID is required.", nameof(userIds));
        return _transport.SendAsync(HttpMethod.Get, UserChatColorPath, HelixJsonContext.Default.HelixPageUserChatColor,
            new HelixQuery().AddValues("user_id", userIds, 100), authorization: new([], allowAppToken: true), cancellationToken: cancellationToken);
    }

    /// <summary>Requires the user's token with user:manage:chat_color. Named colors work for everyone; #RRGGBB requires Turbo or Prime.</summary>
    public Task UpdateUserChatColorAsync(string userId, string color, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(color);
        if (!UserChatColorNames.Contains(color, StringComparer.Ordinal) && !IsHexColor(color))
            throw new ArgumentException("Color must be a documented named color or a #RRGGBB Hex code.", nameof(color));
        return _transport.SendAsync(HttpMethod.Put, UserChatColorPath, new HelixQuery().AddValue("user_id", userId).AddValue("color", color),
            authorization: new([TwitchScopes.UserManageChatColor], requiredUserId: userId), cancellationToken: cancellationToken);
    }

    /// <summary>App or user token. Data is empty when the broadcaster is not in a shared chat session.</summary>
    public Task<HelixPage<SharedChatSession>> GetSharedChatSessionAsync(string broadcasterId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        return _transport.SendAsync(HttpMethod.Get, "shared_chat/session", HelixJsonContext.Default.HelixPageSharedChatSession,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId), authorization: new([], allowAppToken: true), cancellationToken: cancellationToken);
    }

    private Task SendPinChangeAsync(HttpMethod method, string broadcasterId, string moderatorId, string messageId, int? durationSeconds, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moderatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        if (durationSeconds is < MinimumPinSeconds or > MaximumPinSeconds)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Pin duration must be between 30 and 1800 seconds.");
        return _transport.SendAsync(method, ChatPinsPath,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId).AddValue("moderator_id", moderatorId).AddValue("message_id", messageId)
                .AddValue("duration_seconds", durationSeconds),
            authorization: new([TwitchScopes.ModeratorManageChatMessages], allowAppToken: true, requiredUserId: moderatorId), cancellationToken: cancellationToken);
    }

    private static void ValidateChatSettings(UpdateChatSettingsRequest request)
    {
        // Twitch accepts a duration only together with its mode set to true; false clears the duration.
        foreach (var (mode, value, name) in new (bool? Mode, int? Value, string Name)[]
        {
            (request.FollowerMode, request.FollowerModeDuration, "Follower mode duration"),
            (request.SlowMode, request.SlowModeWaitTime, "Slow mode wait time"),
            (request.NonModeratorChatDelay, request.NonModeratorChatDelayDuration, "Non-moderator chat delay duration")
        })
        {
            if (value.HasValue && mode != true) throw new ArgumentException($"{name} may only be set when its mode is set to true in the same request.", nameof(request));
        }
        if (request.FollowerModeDuration is < 0 or > 129600)
            throw new ArgumentOutOfRangeException(nameof(request), "Follower mode duration must be between 0 and 129600 minutes.");
        if (request.SlowModeWaitTime is < 3 or > 120)
            throw new ArgumentOutOfRangeException(nameof(request), "Slow mode wait time must be between 3 and 120 seconds.");
        if (request.NonModeratorChatDelayDuration is { } delay && delay is not (2 or 4 or 6))
            throw new ArgumentOutOfRangeException(nameof(request), "Non-moderator chat delay duration must be 2, 4 or 6 seconds.");
        // Unlike slow and follower mode, the delay has no documented default value.
        if (request.NonModeratorChatDelay == true && request.NonModeratorChatDelayDuration is null)
            throw new ArgumentException("Enabling the non-moderator chat delay requires its duration.", nameof(request));
    }

    private static bool IsHexColor(string color) => color.Length == 7 && color[0] == '#' && color.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;
}
