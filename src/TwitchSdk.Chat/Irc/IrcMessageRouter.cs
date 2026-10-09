using System.Diagnostics.CodeAnalysis;

namespace TwitchSdk.Chat.Irc;

/// <summary>
/// Routes IRC messages to typed handlers. Register handlers before dispatching; dispatching is then safe to call concurrently.
/// Pass <see cref="DispatchAsync"/> as the message callback of <see cref="TwitchIrcClient.RunAsync"/>.
/// </summary>
public sealed class IrcMessageRouter
{
    private readonly Dictionary<string, Func<IrcMessage, CancellationToken, Task<bool>>> _handlers = new(StringComparer.Ordinal);
    private Func<IrcMessage, CancellationToken, Task>? _fallback;

    /// <summary>Handles PRIVMSG chat messages.</summary>
    public IrcMessageRouter OnChatMessage(Func<IrcChatMessage, CancellationToken, Task> handler)
        => Register<IrcChatMessage>("PRIVMSG", IrcChatMessage.TryCreate, handler);

    /// <summary>Handles USERNOTICE messages such as subscriptions, raids and announcements.</summary>
    public IrcMessageRouter OnUserNotice(Func<IrcUserNotice, CancellationToken, Task> handler)
        => Register<IrcUserNotice>("USERNOTICE", IrcUserNotice.TryCreate, handler);

    /// <summary>Handles NOTICE messages.</summary>
    public IrcMessageRouter OnNotice(Func<IrcNotice, CancellationToken, Task> handler)
        => Register<IrcNotice>("NOTICE", IrcNotice.TryCreate, handler);

    /// <summary>Handles CLEARCHAT messages (bans, timeouts and chat clears).</summary>
    public IrcMessageRouter OnClearChat(Func<IrcClearChat, CancellationToken, Task> handler)
        => Register<IrcClearChat>("CLEARCHAT", IrcClearChat.TryCreate, handler);

    /// <summary>Handles CLEARMSG messages (single message deletions).</summary>
    public IrcMessageRouter OnClearMessage(Func<IrcClearMessage, CancellationToken, Task> handler)
        => Register<IrcClearMessage>("CLEARMSG", IrcClearMessage.TryCreate, handler);

    /// <summary>Handles ROOMSTATE messages.</summary>
    public IrcMessageRouter OnRoomState(Func<IrcRoomState, CancellationToken, Task> handler)
        => Register<IrcRoomState>("ROOMSTATE", IrcRoomState.TryCreate, handler);

    /// <summary>Handles USERSTATE messages.</summary>
    public IrcMessageRouter OnUserState(Func<IrcUserState, CancellationToken, Task> handler)
        => Register<IrcUserState>("USERSTATE", IrcUserState.TryCreate, handler);

    /// <summary>Handles the GLOBALUSERSTATE sent after login.</summary>
    public IrcMessageRouter OnGlobalUserState(Func<IrcGlobalUserState, CancellationToken, Task> handler)
        => Register<IrcGlobalUserState>("GLOBALUSERSTATE", IrcGlobalUserState.TryCreate, handler);

    /// <summary>Handles received WHISPER messages.</summary>
    public IrcMessageRouter OnWhisper(Func<IrcWhisper, CancellationToken, Task> handler)
        => Register<IrcWhisper>("WHISPER", IrcWhisper.TryCreate, handler);

    /// <summary>Handles a raw command without a typed view, for example JOIN, PART or a numeric reply.</summary>
    public IrcMessageRouter OnCommand(string command, Func<IrcMessage, CancellationToken, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(handler);
        return Add(command.ToUpperInvariant(), async (message, ct) =>
        {
            await handler(message, ct).ConfigureAwait(false);
            return true;
        });
    }

    /// <summary>Handles every message no other handler accepted, including commands whose typed view could not be created.</summary>
    public IrcMessageRouter OnUnhandled(Func<IrcMessage, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (_fallback is not null) throw new InvalidOperationException("An unhandled-message handler is already registered.");
        _fallback = handler;
        return this;
    }

    /// <summary>Dispatches a message. Returns false when no handler applies.</summary>
    public async Task<bool> DispatchAsync(IrcMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (_handlers.TryGetValue(message.Command, out var handler) && await handler(message, cancellationToken).ConfigureAwait(false)) return true;
        if (_fallback is null) return false;
        await _fallback(message, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private delegate bool ViewFactory<TView>(IrcMessage message, [NotNullWhen(true)] out TView? view);

    private IrcMessageRouter Register<TView>(string command, ViewFactory<TView> factory, Func<TView, CancellationToken, Task> handler) where TView : class
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Add(command, async (message, ct) =>
        {
            if (!factory(message, out var view)) return false;
            await handler(view, ct).ConfigureAwait(false);
            return true;
        });
    }

    private IrcMessageRouter Add(string command, Func<IrcMessage, CancellationToken, Task<bool>> handler)
    {
        if (!_handlers.TryAdd(command, handler)) throw new InvalidOperationException($"A handler for {command} is already registered.");
        return this;
    }
}
