namespace TwitchDock.Helix.Models;

/// <summary>Guest Star channel configuration (public beta). Discriminators remain strings to tolerate values added during the beta.</summary>
public sealed class GuestStarChannelSettings
{
    /// <summary>Whether Guest Star moderators may control whether an assigned guest is live.</summary>
    public bool IsModeratorSendLiveEnabled { get; init; }
    /// <summary>Number of slots the call interface allows the host to add (documented range 1–6).</summary>
    public int SlotCount { get; init; }
    /// <summary>Whether browser sources subscribed to this channel's sessions output audio.</summary>
    public bool IsBrowserSourceAudioEnabled { get; init; }
    /// <summary>Browser-source layout, for example <c>TILED_LAYOUT</c> or <c>SCREENSHARE_LAYOUT</c>.
    /// Nullable because the official example response names this field <c>layout</c> while the field table names it <c>group_layout</c>.</summary>
    public string? GroupLayout { get; init; }
    /// <summary>View-only token for generating browser source URLs. Treat it as a secret.</summary>
    public required string BrowserSourceToken { get; init; }
}

/// <summary>Settings for Update Channel Guest Star Settings. Null omits a field; false and zero are serialized explicitly.</summary>
public sealed class GuestStarUpdateChannelSettingsRequest
{
    public bool? IsModeratorSendLiveEnabled { get; init; }
    /// <summary>Between 1 and 6.</summary>
    public int? SlotCount { get; init; }
    public bool? IsBrowserSourceAudioEnabled { get; init; }
    /// <summary><c>TILED_LAYOUT</c>, <c>SCREENSHARE_LAYOUT</c>, <c>HORIZONTAL_LAYOUT</c> or <c>VERTICAL_LAYOUT</c>.</summary>
    public string? GroupLayout { get; init; }
    /// <summary>True immediately invalidates every previously configured browser source by regenerating its token.</summary>
    public bool? RegenerateBrowserSources { get; init; }
}

/// <summary>A Guest Star session (public beta).</summary>
public sealed class GuestStarSession
{
    public required string Id { get; init; }
    /// <summary>Guests currently interacting with the session; on creation it contains only the broadcaster.</summary>
    public IReadOnlyList<GuestStarGuest> Guests { get; init => field = value ?? []; } = [];
}

/// <summary>A guest's slot assignment within a Guest Star session.</summary>
public sealed class GuestStarGuest
{
    /// <summary>The host is always slot <c>"0"</c>, guests use consecutive IDs and screen share uses <c>"SCREENSHARE"</c>.
    /// Nullable because the official Create/End examples show the slot as <c>id</c> while every field table names it <c>slot_id</c>.</summary>
    public string? SlotId { get; init; }
    /// <summary>Whether the guest is visible in the browser source in the host's streaming software.</summary>
    public bool IsLive { get; init; }
    public required string UserId { get; init; }
    public required string UserDisplayName { get; init; }
    public required string UserLogin { get; init; }
    /// <summary>The host's volume setting for this guest, 0–100.</summary>
    public int Volume { get; init; }
    public DateTimeOffset AssignedAt { get; init; }
    public required GuestStarMediaSettings AudioSettings { get; init; }
    public required GuestStarMediaSettings VideoSettings { get; init; }
}

/// <summary>Audio or video state of a Guest Star guest.</summary>
public sealed class GuestStarMediaSettings
{
    /// <summary>Whether the host allows the guest's media to be seen or heard in the session.</summary>
    public bool IsHostEnabled { get; init; }
    /// <summary>Whether the guest allows their media to be transmitted to the session.</summary>
    public bool IsGuestEnabled { get; init; }
    /// <summary>Whether the guest has an appropriate device available.</summary>
    public bool IsAvailable { get; init; }
}

/// <summary>A pending Guest Star invite and the invitee's waiting-room state.</summary>
public sealed class GuestStarInvite
{
    public required string UserId { get; init; }
    public DateTimeOffset InvitedAt { get; init; }
    /// <summary><c>INVITED</c>, <c>ACCEPTED</c> or <c>READY</c>.</summary>
    public required string Status { get; init; }
    /// <summary>The invitee's local video choice; they may change it after joining.</summary>
    public bool IsVideoEnabled { get; init; }
    /// <summary>The invitee's local audio choice; they may change it after joining.</summary>
    public bool IsAudioEnabled { get; init; }
    public bool IsVideoAvailable { get; init; }
    public bool IsAudioAvailable { get; init; }
}

/// <summary>Identifies an invite for Send/Delete Guest Star Invite. All values are query parameters.</summary>
public sealed class GuestStarInviteRequest
{
    public required string BroadcasterId { get; init; }
    /// <summary>The broadcaster or one of their moderators; must match the token's user.</summary>
    public required string ModeratorId { get; init; }
    public required string SessionId { get; init; }
    public required string GuestId { get; init; }
}

/// <summary>Assign Guest Star Slot. All values are query parameters.</summary>
public sealed class GuestStarAssignSlotRequest
{
    public required string BroadcasterId { get; init; }
    /// <summary>The broadcaster or one of their moderators; must match the token's user.</summary>
    public required string ModeratorId { get; init; }
    public required string SessionId { get; init; }
    /// <summary>A guest who already has an invite and has signaled they are ready.</summary>
    public required string GuestId { get; init; }
    /// <summary>Numeric slot between "1" and the session's slot count.</summary>
    public required string SlotId { get; init; }
}

/// <summary>Update Guest Star Slot (move or swap an assignment). All values are query parameters.</summary>
public sealed class GuestStarUpdateSlotRequest
{
    public required string BroadcasterId { get; init; }
    /// <summary>The broadcaster or one of their moderators; must match the token's user.</summary>
    public required string ModeratorId { get; init; }
    public required string SessionId { get; init; }
    public required string SourceSlotId { get; init; }
    /// <summary>Optional target slot. An occupied destination swaps its guest into the source slot.</summary>
    public string? DestinationSlotId { get; init; }
}

/// <summary>Delete Guest Star Slot. All values are query parameters.</summary>
public sealed class GuestStarDeleteSlotRequest
{
    public required string BroadcasterId { get; init; }
    /// <summary>The broadcaster or one of their moderators; must match the token's user.</summary>
    public required string ModeratorId { get; init; }
    public required string SessionId { get; init; }
    public required string GuestId { get; init; }
    public required string SlotId { get; init; }
    /// <summary>True sends the guest back to the invite queue. Null omits the parameter; false is sent explicitly.</summary>
    public bool? ShouldReinviteGuest { get; init; }
}

/// <summary>Update Guest Star Slot Settings. All values are query parameters; null omits a setting and false/zero are sent explicitly.</summary>
public sealed class GuestStarUpdateSlotSettingsRequest
{
    public required string BroadcasterId { get; init; }
    /// <summary>The broadcaster or one of their moderators; must match the token's user.</summary>
    public required string ModeratorId { get; init; }
    public required string SessionId { get; init; }
    public required string SlotId { get; init; }
    /// <summary>False mutes the slot in every view. True allows the guest to unmute but does not unmute them.</summary>
    public bool? IsAudioEnabled { get; init; }
    /// <summary>False hides the slot's video in every view.</summary>
    public bool? IsVideoEnabled { get; init; }
    /// <summary>Whether the slot is visible/audible in public subscriptions such as broadcasting software.</summary>
    public bool? IsLive { get; init; }
    /// <summary>Audio volume for shared views, 0–100.</summary>
    public int? Volume { get; init; }
}
