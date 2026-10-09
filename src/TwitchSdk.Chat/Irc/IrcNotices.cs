using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace TwitchSdk.Chat.Irc;

/// <summary>
/// A typed view of a USERNOTICE: subscriptions, gifts, raids, announcements, milestones and shared chat notices.
/// <see cref="MsgId"/> stays a string so new notice kinds remain readable.
/// </summary>
public sealed class IrcUserNotice
{
    private const string ParameterPrefix = "msg-param-";

    private IrcUserNotice(IrcMessage raw, string channel)
    {
        Raw = raw;
        Channel = channel;
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in raw.Tags)
            if (key.StartsWith(ParameterPrefix, StringComparison.Ordinal) && key.Length > ParameterPrefix.Length) parameters[key[ParameterPrefix.Length..]] = value;
        Parameters = new ReadOnlyDictionary<string, string>(parameters);
        Badges = IrcTagReader.Badges(raw, "badges");
        BadgeInfo = IrcTagReader.BadgeInfo(raw, "badge-info");
        Emotes = IrcTagReader.Emotes(raw, "emotes");
        Source = IrcTagReader.SharedChatSource(raw);
    }

    /// <summary>Creates the view when <paramref name="message"/> is a USERNOTICE to a channel.</summary>
    public static bool TryCreate(IrcMessage message, [NotNullWhen(true)] out IrcUserNotice? notice)
    {
        ArgumentNullException.ThrowIfNull(message);
        notice = message.Command == "USERNOTICE" && IrcTagReader.Channel(message) is { } channel ? new IrcUserNotice(message, channel) : null;
        return notice is not null;
    }

    /// <summary>The underlying IRC message with all tags.</summary>
    public IrcMessage Raw { get; }
    /// <summary>The channel login without <c>#</c>.</summary>
    public string Channel { get; }
    /// <summary>The optional message the user attached, for example to a resubscription.</summary>
    public string? Text => Raw.GetParameter(1) is { Length: > 0 } text ? text : null;
    /// <summary>The <c>msg-id</c> tag, for example <c>sub</c>, <c>resub</c>, <c>subgift</c>, <c>raid</c>, <c>announcement</c> or <c>sharedchatnotice</c>.</summary>
    public string? MsgId => IrcTagReader.Text(Raw, "msg-id");
    /// <summary>All <c>msg-param-*</c> tags keyed without the <c>msg-param-</c> prefix, for example <c>cumulative-months</c>.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; }
    /// <summary>The unescaped <c>system-msg</c> tag that Twitch displays for the notice.</summary>
    public string? SystemMessage => IrcTagReader.Text(Raw, "system-msg");
    /// <summary>The <c>id</c> tag.</summary>
    public string? MessageId => IrcTagReader.Text(Raw, "id");
    /// <summary>The <c>login</c> tag of the user who caused the notice.</summary>
    public string? Login => IrcTagReader.Text(Raw, "login");
    /// <summary>The <c>user-id</c> tag.</summary>
    public string? UserId => IrcTagReader.Text(Raw, "user-id");
    /// <summary>The <c>display-name</c> tag.</summary>
    public string? DisplayName => IrcTagReader.Text(Raw, "display-name");
    /// <summary>The <c>color</c> tag.</summary>
    public string? Color => IrcTagReader.Text(Raw, "color");
    /// <summary>The <c>badges</c> tag.</summary>
    public IReadOnlyList<IrcBadge> Badges { get; }
    /// <summary>The <c>badge-info</c> tag.</summary>
    public IReadOnlyDictionary<string, string> BadgeInfo { get; }
    /// <summary>The <c>emotes</c> tag for <see cref="Text"/>.</summary>
    public IReadOnlyList<IrcEmote> Emotes { get; }
    /// <summary>The <c>mod</c> tag.</summary>
    public bool IsModerator => IrcTagReader.Flag(Raw, "mod");
    /// <summary>The <c>subscriber</c> tag.</summary>
    public bool IsSubscriber => IrcTagReader.Flag(Raw, "subscriber");
    /// <summary>The deprecated <c>turbo</c> tag.</summary>
    public bool IsTurbo => IrcTagReader.Flag(Raw, "turbo");
    /// <summary>The <c>user-type</c> tag.</summary>
    public string? UserType => IrcTagReader.Text(Raw, "user-type");
    /// <summary>The <c>room-id</c> tag.</summary>
    public string? RoomId => IrcTagReader.Text(Raw, "room-id");
    /// <summary>The <c>tmi-sent-ts</c> tag.</summary>
    public DateTimeOffset? SentAt => IrcTagReader.UnixMilliseconds(Raw, "tmi-sent-ts");
    /// <summary>Shared chat metadata, including <c>source-msg-id</c>, when the notice originated in another channel.</summary>
    public IrcSharedChatSource? Source { get; }

    /// <summary>Reads a numeric <c>msg-param-*</c> value by its name without the prefix.</summary>
    public bool TryGetInt32Parameter(string name, out int value)
    {
        ArgumentNullException.ThrowIfNull(name);
        value = 0;
        return Parameters.TryGetValue(name, out var text) && int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }
}

