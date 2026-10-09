using System.Diagnostics.CodeAnalysis;

namespace TwitchDock.Chat.Irc;

/// <summary>A chat badge from a <c>badges</c> tag, for example <c>subscriber/12</c>.</summary>
/// <param name="SetId">The badge set, for example <c>subscriber</c>, <c>moderator</c> or <c>broadcaster</c>.</param>
/// <param name="Version">The badge version within the set; empty when the wire value had none.</param>
public sealed record IrcBadge(string SetId, string Version);

/// <summary>An emote occurrence from an <c>emotes</c> tag.</summary>
/// <param name="Id">The emote ID.</param>
/// <param name="Start">Zero-based index of the first character, counted in Unicode code points as Twitch does.</param>
/// <param name="End">Zero-based index of the last character (inclusive), counted in Unicode code points.</param>
public sealed record IrcEmote(string Id, int Start, int End)
{
    /// <summary>
    /// Returns the emote text from <paramref name="message"/>, translating code point positions to UTF-16 indices
    /// so emoji before the emote do not shift the result. Returns null when the range lies outside the text.
    /// </summary>
    public string? GetText(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        int codePoint = 0, startIndex = -1;
        for (var i = 0; i < message.Length; codePoint++)
        {
            if (codePoint == Start) startIndex = i;
            var width = char.IsHighSurrogate(message[i]) && i + 1 < message.Length && char.IsLowSurrogate(message[i + 1]) ? 2 : 1;
            if (codePoint == End) return startIndex < 0 ? null : message[startIndex..(i + width)];
            i += width;
        }
        return null;
    }
}

/// <summary>Reply metadata from the <c>reply-parent-*</c> and <c>reply-thread-parent-*</c> tags.</summary>
public sealed class IrcReply
{
    /// <summary>The <c>reply-parent-msg-id</c> tag: the message being replied to.</summary>
    public required string ParentMessageId { get; init; }
    /// <summary>The <c>reply-parent-user-id</c> tag.</summary>
    public string? ParentUserId { get; init; }
    /// <summary>The <c>reply-parent-user-login</c> tag.</summary>
    public string? ParentUserLogin { get; init; }
    /// <summary>The <c>reply-parent-display-name</c> tag.</summary>
    public string? ParentDisplayName { get; init; }
    /// <summary>The unescaped <c>reply-parent-msg-body</c> tag.</summary>
    public string? ParentMessageBody { get; init; }
    /// <summary>The <c>reply-thread-parent-msg-id</c> tag: the top-level message of the thread.</summary>
    public string? ThreadParentMessageId { get; init; }
    /// <summary>The <c>reply-thread-parent-user-id</c> tag.</summary>
    public string? ThreadParentUserId { get; init; }
    /// <summary>The <c>reply-thread-parent-user-login</c> tag.</summary>
    public string? ThreadParentUserLogin { get; init; }
    /// <summary>The <c>reply-thread-parent-display-name</c> tag.</summary>
    public string? ThreadParentDisplayName { get; init; }
}

/// <summary>Shared chat metadata from the <c>source-*</c> tags, present when a message originated in another channel of a shared chat session.</summary>
public sealed class IrcSharedChatSource
{
    /// <summary>The <c>source-room-id</c> tag: the channel ID where the message was sent.</summary>
    public required string RoomId { get; init; }
    /// <summary>The <c>source-id</c> tag: the message ID in the source channel.</summary>
    public string? MessageId { get; init; }
    /// <summary>The <c>source-msg-id</c> tag of shared chat USERNOTICE messages, for example <c>sub</c> or <c>raid</c>.</summary>
    public string? MsgId { get; init; }
    /// <summary>The <c>source-badges</c> tag: the sender's badges in the source channel.</summary>
    public IReadOnlyList<IrcBadge> Badges { get; init; } = [];
    /// <summary>The <c>source-badge-info</c> tag.</summary>
    public IReadOnlyDictionary<string, string> BadgeInfo { get; init; } = new Dictionary<string, string>();
    /// <summary>The <c>source-only</c> tag; null when absent.</summary>
    public bool? IsSourceOnly { get; init; }
}

/// <summary>
/// A typed view of a PRIVMSG. Optional tags that are absent or empty read as null or false.
/// Tags this view does not model remain available through <see cref="Raw"/>.
/// </summary>
public sealed class IrcChatMessage
{
    private const string ActionPrefix = "\u0001ACTION ";

    private IrcChatMessage(IrcMessage raw, string channel, string text, bool isAction)
    {
        Raw = raw;
        Channel = channel;
        Text = text;
        IsAction = isAction;
        Badges = IrcTagReader.Badges(raw, "badges");
        BadgeInfo = IrcTagReader.BadgeInfo(raw, "badge-info");
        Emotes = IrcTagReader.Emotes(raw, "emotes");
        Source = IrcTagReader.SharedChatSource(raw);
        if (IrcTagReader.Text(raw, "reply-parent-msg-id") is { } parentId)
        {
            Reply = new IrcReply
            {
                ParentMessageId = parentId,
                ParentUserId = IrcTagReader.Text(raw, "reply-parent-user-id"),
                ParentUserLogin = IrcTagReader.Text(raw, "reply-parent-user-login"),
                ParentDisplayName = IrcTagReader.Text(raw, "reply-parent-display-name"),
                ParentMessageBody = IrcTagReader.Text(raw, "reply-parent-msg-body"),
                ThreadParentMessageId = IrcTagReader.Text(raw, "reply-thread-parent-msg-id"),
                ThreadParentUserId = IrcTagReader.Text(raw, "reply-thread-parent-user-id"),
                ThreadParentUserLogin = IrcTagReader.Text(raw, "reply-thread-parent-user-login"),
                ThreadParentDisplayName = IrcTagReader.Text(raw, "reply-thread-parent-display-name")
            };
        }
    }

