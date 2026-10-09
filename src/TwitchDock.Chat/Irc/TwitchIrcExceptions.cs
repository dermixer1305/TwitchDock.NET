namespace TwitchDock.Chat.Irc;

/// <summary>A Twitch IRC protocol failure, for example a rejected capability or a send on a failed connection.</summary>
public class TwitchIrcException : Exception
{
    /// <summary>Creates the exception.</summary>
    public TwitchIrcException(string message) : base(message) { }

    /// <summary>Creates the exception with the underlying cause.</summary>
    public TwitchIrcException(string message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// Twitch rejected the IRC login, for example with <c>Login authentication failed</c> or <c>Improperly formatted auth</c>.
/// The client does not retry; obtain a valid user token with the chat scopes and start again. The message never contains the token.
/// </summary>
public sealed class TwitchIrcAuthenticationException : TwitchIrcException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The error description.</param>
    /// <param name="serverNotice">The NOTICE text Twitch sent, when available.</param>
    public TwitchIrcAuthenticationException(string message, string? serverNotice = null) : base(message) => ServerNotice = serverNotice;

    /// <summary>The NOTICE text Twitch sent, when available.</summary>
    public string? ServerNotice { get; }
}
