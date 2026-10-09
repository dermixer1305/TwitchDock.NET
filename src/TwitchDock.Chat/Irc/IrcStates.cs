using System.Diagnostics.CodeAnalysis;

namespace TwitchDock.Chat.Irc;

/// <summary>
/// A typed view of a ROOMSTATE. After JOIN Twitch sends every setting; later updates contain only the changed tag,
/// so each setting is null when this message does not include it.
/// </summary>
public sealed class IrcRoomState
{
    private IrcRoomState(IrcMessage raw, string channel)
    {
        Raw = raw;
        Channel = channel;
    }

    /// <summary>Creates the view when <paramref name="message"/> is a ROOMSTATE for a channel.</summary>
    public static bool TryCreate(IrcMessage message, [NotNullWhen(true)] out IrcRoomState? roomState)
    {
        ArgumentNullException.ThrowIfNull(message);
        roomState = message.Command == "ROOMSTATE" && IrcTagReader.Channel(message) is { } channel ? new IrcRoomState(message, channel) : null;
        return roomState is not null;
    }

    /// <summary>The underlying IRC message with all tags.</summary>
    public IrcMessage Raw { get; }
    /// <summary>The channel login without <c>#</c>.</summary>
    public string Channel { get; }
    /// <summary>The <c>room-id</c> tag.</summary>
    public string? RoomId => IrcTagReader.Text(Raw, "room-id");
    /// <summary>The <c>emote-only</c> tag.</summary>
    public bool? IsEmoteOnly => IrcTagReader.OptionalFlag(Raw, "emote-only");
    /// <summary>The <c>followers-only</c> tag: false for <c>-1</c>, true for zero or more minutes.</summary>
    public bool? IsFollowersOnly => IrcTagReader.Int32(Raw, "followers-only") is { } minutes ? minutes >= 0 : null;
    /// <summary>How long users must follow before chatting when <see cref="IsFollowersOnly"/> is true; zero means any follower.</summary>
    public TimeSpan? FollowersOnlyDuration => IrcTagReader.Int32(Raw, "followers-only") is { } minutes and >= 0 ? TimeSpan.FromMinutes(minutes) : null;
    /// <summary>The <c>r9k</c> tag: unique chat mode.</summary>
    public bool? IsUniqueChat => IrcTagReader.OptionalFlag(Raw, "r9k");
    /// <summary>The <c>slow</c> tag; <see cref="TimeSpan.Zero"/> means slow mode is off.</summary>
    public TimeSpan? SlowModeDelay => IrcTagReader.Seconds(Raw, "slow");
    /// <summary>The <c>subs-only</c> tag.</summary>
    public bool? IsSubscribersOnly => IrcTagReader.OptionalFlag(Raw, "subs-only");
}

/// <summary>A typed view of a USERSTATE: the authenticated user's state in a channel, sent after JOIN and after each PRIVMSG.</summary>
public sealed class IrcUserState
{
    private IrcUserState(IrcMessage raw, string channel)
    {
        Raw = raw;
        Channel = channel;
        Badges = IrcTagReader.Badges(raw, "badges");
        BadgeInfo = IrcTagReader.BadgeInfo(raw, "badge-info");
        EmoteSets = IrcTagReader.List(raw, "emote-sets");
    }

    /// <summary>Creates the view when <paramref name="message"/> is a USERSTATE for a channel.</summary>
    public static bool TryCreate(IrcMessage message, [NotNullWhen(true)] out IrcUserState? userState)
    {
        ArgumentNullException.ThrowIfNull(message);
        userState = message.Command == "USERSTATE" && IrcTagReader.Channel(message) is { } channel ? new IrcUserState(message, channel) : null;
        return userState is not null;
    }

    /// <summary>The underlying IRC message with all tags.</summary>
    public IrcMessage Raw { get; }
    /// <summary>The channel login without <c>#</c>.</summary>
    public string Channel { get; }
    /// <summary>The <c>id</c> tag: the ID of the message just sent, when this USERSTATE acknowledges a PRIVMSG.</summary>
    public string? MessageId => IrcTagReader.Text(Raw, "id");
    /// <summary>The <c>display-name</c> tag.</summary>
    public string? DisplayName => IrcTagReader.Text(Raw, "display-name");
    /// <summary>The <c>color</c> tag.</summary>
    public string? Color => IrcTagReader.Text(Raw, "color");
    /// <summary>The <c>badges</c> tag.</summary>
    public IReadOnlyList<IrcBadge> Badges { get; }
    /// <summary>The <c>badge-info</c> tag.</summary>
    public IReadOnlyDictionary<string, string> BadgeInfo { get; }
    /// <summary>The <c>emote-sets</c> tag.</summary>
    public IReadOnlyList<string> EmoteSets { get; }
    /// <summary>The <c>mod</c> tag. Use it to choose the moderator message rate limit.</summary>
    public bool IsModerator => IrcTagReader.Flag(Raw, "mod");
    /// <summary>The <c>subscriber</c> tag.</summary>
    public bool IsSubscriber => IrcTagReader.Flag(Raw, "subscriber");
    /// <summary>The deprecated <c>turbo</c> tag.</summary>
    public bool IsTurbo => IrcTagReader.Flag(Raw, "turbo");
    /// <summary>The <c>user-type</c> tag.</summary>
    public string? UserType => IrcTagReader.Text(Raw, "user-type");
    /// <summary>True when the user has the <c>broadcaster</c> badge in this channel.</summary>
    public bool IsBroadcaster => Badges.Any(badge => badge.SetId == "broadcaster");
}

/// <summary>A typed view of a GLOBALUSERSTATE, sent once after a successful login.</summary>
public sealed class IrcGlobalUserState
{
    private IrcGlobalUserState(IrcMessage raw)
    {
        Raw = raw;
        Badges = IrcTagReader.Badges(raw, "badges");
        BadgeInfo = IrcTagReader.BadgeInfo(raw, "badge-info");
        EmoteSets = IrcTagReader.List(raw, "emote-sets");
    }

    /// <summary>Creates the view when <paramref name="message"/> is a GLOBALUSERSTATE.</summary>
    public static bool TryCreate(IrcMessage message, [NotNullWhen(true)] out IrcGlobalUserState? globalUserState)
    {
        ArgumentNullException.ThrowIfNull(message);
        globalUserState = message.Command == "GLOBALUSERSTATE" ? new IrcGlobalUserState(message) : null;
        return globalUserState is not null;
    }

    /// <summary>The underlying IRC message with all tags.</summary>
    public IrcMessage Raw { get; }
    /// <summary>The <c>user-id</c> tag of the authenticated user.</summary>
    public string? UserId => IrcTagReader.Text(Raw, "user-id");
    /// <summary>The <c>display-name</c> tag.</summary>
    public string? DisplayName => IrcTagReader.Text(Raw, "display-name");
    /// <summary>The <c>color</c> tag.</summary>
    public string? Color => IrcTagReader.Text(Raw, "color");
    /// <summary>The <c>badges</c> tag.</summary>
    public IReadOnlyList<IrcBadge> Badges { get; }
    /// <summary>The <c>badge-info</c> tag.</summary>
    public IReadOnlyDictionary<string, string> BadgeInfo { get; }
    /// <summary>The <c>emote-sets</c> tag.</summary>
    public IReadOnlyList<string> EmoteSets { get; }
    /// <summary>The deprecated <c>turbo</c> tag.</summary>
    public bool IsTurbo => IrcTagReader.Flag(Raw, "turbo");
    /// <summary>The <c>user-type</c> tag.</summary>
    public string? UserType => IrcTagReader.Text(Raw, "user-type");
}