/// <summary>A typed view of a NOTICE, for example command results, errors and login failures.</summary>
public sealed class IrcNotice
{
    private IrcNotice(IrcMessage raw) => Raw = raw;

    /// <summary>Creates the view when <paramref name="message"/> is a NOTICE.</summary>
    public static bool TryCreate(IrcMessage message, [NotNullWhen(true)] out IrcNotice? notice)
    {
        ArgumentNullException.ThrowIfNull(message);
        notice = message.Command == "NOTICE" && message.Parameters.Count > 0 ? new IrcNotice(message) : null;
        return notice is not null;
    }

    /// <summary>The underlying IRC message with all tags.</summary>
    public IrcMessage Raw { get; }
    /// <summary>The channel login without <c>#</c>; null for global notices addressed to <c>*</c>.</summary>
    public string? Channel => IrcTagReader.Channel(Raw);
    /// <summary>The <c>msg-id</c> tag, for example <c>msg_ratelimit</c> or <c>msg_banned</c>; null for login failures, which Twitch sends without tags.</summary>
    public string? MsgId => IrcTagReader.Text(Raw, "msg-id");
    /// <summary>The human-readable notice text.</summary>
    public string Text => Raw.Parameters.Count > 1 ? Raw.Parameters[^1] : "";
    /// <summary>The <c>target-user-id</c> tag, when present.</summary>
    public string? TargetUserId => IrcTagReader.Text(Raw, "target-user-id");
}

/// <summary>A typed view of a CLEARCHAT: a ban, a timeout or clearing all messages in a channel.</summary>
public sealed class IrcClearChat
{
    private IrcClearChat(IrcMessage raw, string channel)
    {
        Raw = raw;
        Channel = channel;
    }

    /// <summary>Creates the view when <paramref name="message"/> is a CLEARCHAT for a channel.</summary>
    public static bool TryCreate(IrcMessage message, [NotNullWhen(true)] out IrcClearChat? clearChat)
    {
        ArgumentNullException.ThrowIfNull(message);
        clearChat = message.Command == "CLEARCHAT" && IrcTagReader.Channel(message) is { } channel ? new IrcClearChat(message, channel) : null;
        return clearChat is not null;
    }

    /// <summary>The underlying IRC message with all tags.</summary>
    public IrcMessage Raw { get; }
    /// <summary>The channel login without <c>#</c>.</summary>
    public string Channel { get; }
    /// <summary>The banned or timed-out user's login; null when all messages were cleared.</summary>
    public string? TargetLogin => Raw.GetParameter(1) is { Length: > 0 } login ? login : null;
    /// <summary>The <c>target-user-id</c> tag.</summary>
    public string? TargetUserId => IrcTagReader.Text(Raw, "target-user-id");
    /// <summary>The <c>ban-duration</c> tag for timeouts; null for permanent bans and chat clears.</summary>
    public TimeSpan? BanDuration => IrcTagReader.Seconds(Raw, "ban-duration");
    /// <summary>True when the whole chat was cleared rather than one user.</summary>
    public bool IsChatCleared => TargetLogin is null;
    /// <summary>True for a permanent ban of <see cref="TargetLogin"/>.</summary>
    public bool IsPermanentBan => TargetLogin is not null && !Raw.Tags.ContainsKey("ban-duration");
    /// <summary>The <c>room-id</c> tag.</summary>
    public string? RoomId => IrcTagReader.Text(Raw, "room-id");
    /// <summary>The <c>tmi-sent-ts</c> tag.</summary>
    public DateTimeOffset? SentAt => IrcTagReader.UnixMilliseconds(Raw, "tmi-sent-ts");
}

