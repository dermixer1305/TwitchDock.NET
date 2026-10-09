using System.Text.Json;
using TwitchSdk.Core;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Helix.Clients;

/// <summary>Guest Star endpoints. Guest Star is BETA (public beta) in the official API reference; contracts may change without a version bump.</summary>
public sealed class GuestStarClient(TwitchHttpClient transport)
{
    private const string ChannelSettingsPath = "guest_star/channel_settings";
    private const string SessionPath = "guest_star/session";
    private const string InvitesPath = "guest_star/invites";
    private const string SlotPath = "guest_star/slot";
    private const string SlotSettingsPath = "guest_star/slot_settings";
    private static readonly string[] GroupLayouts = ["TILED_LAYOUT", "SCREENSHARE_LAYOUT", "HORIZONTAL_LAYOUT", "VERTICAL_LAYOUT"];
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    /// <summary>BETA. Gets the broadcaster's Guest Star settings. The moderator ID (broadcaster or moderator) must match the token user.</summary>
    public Task<HelixPage<GuestStarChannelSettings>> GetChannelGuestStarSettingsAsync(string broadcasterId, string moderatorId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moderatorId);
        return _transport.SendAsync(HttpMethod.Get, ChannelSettingsPath, HelixJsonContext.Default.HelixPageGuestStarChannelSettings,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId).AddValue("moderator_id", moderatorId),
            authorization: Read(moderatorId), cancellationToken: cancellationToken);
    }

    /// <summary>BETA. Changes the broadcaster's Guest Star settings (JSON body). Requires the broadcaster's token; regenerating browser sources invalidates existing ones.</summary>
    public Task UpdateChannelGuestStarSettingsAsync(string broadcasterId, GuestStarUpdateChannelSettingsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentNullException.ThrowIfNull(request);
        if (request.SlotCount is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(request), "Slot count must be between 1 and 6.");
        if (request.GroupLayout is not null && !GroupLayouts.Contains(request.GroupLayout, StringComparer.Ordinal))
            throw new ArgumentException("Group layout must be TILED_LAYOUT, SCREENSHARE_LAYOUT, HORIZONTAL_LAYOUT or VERTICAL_LAYOUT.", nameof(request));
        return _transport.SendAsync(HttpMethod.Put, ChannelSettingsPath,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId),
            JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.GuestStarUpdateChannelSettingsRequest),
            authorization: Broadcaster(broadcasterId), cancellationToken: cancellationToken);
    }

    /// <summary>BETA. Gets the channel's ongoing Guest Star session. The moderator ID must match the token user.</summary>
    public Task<HelixPage<GuestStarSession>> GetGuestStarSessionAsync(string broadcasterId, string moderatorId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moderatorId);
        return _transport.SendAsync(HttpMethod.Get, SessionPath, HelixJsonContext.Default.HelixPageGuestStarSession,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId).AddValue("moderator_id", moderatorId),
            authorization: Read(moderatorId), cancellationToken: cancellationToken);
    }

    /// <summary>BETA. Starts a session for the broadcaster, who must be present in the call interface or Twitch ends the call. Never retried after ambiguous failures.</summary>
    public Task<HelixPage<GuestStarSession>> CreateGuestStarSessionAsync(string broadcasterId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        return _transport.SendAsync(HttpMethod.Post, SessionPath, HelixJsonContext.Default.HelixPageGuestStarSession,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId),
            authorization: Broadcaster(broadcasterId), cancellationToken: cancellationToken);
    }

    /// <summary>BETA. Ends the session like the host's "End Call" button and returns its final state.</summary>
    public Task<HelixPage<GuestStarSession>> EndGuestStarSessionAsync(string broadcasterId, string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return _transport.SendAsync(HttpMethod.Delete, SessionPath, HelixJsonContext.Default.HelixPageGuestStarSession,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId).AddValue("session_id", sessionId),
            authorization: Broadcaster(broadcasterId), cancellationToken: cancellationToken);
    }

    /// <summary>BETA. Lists pending invites with each invitee's waiting-room state. The moderator ID must match the token user.</summary>
    public Task<HelixPage<GuestStarInvite>> GetGuestStarInvitesAsync(string broadcasterId, string moderatorId, string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moderatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return _transport.SendAsync(HttpMethod.Get, InvitesPath, HelixJsonContext.Default.HelixPageGuestStarInvite,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId).AddValue("moderator_id", moderatorId).AddValue("session_id", sessionId),
            authorization: Read(moderatorId), cancellationToken: cancellationToken);
    }

    /// <summary>BETA. Invites a guest to the session in progress. All values are query parameters.</summary>
    public Task SendGuestStarInviteAsync(GuestStarInviteRequest request, CancellationToken cancellationToken = default)
        => SendInviteAsync(HttpMethod.Post, request, cancellationToken);

    /// <summary>BETA. Revokes a previously sent invite. All values are query parameters.</summary>
    public Task DeleteGuestStarInviteAsync(GuestStarInviteRequest request, CancellationToken cancellationToken = default)
        => SendInviteAsync(HttpMethod.Delete, request, cancellationToken);

    /// <summary>BETA. Assigns a ready, invited guest to a numeric guest slot. All values are query parameters.</summary>
    public Task AssignGuestStarSlotAsync(GuestStarAssignSlotRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireSession(request.BroadcasterId, request.ModeratorId, request.SessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.GuestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SlotId);
        // The host occupies slot "0"; guest slots are numeric IDs from "1" to the session's slot count.
        if (request.SlotId[0] is < '1' or > '9' || !request.SlotId.All(char.IsAsciiDigit))
            throw new ArgumentException("Slot ID must be a numeric guest slot of at least 1.", nameof(request));
        return _transport.SendAsync(HttpMethod.Post, SlotPath,
            SessionQuery(request.BroadcasterId, request.ModeratorId, request.SessionId).AddValue("guest_id", request.GuestId).AddValue("slot_id", request.SlotId),
            authorization: Manage(request.ModeratorId), cancellationToken: cancellationToken);
    }

    /// <summary>BETA. Moves an assignment to another slot; an occupied destination is swapped into the source slot. All values are query parameters.</summary>
    public Task UpdateGuestStarSlotAsync(GuestStarUpdateSlotRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireSession(request.BroadcasterId, request.ModeratorId, request.SessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceSlotId);
        if (request.DestinationSlotId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationSlotId);
        return _transport.SendAsync(HttpMethod.Patch, SlotPath,
            SessionQuery(request.BroadcasterId, request.ModeratorId, request.SessionId)
                .AddValue("source_slot_id", request.SourceSlotId).AddValue("destination_slot_id", request.DestinationSlotId),
            authorization: Manage(request.ModeratorId), cancellationToken: cancellationToken);
    }

    /// <summary>BETA. Removes a guest from their slot and immediately revokes their session media access. All values are query parameters.</summary>
    public Task DeleteGuestStarSlotAsync(GuestStarDeleteSlotRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireSession(request.BroadcasterId, request.ModeratorId, request.SessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.GuestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SlotId);
        return _transport.SendAsync(HttpMethod.Delete, SlotPath,
            SessionQuery(request.BroadcasterId, request.ModeratorId, request.SessionId)
                .AddValue("guest_id", request.GuestId).AddValue("slot_id", request.SlotId).AddValue("should_reinvite_guest", request.ShouldReinviteGuest),
            authorization: Manage(request.ModeratorId), cancellationToken: cancellationToken);
    }

    /// <summary>BETA. Changes audio/video/live/volume settings of a slot; at least one setting is required. All values, including the settings, are query parameters.</summary>
    public Task UpdateGuestStarSlotSettingsAsync(GuestStarUpdateSlotSettingsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireSession(request.BroadcasterId, request.ModeratorId, request.SessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SlotId);
        if (request is { IsAudioEnabled: null, IsVideoEnabled: null, IsLive: null, Volume: null })
            throw new ArgumentException("Specify at least one of IsAudioEnabled, IsVideoEnabled, IsLive or Volume.", nameof(request));
        if (request.Volume is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(request), "Volume must be between 0 and 100.");
        return _transport.SendAsync(HttpMethod.Patch, SlotSettingsPath,
            SessionQuery(request.BroadcasterId, request.ModeratorId, request.SessionId).AddValue("slot_id", request.SlotId)
                .AddValue("is_audio_enabled", request.IsAudioEnabled).AddValue("is_video_enabled", request.IsVideoEnabled)
                .AddValue("is_live", request.IsLive).AddValue("volume", request.Volume),
            authorization: Manage(request.ModeratorId), cancellationToken: cancellationToken);
    }

    private Task SendInviteAsync(HttpMethod method, GuestStarInviteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireSession(request.BroadcasterId, request.ModeratorId, request.SessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.GuestId);
        return _transport.SendAsync(method, InvitesPath,
            SessionQuery(request.BroadcasterId, request.ModeratorId, request.SessionId).AddValue("guest_id", request.GuestId),
            authorization: Manage(request.ModeratorId), cancellationToken: cancellationToken);
    }

    private static void RequireSession(string broadcasterId, string moderatorId, string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moderatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
    }

    private static HelixQuery SessionQuery(string broadcasterId, string moderatorId, string sessionId)
        => new HelixQuery().AddValue("broadcaster_id", broadcasterId).AddValue("moderator_id", moderatorId).AddValue("session_id", sessionId);

    // Broadcaster-only operations: broadcaster_id must match the token user.
    private static TwitchAuthorizationRequirement Broadcaster(string broadcasterId) => new([TwitchScopes.ChannelManageGuestStar], requiredUserId: broadcasterId);

    // moderator_id (the broadcaster or a moderator) must match the token user; the documented scopes are alternatives.
    private static TwitchAuthorizationRequirement Read(string moderatorId) => new([], requiredUserId: moderatorId, anyUserScopes:
        [TwitchScopes.ChannelReadGuestStar, TwitchScopes.ChannelManageGuestStar, TwitchScopes.ModeratorReadGuestStar, TwitchScopes.ModeratorManageGuestStar]);

    private static TwitchAuthorizationRequirement Manage(string moderatorId) => new([], requiredUserId: moderatorId, anyUserScopes:
        [TwitchScopes.ChannelManageGuestStar, TwitchScopes.ModeratorManageGuestStar]);
}
