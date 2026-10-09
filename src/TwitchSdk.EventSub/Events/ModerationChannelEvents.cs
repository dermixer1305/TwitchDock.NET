using System.Text.Json;
using System.Text.Json.Serialization;
using TwitchSdk.Core;

namespace TwitchSdk.EventSub.Events;

/// <summary>channel.update v2: the broadcaster changed the title, category, language or content classification labels.</summary>
public sealed class ChannelUpdateEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string Title { get; init; }
    public required string Language { get; init; }
    public required string CategoryId { get; init; }
    public required string CategoryName { get; init; }
    /// <summary>IDs of the content classification labels currently applied to the channel.</summary>
    public IReadOnlyList<string> ContentClassificationLabels { get; init => field = value ?? []; } = [];
}

/// <summary>channel.follow v2: a user followed the channel.</summary>
public sealed class ChannelFollowEvent
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public DateTimeOffset FollowedAt { get; init; }
}

/// <summary>channel.ad_break.begin v1: a midroll ad break started, manually or through Ads Manager.</summary>
public sealed class ChannelAdBreakBeginEvent
{
    /// <summary>Length of the ad break in seconds. Also accepts the quoted form shown in the official notification example.</summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int DurationSeconds { get; init; }
    /// <summary>When the ad break began; viewers may see the ads with some delay.</summary>
    public DateTimeOffset StartedAt { get; init; }
    /// <summary>True when Ads Manager scheduled the ad. Also accepts the quoted form shown in the official notification example.</summary>
    [JsonConverter(typeof(BooleanOrStringConverter))]
    public bool IsAutomatic { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    /// <summary>The user that requested the ad; the broadcaster for automatic ads.</summary>
    public required string RequesterUserId { get; init; }
    public required string RequesterUserLogin { get; init; }
    public required string RequesterUserName { get; init; }

    /// <summary>Reads JSON booleans and the strings "true"/"false"; writes JSON booleans.</summary>
    internal sealed class BooleanOrStringConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.String when bool.TryParse(reader.GetString(), out var value) => value,
            _ => throw new JsonException("Expected a boolean."),
        };

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteBooleanValue(value);
        }
    }
}

/// <summary>channel.raid v1: a broadcaster raided another channel.</summary>
public sealed class ChannelRaidEvent
{
    public required string FromBroadcasterUserId { get; init; }
    public required string FromBroadcasterUserLogin { get; init; }
    public required string FromBroadcasterUserName { get; init; }
    public required string ToBroadcasterUserId { get; init; }
    public required string ToBroadcasterUserLogin { get; init; }
    public required string ToBroadcasterUserName { get; init; }
    public int Viewers { get; init; }
}

/// <summary>channel.ban v1: a user was banned or timed out.</summary>
public sealed class ChannelBanEvent
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string ModeratorUserId { get; init; }
    public required string ModeratorUserLogin { get; init; }
    public required string ModeratorUserName { get; init; }
    public required string Reason { get; init; }
    /// <summary>When the user was banned or timed out.</summary>
    public DateTimeOffset BannedAt { get; init; }
    /// <summary>When the timeout ends; null (or an empty string on the wire) for permanent bans.</summary>
    [JsonConverter(typeof(EmptyStringAsNullDateTimeOffsetConverter))]
    public DateTimeOffset? EndsAt { get; init; }
    /// <summary>True for a permanent ban, false for a timeout.</summary>
    public bool IsPermanent { get; init; }
}

/// <summary>channel.unban v1: a user was unbanned.</summary>
public sealed class ChannelUnbanEvent
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string ModeratorUserId { get; init; }
    public required string ModeratorUserLogin { get; init; }
    public required string ModeratorUserName { get; init; }
}