/// <summary>A typed view of a CLEARMSG: a single message was deleted.</summary>
public sealed class IrcClearMessage
{
    private IrcClearMessage(IrcMessage raw, string channel)
    {
        Raw = raw;
        Channel = channel;
    }

    /// <summary>Creates the view when <paramref name="message"/> is a CLEARMSG for a channel.</summary>
    public static bool TryCreate(IrcMessage message, [NotNullWhen(true)] out IrcClearMessage? clearMessage)
    {
        ArgumentNullException.ThrowIfNull(message);
        clearMessage = message.Command == "CLEARMSG" && IrcTagReader.Channel(message) is { } channel ? new IrcClearMessage(message, channel) : null;
        return clearMessage is not null;
    }

    /// <summary>The underlying IRC message with all tags.</summary>
    public IrcMessage Raw { get; }
    /// <summary>The channel login without <c>#</c>.</summary>
    public string Channel { get; }
    /// <summary>The <c>target-msg-id</c> tag: the deleted message.</summary>
    public string? TargetMessageId => IrcTagReader.Text(Raw, "target-msg-id");
    /// <summary>The <c>login</c> tag of the user whose message was deleted.</summary>
    public string? Login => IrcTagReader.Text(Raw, "login");
    /// <summary>The deleted message text.</summary>
    public string Text => Raw.GetParameter(1) ?? "";
    /// <summary>The <c>room-id</c> tag.</summary>
    public string? RoomId => IrcTagReader.Text(Raw, "room-id");
    /// <summary>The <c>tmi-sent-ts</c> tag.</summary>
    public DateTimeOffset? SentAt => IrcTagReader.UnixMilliseconds(Raw, "tmi-sent-ts");
}

/// <summary>A typed view of a received WHISPER. Twitch delivers whispers over IRC, but new code should send them through Helix.</summary>
public sealed class IrcWhisper
{
    private IrcWhisper(IrcMessage raw)
    {
        Raw = raw;
        Badges = IrcTagReader.Badges(raw, "badges");
        Emotes = IrcTagReader.Emotes(raw, "emotes");
    }

    /// <summary>Creates the view when <paramref name="message"/> is a WHISPER with a recipient and text.</summary>
    public static bool TryCreate(IrcMessage message, [NotNullWhen(true)] out IrcWhisper? whisper)
    {
        ArgumentNullException.ThrowIfNull(message);
        whisper = message.Command == "WHISPER" && message.Parameters.Count >= 2 ? new IrcWhisper(message) : null;
        return whisper is not null;
    }

    /// <summary>The underlying IRC message with all tags.</summary>
    public IrcMessage Raw { get; }
    /// <summary>The sender's login from the message prefix.</summary>
    public string? FromLogin => Raw.Nick;
    /// <summary>The recipient's login.</summary>
    public string ToLogin => Raw.Parameters[0];
    /// <summary>The whisper text.</summary>
    public string Text => Raw.Parameters[1];
    /// <summary>The <c>message-id</c> tag.</summary>
    public string? MessageId => IrcTagReader.Text(Raw, "message-id");
    /// <summary>The <c>thread-id</c> tag, formed from both user IDs.</summary>
    public string? ThreadId => IrcTagReader.Text(Raw, "thread-id");
    /// <summary>The <c>user-id</c> tag of the sender.</summary>
    public string? UserId => IrcTagReader.Text(Raw, "user-id");
    /// <summary>The <c>display-name</c> tag of the sender.</summary>
    public string? DisplayName => IrcTagReader.Text(Raw, "display-name");
    /// <summary>The <c>color</c> tag of the sender.</summary>
    public string? Color => IrcTagReader.Text(Raw, "color");
    /// <summary>The <c>badges</c> tag.</summary>
    public IReadOnlyList<IrcBadge> Badges { get; }
    /// <summary>The <c>emotes</c> tag.</summary>
    public IReadOnlyList<IrcEmote> Emotes { get; }
    /// <summary>The deprecated <c>turbo</c> tag.</summary>
    public bool IsTurbo => IrcTagReader.Flag(Raw, "turbo");
    /// <summary>The <c>user-type</c> tag.</summary>
    public string? UserType => IrcTagReader.Text(Raw, "user-type");
}
