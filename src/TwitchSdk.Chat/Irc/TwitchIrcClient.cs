using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TwitchSdk.Core;

namespace TwitchSdk.Chat.Irc;

/// <summary>
/// Twitch chat over IRCv3, an alternative to the recommended Helix + EventSub chat path.
/// <see cref="RunAsync"/> owns one connection at a time: it logs in, rejoins channels, answers PING, sends keepalive PINGs,
/// follows RECONNECT and reconnects dropped connections with bounded exponential backoff.
/// Message callbacks run sequentially and must return promptly. Joining, parting and sending are thread-safe and may be called from callbacks.
/// </summary>
public sealed class TwitchIrcClient
{
    /// <summary>The longest chat message Twitch accepts, in Unicode code points.</summary>
    public const int MaxMessageLength = 500;
    private const string PingLine = "PING :tmi.twitch.tv";
    private static readonly TwitchAuthorizationRequirement ReadRequirement = new([TwitchScopes.ChatRead]);
    private static readonly TwitchAuthorizationRequirement EditRequirement = new([TwitchScopes.ChatEdit]);

    private readonly IAccessTokenProvider _tokens;
    private readonly TwitchIrcOptions _options;
    private readonly string _login;
    private readonly string? _capabilityRequest;
    private readonly Func<IIrcConnection> _factory;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly IrcSlidingWindowRateLimiter _messageLimiter;
    private readonly IrcSlidingWindowRateLimiter _joinLimiter;
    private readonly IrcSlidingWindowRateLimiter _authLimiter;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly HashSet<string> _channels = new(StringComparer.Ordinal);
    private Session? _session;
    private int _running;

    /// <param name="tokens">Supplies the user access token. It needs <c>chat:read</c>, and <c>chat:edit</c> to send; known scopes are checked before use.</param>
    /// <param name="options">Login, endpoint, capabilities, rate limits and timing.</param>
    /// <param name="connectionFactory">Creates a transport per connection attempt. Defaults to <see cref="WebSocketIrcConnection"/>, or <see cref="TcpIrcConnection"/> for irc:// and ircs:// endpoints.</param>
    /// <param name="timeProvider">The clock for keepalive, backoff, timeouts and rate limits.</param>
    /// <param name="logger">Receives connection state changes. Lines and credentials are never logged.</param>
    public TwitchIrcClient(IAccessTokenProvider tokens, TwitchIrcOptions options, Func<IIrcConnection>? connectionFactory = null, TimeProvider? timeProvider = null,
        ILogger<TwitchIrcClient>? logger = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        options.Validate();
        _login = NormalizeLogin(options.Login);
        // Snapshot the request so later changes to a caller-owned list cannot alter the handshake.
        _capabilityRequest = options.Capabilities.Count == 0 ? null
            : new IrcMessage("CAP", ["REQ", string.Join(' ', options.Capabilities)], lastParameterIsTrailing: true).Serialize();
        var tcp = options.Endpoint.Scheme is "irc" or "ircs";
        _factory = connectionFactory ?? (() => tcp ? new TcpIrcConnection() : new WebSocketIrcConnection());
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<TwitchIrcClient>.Instance;
        _messageLimiter = new(options.MessageRateLimit, _time);
        _joinLimiter = new(options.JoinRateLimit, _time);
        _authLimiter = new(options.AuthenticationRateLimit, _time);
    }

    /// <summary>True while a logged-in connection is active.</summary>
    public bool IsConnected => Volatile.Read(ref _session) is not null;

    /// <summary>A snapshot of the channels the client keeps joined across reconnects.</summary>
    public IReadOnlyCollection<string> JoinedChannels
    {
        get { lock (_channels) return _channels.ToArray(); }
    }