    /// <summary>Creates the view when <paramref name="message"/> is a PRIVMSG to a channel.</summary>
    public static bool TryCreate(IrcMessage message, [NotNullWhen(true)] out IrcChatMessage? chatMessage)
    {
        ArgumentNullException.ThrowIfNull(message);
        chatMessage = null;
        if (message.Command != "PRIVMSG" || message.Parameters.Count < 2 || IrcTagReader.Channel(message) is not { } channel) return false;
        var text = message.Parameters[1];
        var isAction = text.StartsWith(ActionPrefix, StringComparison.Ordinal);
        if (isAction) text = text[ActionPrefix.Length..].TrimEnd('\u0001');
        chatMessage = new IrcChatMessage(message, channel, text, isAction);
        return true;
    }

    /// <summary>The underlying IRC message with all tags.</summary>
    public IrcMessage Raw { get; }
    /// <summary>The channel login without <c>#</c>.</summary>
    public string Channel { get; }
    /// <summary>The message text. For <c>/me</c> messages the CTCP ACTION wrapper is removed.</summary>
    public string Text { get; }
    /// <summary>True for <c>/me</c> messages sent as <c>\u0001ACTION text\u0001</c>.</summary>
    public bool IsAction { get; }
    /// <summary>The <c>id</c> tag: this message's ID, used for replies and deletions.</summary>
    public string? MessageId => IrcTagReader.Text(Raw, "id");
    /// <summary>The <c>user-id</c> tag.</summary>
    public string? UserId => IrcTagReader.Text(Raw, "user-id");
    /// <summary>The sender's login from the message prefix.</summary>
    public string? UserLogin => Raw.Nick;
    /// <summary>The <c>display-name</c> tag.</summary>
    public string? DisplayName => IrcTagReader.Text(Raw, "display-name");
    /// <summary>The <c>color</c> tag as <c>#RRGGBB</c>; null when the user has not set a color.</summary>
    public string? Color => IrcTagReader.Text(Raw, "color");
    /// <summary>The <c>badges</c> tag in wire order.</summary>
    public IReadOnlyList<IrcBadge> Badges { get; }
    /// <summary>The <c>badge-info</c> tag, for example <c>subscriber</c> to the exact subscription months.</summary>
    public IReadOnlyDictionary<string, string> BadgeInfo { get; }
    /// <summary>The <c>emotes</c> tag. Positions refer to <see cref="Text"/> in code points.</summary>
    public IReadOnlyList<IrcEmote> Emotes { get; }
    /// <summary>The <c>bits</c> tag for cheers.</summary>
    public int? Bits => IrcTagReader.Int32(Raw, "bits");
    /// <summary>The <c>first-msg</c> tag: the user's first message in the channel.</summary>
    public bool IsFirstMessage => IrcTagReader.Flag(Raw, "first-msg");
    /// <summary>The <c>returning-chatter</c> tag.</summary>
    public bool IsReturningChatter => IrcTagReader.Flag(Raw, "returning-chatter");
    /// <summary>The <c>mod</c> tag.</summary>
    public bool IsModerator => IrcTagReader.Flag(Raw, "mod");
    /// <summary>The <c>subscriber</c> tag.</summary>
    public bool IsSubscriber => IrcTagReader.Flag(Raw, "subscriber");
    /// <summary>The <c>vip</c> tag, which Twitch includes only for VIPs, or a <c>vip</c> badge.</summary>
    public bool IsVip => Raw.Tags.ContainsKey("vip") || HasBadge("vip");
    /// <summary>True when the sender has the <c>broadcaster</c> badge.</summary>
    public bool IsBroadcaster => HasBadge("broadcaster");
    /// <summary>The deprecated <c>turbo</c> tag.</summary>
    public bool IsTurbo => IrcTagReader.Flag(Raw, "turbo");
    /// <summary>The <c>emote-only</c> tag.</summary>
    public bool IsEmoteOnly => IrcTagReader.Flag(Raw, "emote-only");
    /// <summary>The <c>user-type</c> tag: <c>admin</c>, <c>global_mod</c> or <c>staff</c>; null for normal users.</summary>
    public string? UserType => IrcTagReader.Text(Raw, "user-type");
    /// <summary>The <c>room-id</c> tag: the channel's user ID.</summary>
    public string? RoomId => IrcTagReader.Text(Raw, "room-id");
    /// <summary>The <c>tmi-sent-ts</c> tag.</summary>
    public DateTimeOffset? SentAt => IrcTagReader.UnixMilliseconds(Raw, "tmi-sent-ts");
    /// <summary>The <c>custom-reward-id</c> tag for channel point redemptions with a message.</summary>
    public string? CustomRewardId => IrcTagReader.Text(Raw, "custom-reward-id");
    /// <summary>The <c>msg-id</c> tag, for example <c>highlighted-message</c>; kept as a string so new values remain readable.</summary>
    public string? MsgId => IrcTagReader.Text(Raw, "msg-id");
    /// <summary>The <c>client-nonce</c> tag echoed for messages sent with one.</summary>
    public string? ClientNonce => IrcTagReader.Text(Raw, "client-nonce");
    /// <summary>Reply metadata when the message replies to another message.</summary>
    public IrcReply? Reply { get; }
    /// <summary>Shared chat metadata when the message originated in another channel of a shared chat session.</summary>
    public IrcSharedChatSource? Source { get; }

    /// <summary>Returns true when <see cref="Badges"/> contains the set ID.</summary>
    public bool HasBadge(string setId) => Badges.Any(badge => string.Equals(badge.SetId, setId, StringComparison.Ordinal));
}
