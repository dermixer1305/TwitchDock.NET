using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace TwitchDock.Chat.Irc;

/// <summary>
/// An immutable IRCv3 message: tags, optional prefix, command and parameters.
/// Parsing never throws for malformed input through <see cref="TryParse"/>; outgoing messages are validated so a value cannot inject another line.
/// </summary>
public sealed class IrcMessage
{
    /// <summary>The longest accepted line in UTF-16 code units, excluding CR LF. IRCv3 allows 8191 bytes of tags plus 512 bytes for the rest.</summary>
    public const int MaxLineLength = 16 * 1024;

    private static readonly IReadOnlyDictionary<string, string> NoTags = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));
    private string? _line;

    /// <summary>Creates an outgoing message.</summary>
    /// <param name="command">A command of letters (normalized to upper case) or a three-digit numeric.</param>
    /// <param name="parameters">Parameters. Only the last may be empty, contain spaces or start with a colon; none may contain CR, LF or NUL.</param>
    /// <param name="tags">Message tags; values are escaped on serialization. Later duplicates win.</param>
    /// <param name="prefix">Optional prefix without the leading colon. Clients normally omit it.</param>
    /// <param name="lastParameterIsTrailing">Writes the last parameter in trailing form (<c>:text</c>) even when not required.</param>
    /// <exception cref="ArgumentException">A value cannot be represented on a single IRC line.</exception>
    public IrcMessage(string command, IEnumerable<string>? parameters = null, IEnumerable<KeyValuePair<string, string>>? tags = null, string? prefix = null,
        bool lastParameterIsTrailing = false)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsValidCommand(command)) throw new ArgumentException("An IRC command must be letters or a three-digit numeric.", nameof(command));
        if (prefix is not null && (prefix.Length == 0 || prefix.AsSpan().IndexOfAny(" \r\n\0") >= 0))
            throw new ArgumentException("An IRC prefix must be nonempty and must not contain spaces, CR, LF or NUL.", nameof(prefix));
        var list = parameters?.ToList() ?? [];
        for (var i = 0; i < list.Count; i++)
        {
            var value = list[i] ?? throw new ArgumentException("IRC parameters must not be null.", nameof(parameters));
            if (value.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0) throw new ArgumentException("IRC parameters must not contain CR, LF or NUL.", nameof(parameters));
            if (i < list.Count - 1 && (value.Length == 0 || value[0] == ':' || value.Contains(' ')))
                throw new ArgumentException("Only the last IRC parameter may be empty, start with ':' or contain spaces.", nameof(parameters));
        }
        Dictionary<string, string>? tagMap = null;
        foreach (var (key, value) in tags ?? [])
        {
            if (key is null || !IsValidTagKey(key)) throw new ArgumentException("Invalid IRC tag key.", nameof(tags));
            if (value is null || value.Contains('\0')) throw new ArgumentException("IRC tag values must not be null or contain NUL.", nameof(tags));
            (tagMap ??= new(StringComparer.Ordinal))[key] = value;
        }
        Tags = tagMap is null ? NoTags : new ReadOnlyDictionary<string, string>(tagMap);
        Prefix = prefix;
        Command = command.ToUpperInvariant();
        Parameters = list.AsReadOnly();
        HasTrailingParameter = list.Count > 0 && (lastParameterIsTrailing || RequiresTrailing(list[^1]));
        (Nick, User, Host) = SplitPrefix(prefix);
        if (Serialize().Length > MaxLineLength) throw new ArgumentException($"The IRC line exceeds {MaxLineLength} characters.", nameof(parameters));
    }

    private IrcMessage(Dictionary<string, string>? tags, string? prefix, string command, List<string> parameters, bool hasTrailing)
    {
        Tags = tags is null || tags.Count == 0 ? NoTags : new ReadOnlyDictionary<string, string>(tags);
        Prefix = prefix;
        Command = command;
        Parameters = parameters.AsReadOnly();
        HasTrailingParameter = hasTrailing || (parameters.Count > 0 && RequiresTrailing(parameters[^1]));
        (Nick, User, Host) = SplitPrefix(prefix);
    }

    /// <summary>Unescaped tag values keyed by tag name. A tag without a value maps to an empty string.</summary>
    public IReadOnlyDictionary<string, string> Tags { get; }

    /// <summary>The raw prefix without the leading colon, for example <c>nick!user@host</c> or <c>tmi.twitch.tv</c>.</summary>
    public string? Prefix { get; }

    /// <summary>The nickname part of the prefix; for Twitch users this is the login name. Null for server prefixes.</summary>
    public string? Nick { get; }

    /// <summary>The user part of the prefix, between <c>!</c> and <c>@</c>.</summary>
    public string? User { get; }

    /// <summary>The host part of the prefix, or the server name when the prefix has no nickname.</summary>
    public string? Host { get; }

    /// <summary>The command in upper case, or a three-digit numeric reply.</summary>
    public string Command { get; }

    /// <summary>All parameters, including the trailing parameter as the last element.</summary>
    public IReadOnlyList<string> Parameters { get; }

    /// <summary>Whether the last parameter is written (or was received) in trailing form.</summary>
    public bool HasTrailingParameter { get; }

    /// <summary>Returns a tag value, or null when the tag is absent.</summary>
    public string? GetTag(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Tags.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>Returns the parameter at <paramref name="index"/>, or null when it does not exist.</summary>
    public string? GetParameter(int index) => index >= 0 && index < Parameters.Count ? Parameters[index] : null;

    /// <summary>Parses one line. A trailing CR LF is ignored.</summary>
    /// <exception cref="FormatException">The line is not a valid IRC message.</exception>
    public static IrcMessage Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return TryParse(line, out var message) ? message : throw new FormatException("The line is not a valid IRC message.");
    }

    /// <summary>Parses one line without throwing. Returns false for empty, oversized or malformed lines and for lines with embedded CR, LF or NUL.</summary>
    public static bool TryParse([NotNullWhen(true)] string? line, [NotNullWhen(true)] out IrcMessage? message)
    {
        message = null;
        if (line is null) return false;
        var end = line.Length;
        while (end > 0 && line[end - 1] is '\r' or '\n') end--;
        if (end == 0 || end > MaxLineLength) return false;
        var span = line.AsSpan(0, end);
        if (span.IndexOfAny('\r', '\n', '\0') >= 0) return false;

        var pos = 0;
        Dictionary<string, string>? tags = null;
        if (span[0] == '@')
        {
            var space = span.IndexOf(' ');
            if (space < 0 || !TryParseTags(span[1..space], out tags)) return false;
            pos = SkipSpaces(span, space);
        }
        string? prefix = null;
        if (pos < span.Length && span[pos] == ':')
        {
            var space = span[(pos + 1)..].IndexOf(' ');
            if (space <= 0) return false;
            prefix = span.Slice(pos + 1, space).ToString();
            pos = SkipSpaces(span, pos + 1 + space);
        }
        var commandLength = span[pos..].IndexOf(' ');
        var commandEnd = commandLength < 0 ? span.Length : pos + commandLength;
        var command = span[pos..commandEnd];
        if (!IsValidCommand(command)) return false;

        var parameters = new List<string>();
        var trailing = false;
        pos = commandEnd;
        while ((pos = SkipSpaces(span, pos)) < span.Length)
        {
            if (span[pos] == ':')
            {
                parameters.Add(span[(pos + 1)..].ToString());
                trailing = true;
                break;
            }
            var length = span[pos..].IndexOf(' ');
            var stop = length < 0 ? span.Length : pos + length;
            parameters.Add(span[pos..stop].ToString());
            pos = stop;
        }
        message = new IrcMessage(tags, prefix, command.ToString().ToUpperInvariant(), parameters, trailing);
        return true;
    }

    /// <summary>Serializes the message without the terminating CR LF. Tag values are escaped.</summary>
    public string Serialize() => _line ??= BuildLine(redact: false);

    /// <summary>Returns the serialized line, with PASS parameters redacted so credentials never reach logs.</summary>
    public override string ToString() => Command == "PASS" ? BuildLine(redact: true) : Serialize();

    private string BuildLine(bool redact)
    {
        var builder = new StringBuilder();
        if (Tags.Count > 0)
        {
            builder.Append('@');
            var first = true;
            foreach (var (key, value) in Tags)
            {
                if (!first) builder.Append(';');
                first = false;
                builder.Append(key);
                if (value.Length > 0) AppendEscaped(builder.Append('='), value);
            }
            builder.Append(' ');
        }
        if (Prefix is not null) builder.Append(':').Append(Prefix).Append(' ');
        builder.Append(Command);
        for (var i = 0; i < Parameters.Count; i++)
        {
            builder.Append(' ');
            if (i == Parameters.Count - 1 && HasTrailingParameter) builder.Append(':');
            builder.Append(redact ? "***" : Parameters[i]);
        }
        return builder.ToString();
    }

    /// <summary>Escapes a tag value: <c>;</c>, space, backslash, CR and LF.</summary>
    public static string EscapeTagValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.AsSpan().IndexOfAny("; \\\r\n") < 0 ? value : AppendEscaped(new StringBuilder(value.Length + 8), value).ToString();
    }

    /// <summary>Unescapes a raw tag value. Unknown escapes keep the escaped character and a trailing lone backslash is dropped.</summary>
    public static string UnescapeTagValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Unescape(value);
    }

    private static StringBuilder AppendEscaped(StringBuilder builder, string value)
    {
        foreach (var c in value)
        {
            switch (c)
            {
                case ';': builder.Append("\\:"); break;
                case ' ': builder.Append("\\s"); break;
                case '\\': builder.Append("\\\\"); break;
                case '\r': builder.Append("\\r"); break;
                case '\n': builder.Append("\\n"); break;
                default: builder.Append(c); break;
            }
        }
        return builder;
    }

    private static string Unescape(ReadOnlySpan<char> value)
    {
        var index = value.IndexOf('\\');
        if (index < 0) return value.ToString();
        var builder = new StringBuilder(value.Length);
        builder.Append(value[..index]);
        for (var i = index; i < value.Length; i++)
        {
            var c = value[i];
            if (c != '\\')
            {
                builder.Append(c);
                continue;
            }
            if (++i >= value.Length) break;
            builder.Append(value[i] switch { ':' => ';', 's' => ' ', '\\' => '\\', 'r' => '\r', 'n' => '\n', var other => other });
        }
        return builder.ToString();
    }

    private static bool TryParseTags(ReadOnlySpan<char> raw, out Dictionary<string, string>? tags)
    {
        tags = null;
        while (true)
        {
            var separator = raw.IndexOf(';');
            var item = separator < 0 ? raw : raw[..separator];
            if (!item.IsEmpty)
            {
                var equals = item.IndexOf('=');
                var key = equals < 0 ? item : item[..equals];
                if (!IsValidTagKey(key)) return false;
                (tags ??= new(StringComparer.Ordinal))[key.ToString()] = equals < 0 ? "" : Unescape(item[(equals + 1)..]);
            }
            if (separator < 0) return true;
            raw = raw[(separator + 1)..];
        }
    }

    private static int SkipSpaces(ReadOnlySpan<char> span, int pos)
    {
        while (pos < span.Length && span[pos] == ' ') pos++;
        return pos;
    }

    private static bool RequiresTrailing(string value) => value.Length == 0 || value[0] == ':' || value.Contains(' ');

    private static bool IsValidCommand(ReadOnlySpan<char> command)
    {
        if (command.IsEmpty) return false;
        if (char.IsAsciiDigit(command[0])) return command.Length == 3 && char.IsAsciiDigit(command[1]) && char.IsAsciiDigit(command[2]);
        foreach (var c in command) if (!char.IsAsciiLetter(c)) return false;
        return true;
    }

    private static bool IsValidTagKey(ReadOnlySpan<char> key)
    {
        if (!key.IsEmpty && key[0] == '+') key = key[1..];
        if (key.IsEmpty) return false;
        foreach (var c in key) if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '/' or '_')) return false;
        return true;
    }

    private static (string? Nick, string? User, string? Host) SplitPrefix(string? prefix)
    {
        if (prefix is null) return (null, null, null);
        var bang = prefix.IndexOf('!');
        var at = prefix.IndexOf('@');
        if (bang < 0 && at < 0) return prefix.Contains('.') ? (null, null, prefix) : (prefix, null, null);
        var nickEnd = bang < 0 ? at : at < 0 ? bang : Math.Min(bang, at);
        var nick = prefix[..nickEnd];
        string? user = null;
        if (bang == nickEnd) user = at > bang ? prefix[(bang + 1)..at] : prefix[(bang + 1)..];
        var host = at >= 0 && at >= nickEnd && (bang < 0 || at > bang) ? prefix[(at + 1)..] : null;
        return (nick.Length == 0 ? null : nick, user, host);
    }
}
