using System.Collections.ObjectModel;
using System.Globalization;

namespace TwitchDock.Chat.Irc;

/// <summary>Lenient readers for Twitch tag values. Malformed values read as absent instead of throwing.</summary>
internal static class IrcTagReader
{
    private static readonly IReadOnlyDictionary<string, string> NoEntries = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));
    private const long MinUnixMilliseconds = -62_135_596_800_000;
    private const long MaxUnixMilliseconds = 253_402_300_799_999;

    /// <summary>Returns the value, or null when the tag is absent or empty.</summary>
    public static string? Text(IrcMessage message, string key) => message.Tags.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    public static bool Flag(IrcMessage message, string key) => message.Tags.TryGetValue(key, out var value) && IsTrue(value);

    /// <summary>Null when the tag is absent or not a recognized Boolean.</summary>
    public static bool? OptionalFlag(IrcMessage message, string key)
    {
        if (!message.Tags.TryGetValue(key, out var value)) return null;
        if (IsTrue(value)) return true;
        return value is "0" or "false" ? false : null;
    }

    public static int? Int32(IrcMessage message, string key)
        => message.Tags.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) ? number : null;

    public static DateTimeOffset? UnixMilliseconds(IrcMessage message, string key)
        => message.Tags.TryGetValue(key, out var value) && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ms)
            && ms is >= MinUnixMilliseconds and <= MaxUnixMilliseconds ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;

    public static TimeSpan? Seconds(IrcMessage message, string key)
        => Int32(message, key) is { } seconds and >= 0 ? TimeSpan.FromSeconds(seconds) : null;

    /// <summary>Splits a comma-separated list such as <c>emote-sets</c>, skipping empty items.</summary>
    public static IReadOnlyList<string> List(IrcMessage message, string key)
        => Text(message, key) is { } value ? Array.AsReadOnly(value.Split(',', StringSplitOptions.RemoveEmptyEntries)) : Array.Empty<string>();

    /// <summary>Parses <c>set/version,set/version</c> in wire order. Items without a set ID are skipped.</summary>
    public static IReadOnlyList<IrcBadge> Badges(IrcMessage message, string key)
    {
        if (Text(message, key) is not { } value) return Array.Empty<IrcBadge>();
        var badges = new List<IrcBadge>();
        foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var slash = item.IndexOf('/');
            if (slash == 0) continue;
            badges.Add(slash < 0 ? new IrcBadge(item, "") : new IrcBadge(item[..slash], item[(slash + 1)..]));
        }
        return badges.AsReadOnly();
    }

    /// <summary>Parses <c>badge-info</c>-style values into a set ID to info map. Later duplicates win.</summary>
    public static IReadOnlyDictionary<string, string> BadgeInfo(IrcMessage message, string key)
    {
        var badges = Badges(message, key);
        if (badges.Count == 0) return NoEntries;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var badge in badges) map[badge.SetId] = badge.Version;
        return new ReadOnlyDictionary<string, string>(map);
    }

    /// <summary>Parses <c>id:start-end,start-end/id:start-end</c>. Malformed ranges are skipped.</summary>
    public static IReadOnlyList<IrcEmote> Emotes(IrcMessage message, string key)
    {
        if (Text(message, key) is not { } value) return Array.Empty<IrcEmote>();
        var emotes = new List<IrcEmote>();
        foreach (var group in value.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = group.LastIndexOf(':');
            if (colon <= 0) continue;
            var id = group[..colon];
            foreach (var range in group[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var dash = range.IndexOf('-');
                if (dash <= 0
                    || !int.TryParse(range.AsSpan(0, dash), NumberStyles.None, CultureInfo.InvariantCulture, out var start)
                    || !int.TryParse(range.AsSpan(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var end)
                    || end < start) continue;
                emotes.Add(new IrcEmote(id, start, end));
            }
        }
        return emotes.AsReadOnly();
    }

    /// <summary>Returns the channel login from a <c>#channel</c> parameter, or null for other values such as <c>*</c>.</summary>
    public static string? Channel(IrcMessage message, int index = 0)
        => message.GetParameter(index) is { Length: > 1 } value && value[0] == '#' ? value[1..] : null;

    public static IrcSharedChatSource? SharedChatSource(IrcMessage message)
        => Text(message, "source-room-id") is { } roomId
            ? new IrcSharedChatSource
            {
                RoomId = roomId,
                MessageId = Text(message, "source-id"),
                MsgId = Text(message, "source-msg-id"),
                Badges = Badges(message, "source-badges"),
                BadgeInfo = BadgeInfo(message, "source-badge-info"),
                IsSourceOnly = OptionalFlag(message, "source-only")
            }
            : null;

    private static bool IsTrue(string value) => value is "1" or "true";
}