/// <summary>channel.unban_request.create v1: a banned user created an unban request.</summary>
public sealed class ChannelUnbanRequestCreateEvent
{
    /// <summary>The unban request ID. Documented by Twitch but absent from some payloads (for example the Twitch CLI); null then.</summary>
    public string? Id { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    /// <summary>The message sent with the unban request.</summary>
    public required string Text { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// channel.unban_request.resolve v1: an unban request was approved, denied or canceled.
/// The field table documents moderator_id/moderator_login/moderator_name while the official notification example sends
/// moderator_user_id/moderator_user_login/moderator_user_name; both spellings are read.
/// </summary>
public sealed class ChannelUnbanRequestResolveEvent
{
    /// <summary>The unban request ID.</summary>
    public required string Id { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    /// <summary>The moderator who resolved the request, as named in the field table (optional).</summary>
    public string? ModeratorId { get; init; }
    public string? ModeratorLogin { get; init; }
    public string? ModeratorName { get; init; }
    /// <summary>The moderator who resolved the request, as named in the notification example (optional).</summary>
    public string? ModeratorUserId { get; init; }
    public string? ModeratorUserLogin { get; init; }
    public string? ModeratorUserName { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    /// <summary>Optional text supplied by the moderator or broadcaster.</summary>
    public string? ResolutionText { get; init; }
    /// <summary>approved, canceled or denied; kept as a string to tolerate new values.</summary>
    public required string Status { get; init; }
}

/// <summary>Fields shared by channel.moderate v1 and v2. Exactly the object that matches <see cref="Action"/> is non-null.</summary>
public abstract class ChannelModerateEventBase
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    /// <summary>The channel where the action happened; null or equal to the broadcaster outside shared chat.</summary>
    public string? SourceBroadcasterUserId { get; init; }
    public string? SourceBroadcasterUserLogin { get; init; }
    public string? SourceBroadcasterUserName { get; init; }
    public required string ModeratorUserId { get; init; }
    public required string ModeratorUserLogin { get; init; }
    public required string ModeratorUserName { get; init; }
    /// <summary>
    /// ban, timeout, unban, untimeout, clear, emoteonly, emoteonlyoff, followers, followersoff, uniquechat, uniquechatoff, slow, slowoff,
    /// subscribers, subscribersoff, unraid, delete, unvip, vip, raid, add_blocked_term, add_permitted_term, remove_blocked_term,
    /// remove_permitted_term, mod, unmod, approve_unban_request, deny_unban_request, warn (v2), shared_chat_ban, shared_chat_timeout,
    /// shared_chat_untimeout, shared_chat_unban or shared_chat_delete; kept as a string to tolerate new values.
    /// </summary>
    public required string Action { get; init; }
    public ChannelModerateFollowers? Followers { get; init; }
    public ChannelModerateSlow? Slow { get; init; }
    public ChannelModerateUser? Vip { get; init; }
    public ChannelModerateUser? Unvip { get; init; }
    public ChannelModerateUser? Mod { get; init; }
    public ChannelModerateUser? Unmod { get; init; }
    public ChannelModerateBan? Ban { get; init; }
    public ChannelModerateUser? Unban { get; init; }
    public ChannelModerateTimeout? Timeout { get; init; }
    public ChannelModerateUser? Untimeout { get; init; }
    public ChannelModerateRaid? Raid { get; init; }
    public ChannelModerateUser? Unraid { get; init; }
    public ChannelModerateDelete? Delete { get; init; }
    /// <summary>Set for add_blocked_term, add_permitted_term, remove_blocked_term and remove_permitted_term.</summary>
    public ChannelModerateAutomodTerms? AutomodTerms { get; init; }
    /// <summary>Set for approve_unban_request and deny_unban_request.</summary>
    public ChannelModerateUnbanRequest? UnbanRequest { get; init; }
    /// <summary>Like <see cref="Ban"/>, for a channel in the shared chat session other than the subscribed broadcaster.</summary>
    public ChannelModerateBan? SharedChatBan { get; init; }
    /// <summary>Like <see cref="Unban"/>, for a channel in the shared chat session other than the subscribed broadcaster.</summary>
    public ChannelModerateUser? SharedChatUnban { get; init; }
    /// <summary>Like <see cref="Timeout"/>, for a channel in the shared chat session other than the subscribed broadcaster.</summary>
    public ChannelModerateTimeout? SharedChatTimeout { get; init; }
    /// <summary>Like <see cref="Untimeout"/>, for a channel in the shared chat session other than the subscribed broadcaster.</summary>
    public ChannelModerateUser? SharedChatUntimeout { get; init; }
    /// <summary>Like <see cref="Delete"/>, for a channel in the shared chat session other than the subscribed broadcaster.</summary>
    public ChannelModerateDelete? SharedChatDelete { get; init; }
}

/// <summary>channel.moderate v1: a moderator performed a moderation action.</summary>
public sealed class ChannelModerateEvent : ChannelModerateEventBase;

/// <summary>channel.moderate v2: a moderator performed a moderation action, including warnings.</summary>
public sealed class ChannelModerateEventV2 : ChannelModerateEventBase
{
    public ChannelModerateWarn? Warn { get; init; }
}

/// <summary>The followers-only setting of a channel.moderate followers action.</summary>
public sealed class ChannelModerateFollowers
{
    /// <summary>How long, in minutes, users must have followed to chat.</summary>
    public int FollowDurationMinutes { get; init; }
}

/// <summary>The slow mode setting of a channel.moderate slow action.</summary>
public sealed class ChannelModerateSlow
{
    /// <summary>Seconds users must wait between messages.</summary>
    public int WaitTimeSeconds { get; init; }
}

/// <summary>The target user of a channel.moderate vip, unvip, mod, unmod, unban, untimeout or unraid action.</summary>
public sealed class ChannelModerateUser
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
}

/// <summary>The target of a channel.moderate ban action.</summary>
public sealed class ChannelModerateBan
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public string? Reason { get; init; }
}

/// <summary>The target of a channel.moderate timeout action.</summary>
public sealed class ChannelModerateTimeout
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public string? Reason { get; init; }
    /// <summary>When the timeout ends.</summary>
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>The target of a channel.moderate raid action.</summary>
public sealed class ChannelModerateRaid
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public int ViewerCount { get; init; }
}

/// <summary>The deleted message of a channel.moderate delete action.</summary>
public sealed class ChannelModerateDelete
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public required string MessageId { get; init; }
    public required string MessageBody { get; init; }
}