    /// <summary>
    /// Connects and processes messages until cancelled. Every message except PING and PONG is passed to <paramref name="onMessage"/>,
    /// starting with the login replies (CAP ACK and the <c>001</c> welcome) of each connection; RECONNECT is passed on before reconnecting.
    /// Malformed lines are skipped.
    /// </summary>
    /// <param name="onMessage">Handles messages sequentially. Exceptions stop the client and propagate to the caller.</param>
    /// <param name="cancellationToken">Stops the client.</param>
    /// <exception cref="TwitchIrcAuthenticationException">Twitch rejected the login, also after one token refresh.</exception>
    /// <exception cref="TwitchAuthorizationException">The token is an app token or lacks <c>chat:read</c>.</exception>
    /// <exception cref="TwitchIrcException">Twitch rejected a requested capability.</exception>
    public async Task RunAsync(Func<IrcMessage, CancellationToken, Task> onMessage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onMessage);
        if (Interlocked.Exchange(ref _running, 1) != 0) throw new InvalidOperationException("This IRC client is already running.");
        var failures = 0;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (Session Session, IReadOnlyList<IrcMessage> Replies) login;
                try
                {
                    login = await LoginAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsConnectionFailure(ex, cancellationToken))
                {
                    var delay = Backoff(++failures);
                    _logger.LogWarning(ex, "Twitch IRC connection failed; retrying in {Delay}.", delay);
                    await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                failures = 0;
                if (await RunSessionAsync(login.Session, login.Replies, onMessage, cancellationToken).ConfigureAwait(false))
                {
                    _logger.LogInformation("Twitch IRC requested a reconnect.");
                    continue;
                }
                var retry = Backoff(++failures);
                _logger.LogWarning("Twitch IRC connection lost; reconnecting in {Delay}.", retry);
                await Task.Delay(retry, _time, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (CallbackException ex)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
            throw;
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    /// <summary>
    /// Joins a channel and keeps it joined across reconnects. JOIN is rate limited; while disconnected the channel is joined after the next login.
    /// Joining a channel that is already joined does nothing.
    /// </summary>
    /// <param name="channel">The channel login, with or without <c>#</c>; case-insensitive.</param>
    /// <param name="cancellationToken">Cancels a rate-limit wait; the channel is then not joined.</param>
    public async Task JoinAsync(string channel, CancellationToken cancellationToken = default)
    {
        var name = NormalizeChannelName(channel);
        lock (_channels) if (!_channels.Add(name)) return;
        if (Volatile.Read(ref _session) is null) return;
        try
        {
            await _joinLimiter.AcquireAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (_channels) _channels.Remove(name);
            throw;
        }
        lock (_channels) if (!_channels.Contains(name)) return;
        if (Volatile.Read(ref _session) is not { } session) return;
        // A failed connection is reported by RunAsync; the channel is rejoined after the reconnect.
        await TrySendAsync(session, "JOIN #" + name, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Leaves a channel and stops rejoining it. Does nothing when the channel is not joined.</summary>
    /// <param name="channel">The channel login, with or without <c>#</c>; case-insensitive.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    public async Task PartAsync(string channel, CancellationToken cancellationToken = default)
    {
        var name = NormalizeChannelName(channel);
        lock (_channels) if (!_channels.Remove(name)) return;
        if (Volatile.Read(ref _session) is { } session) await TrySendAsync(session, "PART #" + name, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends a chat message, waiting for the message rate limit when necessary.</summary>
    /// <param name="channel">The channel login, with or without <c>#</c>; case-insensitive.</param>
    /// <param name="message">1 to 500 Unicode code points without CR, LF or NUL. A leading <c>/me </c> is not translated.</param>
    /// <param name="replyParentMessageId">The ID of the message to reply to, sent as the <c>reply-parent-msg-id</c> tag.</param>
    /// <param name="cancellationToken">Cancels the rate-limit wait or the send.</param>
    /// <exception cref="InvalidOperationException">No logged-in connection is active.</exception>
    /// <exception cref="TwitchAuthorizationException">The token's known scopes lack <c>chat:edit</c>.</exception>
    /// <exception cref="TwitchIrcException">The connection failed while sending; the message may not have been delivered.</exception>
    public Task SendMessageAsync(string channel, string message, string? replyParentMessageId = null, CancellationToken cancellationToken = default)
    {
        var name = NormalizeChannelName(channel);
        ValidateMessage(message);
        KeyValuePair<string, string>[]? tags = null;
        if (replyParentMessageId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(replyParentMessageId);
            if (replyParentMessageId.Any(char.IsControl)) throw new ArgumentException("The reply parent message ID must not contain control characters.", nameof(replyParentMessageId));
            tags = [new("reply-parent-msg-id", replyParentMessageId)];
        }
        var line = new IrcMessage("PRIVMSG", ["#" + name, message], tags, lastParameterIsTrailing: true).Serialize();
        return SendLimitedAsync(line, _messageLimiter, 1, EditRequirement, cancellationToken);
    }

    /// <summary>
    /// Sends a raw message, for example a PRIVMSG with a <c>client-nonce</c> tag. PRIVMSG and JOIN count against their rate limits
    /// (JOIN once per comma-separated channel), but raw JOIN and PART do not change <see cref="JoinedChannels"/>. Login commands are rejected.
    /// </summary>
    /// <exception cref="InvalidOperationException">No logged-in connection is active.</exception>
    /// <exception cref="TwitchIrcException">The connection failed while sending.</exception>
    public Task SendRawAsync(IrcMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Command is "PASS" or "NICK" or "USER" or "CAP")
            throw new ArgumentException("The client manages PASS, NICK, USER and CAP during login.", nameof(message));
        var line = message.Serialize();
        return message.Command switch
        {
            "PRIVMSG" => SendLimitedAsync(line, _messageLimiter, 1, EditRequirement, cancellationToken),
            "JOIN" => SendLimitedAsync(line, _joinLimiter, Math.Max(1, message.GetParameter(0)?.Split(',').Length ?? 1), null, cancellationToken),
            _ => SendLimitedAsync(line, null, 0, null, cancellationToken)
        };
    }

    /// <summary>Normalizes a channel name: removes one leading <c>#</c> and lower-cases it.</summary>
    /// <exception cref="ArgumentException">The name is not 1 to 25 ASCII letters, digits or underscores.</exception>
    public static string NormalizeChannelName(string channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var name = channel.StartsWith('#') ? channel[1..] : channel;
        return IsValidName(name) ? name.ToLowerInvariant()
            : throw new ArgumentException("A channel must be a Twitch login of 1 to 25 letters, digits or underscores, optionally prefixed with '#'.", nameof(channel));
    }

    internal static string NormalizeLogin(string login)
    {
        ArgumentNullException.ThrowIfNull(login);
        return IsValidName(login) ? login.ToLowerInvariant()
            : throw new ArgumentException("The login must be a Twitch login of 1 to 25 letters, digits or underscores.", nameof(login));
    }

    private static bool IsValidName(string name) => name.Length is >= 1 and <= 25 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private static void ValidateMessage(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (message.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0) throw new ArgumentException("Chat messages must not contain CR, LF or NUL.", nameof(message));
        if (message.EnumerateRunes().Count() > MaxMessageLength)
            throw new ArgumentException($"Chat messages may contain at most {MaxMessageLength} Unicode code points.", nameof(message));
    }

    private async Task SendLimitedAsync(string line, IrcSlidingWindowRateLimiter? limiter, int permits, TwitchAuthorizationRequirement? requirement, CancellationToken ct)
    {
        // Fail fast before waiting for a permit; check again afterwards because a reconnect may have replaced the session and token.
        var session = CurrentSession();
        requirement?.Validate(session.Token);
        for (var i = 0; i < permits; i++) await limiter!.AcquireAsync(ct).ConfigureAwait(false);
        session = CurrentSession();
        requirement?.Validate(session.Token);
        try
        {
            await SendAsync(session, line, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct) || ex is ObjectDisposedException)
        {
            throw new TwitchIrcException("The IRC connection failed while sending; the line may not have been delivered.", ex);
        }
    }

    private Session CurrentSession()
        => Volatile.Read(ref _session) ?? throw new InvalidOperationException("The IRC client is not connected. Start RunAsync and retry after it has logged in.");

    private async Task<(Session Session, IReadOnlyList<IrcMessage> Replies)> LoginAsync(CancellationToken ct)
    {
        var token = await _tokens.GetTokenAsync(ct).ConfigureAwait(false);
        ReadRequirement.Validate(token);
        for (var attempt = 0; ; attempt++)
        {
            await _authLimiter.AcquireAsync(ct).ConfigureAwait(false);
            var connection = _factory() ?? throw new InvalidOperationException("The connection factory returned null.");
            Handshake result;
            try
            {
                result = await HandshakeAsync(connection, token, ct).ConfigureAwait(false);
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            if (result.FailureNotice is null)
            {
                _logger.LogDebug("Logged in to Twitch IRC as {Login}.", _login);
                return (new Session(connection, token), result.Replies);
            }
            await connection.DisposeAsync().ConfigureAwait(false);
            var notice = result.FailureNotice;
            if (attempt > 0 || notice.Contains("Improperly formatted", StringComparison.OrdinalIgnoreCase))
                throw new TwitchIrcAuthenticationException($"Twitch rejected the IRC login: {notice}", notice);
            // An expired token is the usual cause; retry once with a refreshed token when the provider can supply a different one.
            var refreshed = await _tokens.RefreshTokenAsync(token, ct).ConfigureAwait(false);
            if (refreshed is null || string.Equals(refreshed.Value, token.Value, StringComparison.Ordinal))
                throw new TwitchIrcAuthenticationException($"Twitch rejected the IRC login: {notice}", notice);
            ReadRequirement.Validate(refreshed);
            token = refreshed;
        }
    }

    private async Task<Handshake> HandshakeAsync(IIrcConnection connection, AccessToken token, CancellationToken ct)
    {
        var pass = PassLine(token);
        using var timeout = new CancellationTokenSource(_options.ConnectTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        var lt = linked.Token;
        await connection.ConnectAsync(_options.Endpoint, lt).ConfigureAwait(false);
        if (_capabilityRequest is not null) await connection.SendLineAsync(_capabilityRequest, lt).ConfigureAwait(false);
        await connection.SendLineAsync(pass, lt).ConfigureAwait(false);
        await connection.SendLineAsync("NICK " + _login, lt).ConfigureAwait(false);
        var replies = new List<IrcMessage>();
        while (true)
        {
            var line = await connection.ReceiveLineAsync(lt).ConfigureAwait(false) ?? throw new IrcConnectionClosedException("Twitch closed the IRC connection during login.");
            if (!IrcMessage.TryParse(line, out var message)) continue;
            switch (message.Command)
            {
                case "PING":
                    await connection.SendLineAsync(Pong(message), lt).ConfigureAwait(false);
                    continue;
                case "CAP" when message.GetParameter(1) == "NAK":
                    throw new TwitchIrcException($"Twitch rejected the requested IRC capabilities: {message.GetParameter(2)}");
                case "NOTICE" when message.GetParameter(0) == "*":
                    return new Handshake(message.GetParameter(message.Parameters.Count - 1) ?? "", replies);
            }
            replies.Add(message);
            if (message.Command == "001") return new Handshake(null, replies);
        }
    }

    private async Task<bool> RunSessionAsync(Session session, IReadOnlyList<IrcMessage> replies, Func<IrcMessage, CancellationToken, Task> onMessage, CancellationToken ct)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Publish before taking the rejoin snapshot so a concurrent JoinAsync either lands in the snapshot or sends itself.
        Volatile.Write(ref _session, session);
        var rejoin = RejoinAsync(session, lifetime.Token);
        Task<string?>? pending = null;
        try
        {
            foreach (var reply in replies) await DispatchAsync(reply, onMessage, ct).ConfigureAwait(false);
            var awaitingPong = false;
            while (true)
            {
                pending ??= session.Connection.ReceiveLineAsync(lifetime.Token);
                string? line;
                try
                {
                    line = await pending.WaitAsync(awaitingPong ? _options.KeepaliveTimeout : _options.KeepaliveInterval, _time, ct).ConfigureAwait(false);
                }
                catch (TimeoutException) when (!awaitingPong)
                {
                    awaitingPong = true;
                    if (await TrySendAsync(session, PingLine, ct).ConfigureAwait(false)) continue;
                    return false;
                }
                catch (Exception ex) when (IsConnectionFailure(ex, ct))
                {
                    _logger.LogDebug(ex, "Twitch IRC receive failed.");
                    return false;
                }
                pending = null;
                awaitingPong = false;
                if (line is null) return false;
                var result = await HandleLineAsync(session, line, onMessage, ct).ConfigureAwait(false);
                if (result != LineResult.Continue) return result == LineResult.Reconnect;
            }
        }
        finally
        {
            Interlocked.CompareExchange(ref _session, null, session);
            lifetime.Cancel();
            await session.Connection.DisposeAsync().ConfigureAwait(false);
            await ObserveAsync(rejoin).ConfigureAwait(false);
            // A transport may complete an abandoned receive only after disposal; observe it without waiting.
            _ = pending?.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private async Task<LineResult> HandleLineAsync(Session session, string line, Func<IrcMessage, CancellationToken, Task> onMessage, CancellationToken ct)
    {
        if (!IrcMessage.TryParse(line, out var message))
        {
            _logger.LogDebug("Skipped a malformed Twitch IRC line of {Length} characters.", line.Length);
            return LineResult.Continue;
        }
        switch (message.Command)
        {
            case "PING":
                return await TrySendAsync(session, Pong(message), ct).ConfigureAwait(false) ? LineResult.Continue : LineResult.ConnectionLost;
            case "PONG":
                return LineResult.Continue;
            case "RECONNECT":
                await DispatchAsync(message, onMessage, ct).ConfigureAwait(false);
                return LineResult.Reconnect;
            default:
                await DispatchAsync(message, onMessage, ct).ConfigureAwait(false);
                return LineResult.Continue;
        }
    }

    private async Task RejoinAsync(Session session, CancellationToken ct)
    {
        string[] channels;
        lock (_channels) channels = _channels.ToArray();
        foreach (var channel in channels)
        {
            await _joinLimiter.AcquireAsync(ct).ConfigureAwait(false);
            lock (_channels) if (!_channels.Contains(channel)) continue;
            await SendAsync(session, "JOIN #" + channel, ct).ConfigureAwait(false);
        }
    }

    private async Task SendAsync(Session session, string line, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await session.Connection.SendLineAsync(line, ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task<bool> TrySendAsync(Session session, string line, CancellationToken ct)
    {
        try
        {
            await SendAsync(session, line, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct) || ex is ObjectDisposedException)
        {
            return false;
        }
    }

    private static async Task DispatchAsync(IrcMessage message, Func<IrcMessage, CancellationToken, Task> onMessage, CancellationToken ct)
    {
        try
        {
            await onMessage(message, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Callback failures must not be misclassified as connection failures.
            throw new CallbackException(ex);
        }
    }

    private async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Background rejoin work ends with its session.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Twitch IRC rejoin stopped with the connection.");
        }
    }

    private static string PassLine(AccessToken token)
    {
        var value = token.Value.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase) ? token.Value[6..] : token.Value;
        if (value.Length == 0 || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new TwitchIrcAuthenticationException("The access token contains characters that cannot be sent over IRC.");
        return "PASS oauth:" + value;
    }

    private static string Pong(IrcMessage ping)
        => new IrcMessage("PONG", [ping.GetParameter(ping.Parameters.Count - 1) is { Length: > 0 } origin ? origin : "tmi.twitch.tv"], lastParameterIsTrailing: true).Serialize();

    private TimeSpan Backoff(int failures)
        => TimeSpan.FromSeconds(Math.Min(_options.MaxReconnectDelay.TotalSeconds, Math.Pow(2, Math.Min(failures - 1, 20))));

    /// <summary>Transport failures, internal timeouts and transient token endpoint failures are retried; everything else stops the client.</summary>
    private static bool IsConnectionFailure(Exception exception, CancellationToken ct)
        => !ct.IsCancellationRequested && (exception is WebSocketException or IOException or SocketException or OperationCanceledException or TimeoutException
            || exception is HttpRequestException { StatusCode: null or HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError });

    private enum LineResult { Continue, Reconnect, ConnectionLost }

    private sealed record Handshake(string? FailureNotice, IReadOnlyList<IrcMessage> Replies);

    private sealed class Session(IIrcConnection connection, AccessToken token)
    {
        public IIrcConnection Connection { get; } = connection;
        public AccessToken Token { get; } = token;
    }

    private sealed class CallbackException(Exception inner) : Exception("IRC callback failed.", inner);
}