/// <summary>The AutoMod term change of a channel.moderate add/remove blocked/permitted term action.</summary>
public sealed class ChannelModerateAutomodTerms
{
    /// <summary>add or remove.</summary>
    public required string Action { get; init; }
    /// <summary>blocked or permitted.</summary>
    public required string List { get; init; }
    public IReadOnlyList<string> Terms { get; init => field = value ?? []; } = [];
    /// <summary>True when the terms changed because of an AutoMod message approve or deny action.</summary>
    public bool FromAutomod { get; init; }
}

/// <summary>The resolution of a channel.moderate approve_unban_request or deny_unban_request action.</summary>
public sealed class ChannelModerateUnbanRequest
{
    public bool IsApproved { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public string? ModeratorMessage { get; init; }
}

/// <summary>The target of a channel.moderate v2 warn action.</summary>
public sealed class ChannelModerateWarn
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public string? Reason { get; init; }
    /// <summary>Chat rules cited for the warning; null when none were cited.</summary>
    public IReadOnlyList<string>? ChatRulesCited { get; init; }
}

/// <summary>channel.moderator.add v1: a user became a moderator.</summary>
public sealed class ChannelModeratorAddEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
}

/// <summary>channel.moderator.remove v1: a user lost moderator status.</summary>
public sealed class ChannelModeratorRemoveEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
}

/// <summary>channel.vip.add v1: a user became a VIP.</summary>
public sealed class ChannelVipAddEvent
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
}

/// <summary>channel.vip.remove v1: a user lost VIP status.</summary>
public sealed class ChannelVipRemoveEvent
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
}

/// <summary>channel.shield_mode.begin v1: Shield Mode was activated.</summary>
public sealed class ChannelShieldModeBeginEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    /// <summary>The moderator who activated Shield Mode; equals the broadcaster when they did it themselves.</summary>
    public required string ModeratorUserId { get; init; }
    public required string ModeratorUserLogin { get; init; }
    public required string ModeratorUserName { get; init; }
    public DateTimeOffset StartedAt { get; init; }
}

/// <summary>channel.shield_mode.end v1: Shield Mode was deactivated.</summary>
public sealed class ChannelShieldModeEndEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    /// <summary>The moderator who deactivated Shield Mode; equals the broadcaster when they did it themselves.</summary>
    public required string ModeratorUserId { get; init; }
    public required string ModeratorUserLogin { get; init; }
    public required string ModeratorUserName { get; init; }
    public DateTimeOffset EndedAt { get; init; }
}

/// <summary>channel.shoutout.create v1: the broadcaster sent a Shoutout.</summary>
public sealed class ChannelShoutoutCreateEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string ToBroadcasterUserId { get; init; }
    public required string ToBroadcasterUserLogin { get; init; }
    public required string ToBroadcasterUserName { get; init; }
    /// <summary>The moderator who sent the Shoutout; equals the broadcaster when they did it themselves.</summary>
    public required string ModeratorUserId { get; init; }
    public required string ModeratorUserLogin { get; init; }
    public required string ModeratorUserName { get; init; }
    /// <summary>Viewers of the broadcaster's stream at the time of the Shoutout.</summary>
    public int ViewerCount { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    /// <summary>When the broadcaster may send a Shoutout to a different broadcaster.</summary>
    public DateTimeOffset CooldownEndsAt { get; init; }
    /// <summary>When the broadcaster may shout out the same broadcaster again.</summary>
    public DateTimeOffset TargetCooldownEndsAt { get; init; }
}

/// <summary>channel.shoutout.receive v1: the broadcaster received a Shoutout (only when Twitch posts it to the activity feed).</summary>
public sealed class ChannelShoutoutReceiveEvent
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string FromBroadcasterUserId { get; init; }
    public required string FromBroadcasterUserLogin { get; init; }
    public required string FromBroadcasterUserName { get; init; }
    /// <summary>Viewers of the sending broadcaster's stream at the time of the Shoutout.</summary>
    public int ViewerCount { get; init; }
    public DateTimeOffset StartedAt { get; init; }
}
