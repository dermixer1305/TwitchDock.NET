using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.ExceptionServices;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TwitchDock.Core;

namespace TwitchDock.Chat.Irc;

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

    /// <summary>A connection must stay up this many seconds before the reconnect backoff resets.</summary>
    internal const int StableSessionSeconds = 30;

    /// <summary>A RECONNECT within this many seconds of login counts as a failed connection and waits for the backoff.</summary>
    internal const int RapidReconnectSeconds = 10;

    private const int MaxPongOriginLength = 256;
    private const string DefaultPingOrigin = "tmi.twitch.tv";
    private const string PingLine = "PING :" + DefaultPingOrigin;
    private static readonly TimeSpan StableSessionDuration = TimeSpan.FromSeconds(StableSessionSeconds);
    private static readonly TimeSpan RapidReconnectWindow = TimeSpan.FromSeconds(RapidReconnectSeconds);
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
    // JOINs requested by JoinAsync that still wait for a rate-limit permit, keyed by channel. Guarded by the _channels lock.
    private readonly Dictionary<string, PendingJoin> _pendingJoins = new(StringComparer.Ordinal);
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
    /// Malformed lines are skipped. The reconnect backoff only resets after a connection stayed up for 30 seconds, and a RECONNECT
    /// within 10 seconds of login waits for the backoff, so a server that accepts and then drops connections is not hammered.
    /// </summary>
    /// <param name="onMessage">Handles messages sequentially. Exceptions stop the client and propagate to the caller.</param>
    /// <param name="cancellationToken">Stops the client.</param>
    /// <exception cref="TwitchIrcAuthenticationException">Twitch rejected the login, also after one token refresh.</exception>
    /// <exception cref="TwitchAuthorizationException">The token is an app token or lacks <c>chat:read</c>.</exception>
    /// <exception cref="TwitchIrcException">Twitch rejected a requested capability.</exception>
    /// <exception cref="OperationCanceledException">The client was stopped with <paramref name="cancellationToken"/>.</exception>
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
                Login login;
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
                var started = _time.GetTimestamp();
                var reconnectRequested = await RunSessionAsync(login, onMessage, cancellationToken).ConfigureAwait(false);
                var lasted = _time.GetElapsedTime(started);
                // Only a connection that stayed up resets the backoff; otherwise accept-then-drop servers would be retried every second.
                if (lasted >= StableSessionDuration) failures = 0;
                if (reconnectRequested && lasted >= RapidReconnectWindow)
                {
                    _logger.LogInformation("Twitch IRC requested a reconnect.");
                    continue;
                }
                var retry = Backoff(++failures);
                if (reconnectRequested) _logger.LogWarning("Twitch IRC requested a reconnect right after login; reconnecting in {Delay}.", retry);
                else _logger.LogWarning("Twitch IRC connection lost; reconnecting in {Delay}.", retry);
                await Task.Delay(retry, _time, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (CallbackException ex)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
            throw;
        }
        catch (Exception ex) when (cancellationToken.IsCancellationRequested && ex is not OperationCanceledException
            && (IsTransientFailure(ex) || ex is ObjectDisposedException))
        {
            // Transports may report their own cancellation as a failure, for example an aborted socket read as IOException on .NET 8.
            throw new OperationCanceledException("The IRC client was stopped.", ex, cancellationToken);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested && ex.CancellationToken != cancellationToken)
        {
            // Internal session tokens are linked to the caller's; report the caller's token so `when (ex.CancellationToken == stoppingToken)` filters work.
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    /// <summary>
    /// Joins a channel and keeps it joined across reconnects. JOIN is rate limited; while disconnected the channel is joined after the next login.
    /// Joining a channel that is already joined does nothing; joining one whose JOIN still waits for the rate limit waits as well.
    /// </summary>
    /// <param name="channel">The channel login, with or without <c>#</c>; case-insensitive.</param>
    /// <param name="cancellationToken">Cancels the rate-limit wait. The channel is then not joined, unless another pending JoinAsync call asked for it too.</param>
    public async Task JoinAsync(string channel, CancellationToken cancellationToken = default)
    {
        var name = NormalizeChannelName(channel);
        PendingJoin claim;
        lock (_channels)
        {
            if (_channels.Add(name))
            {
                if (Volatile.Read(ref _session) is null) return;
                _pendingJoins[name] = claim = new PendingJoin();
            }
            else if (_pendingJoins.TryGetValue(name, out var pending))
            {
                // Share the pending JOIN so the first caller's cancellation does not drop a channel this caller asked for.
                claim = pending;
                claim.Claims++;
            }
            else return;
        }
        try
        {
            await _joinLimiter.AcquireAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (_channels)
            {
                // Remove the channel only when this was the last claim on a JOIN that is still pending.
                if (_pendingJoins.TryGetValue(name, out var pending) && pending == claim && --claim.Claims == 0)
                {
                    _pendingJoins.Remove(name);
                    _channels.Remove(name);
                }
            }
            throw;
        }
        Session? session;
        lock (_channels)
        {
            // The first claimant with a permit sends the JOIN; the others are done. PartAsync may also have dropped the channel meanwhile.
            if (!_pendingJoins.TryGetValue(name, out var pending) || pending != claim) return;
            _pendingJoins.Remove(name);
            session = Volatile.Read(ref _session);
        }
        // Once a permit is taken the JOIN is no longer cancellable. A failed connection is reported by RunAsync and the channel rejoined afterwards.
        if (session is not null) await TrySendAsync(session, "JOIN #" + name, CancellationToken.None, () => IsJoined(name)).ConfigureAwait(false);
    }

    /// <summary>Leaves a channel and stops rejoining it. Does nothing when the channel is not joined.</summary>
    /// <param name="channel">The channel login, with or without <c>#</c>; case-insensitive.</param>
    /// <param name="cancellationToken">Cancels waiting to send PART; the channel is still not rejoined after reconnects.</param>
    public async Task PartAsync(string channel, CancellationToken cancellationToken = default)
    {
        var name = NormalizeChannelName(channel);
        lock (_channels)
        {
            if (!_channels.Remove(name)) return;
            _pendingJoins.Remove(name);
        }
        if (Volatile.Read(ref _session) is { } session)
            await TrySendAsync(session, "PART #" + name, cancellationToken, () => IsParted(name)).ConfigureAwait(false);
    }

    /// <summary>Sends a chat message, waiting for the message rate limit when necessary.</summary>
    /// <param name="channel">The channel login, with or without <c>#</c>; case-insensitive.</param>
    /// <param name="message">1 to 500 Unicode code points without CR, LF or NUL. A leading <c>/me </c> is not translated.</param>
    /// <param name="replyParentMessageId">The ID of the message to reply to, sent as the <c>reply-parent-msg-id</c> tag.</param>
    /// <param name="cancellationToken">Cancels waiting for the rate limit or for earlier sends; a line that is being written is not interrupted.</param>
    /// <exception cref="InvalidOperationException">No logged-in connection is active.</exception>
    /// <exception cref="TwitchAuthorizationException">The token's known scopes lack <c>chat:edit</c>.</exception>
    /// <exception cref="TwitchIrcException">
    /// The connection failed while sending, or the write did not complete within <see cref="TwitchIrcOptions.KeepaliveTimeout"/>;
    /// the message may not have been delivered and the client reconnects.
    /// </exception>
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
    /// The token cancels waiting for the rate limit or for earlier sends; a line that is being written is not interrupted.
    /// </summary>
    /// <exception cref="InvalidOperationException">No logged-in connection is active.</exception>
    /// <exception cref="TwitchIrcException">The connection failed or timed out while sending; the client reconnects.</exception>
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

    private bool IsJoined(string channel)
    {
        lock (_channels) return _channels.Contains(channel);
    }

    /// <summary>True unless the channel was joined again and its JOIN already went out, in which case a late PART would undo it.</summary>
    private bool IsParted(string channel)
    {
        lock (_channels) return !_channels.Contains(channel) || _pendingJoins.ContainsKey(channel);
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

    private async Task<Login> LoginAsync(CancellationToken ct)
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
                return new Login(connection, token, result.Replies);
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

    /// <summary>Processes one logged-in connection. Returns true when Twitch asked for a reconnect and false when the connection was lost.</summary>
    private async Task<bool> RunSessionAsync(Login login, Func<IrcMessage, CancellationToken, Task> onMessage, CancellationToken ct)
    {
        var session = new Session(login.Connection, login.Token, ct);
        var rejoin = Task.CompletedTask;
        Task<string?>? pending = null;
        try
        {
            // Publish before taking the rejoin snapshot so a concurrent JoinAsync either lands in the snapshot or sends itself.
            Volatile.Write(ref _session, session);
            rejoin = RejoinAsync(session);
            foreach (var reply in login.Replies) await DispatchAsync(reply, onMessage, ct).ConfigureAwait(false);
            var awaitingPong = false;
            var discarded = 0;
            while (true)
            {
                pending ??= session.Connection.ReceiveLineAsync(session.Lifetime);
                string? line;
                try
                {
                    // The session token also ends the wait when a failed or stuck send tears the connection down.
                    line = await pending.WaitAsync(awaitingPong ? _options.KeepaliveTimeout : _options.KeepaliveInterval, _time, session.Lifetime).ConfigureAwait(false);
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
                discarded = ReportDiscardedLines(session.Connection, discarded);
                if (line is null) return false;
                var result = await HandleLineAsync(session, line, onMessage, ct).ConfigureAwait(false);
                if (result != LineResult.Continue) return result == LineResult.Reconnect;
            }
        }
        finally
        {
            await CloseSessionAsync(session, rejoin, pending).ConfigureAwait(false);
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

    private async Task RejoinAsync(Session session)
    {
        string[] channels;
        lock (_channels) channels = _channels.ToArray();
        foreach (var channel in channels)
        {
            if (!IsJoined(channel)) continue;
            await _joinLimiter.AcquireAsync(session.Lifetime).ConfigureAwait(false);
            // Checked again under the send lock, so a concurrent PartAsync is never followed by a stale JOIN.
            await SendAsync(session, "JOIN #" + channel, session.Lifetime, () => IsJoined(channel)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes one line under the send lock. <paramref name="waitToken"/> only cancels waiting for the lock: an interrupted write can leave
    /// part of a line on the stream, so the write runs on the session token and is bounded by <see cref="TwitchIrcOptions.KeepaliveTimeout"/>.
    /// A write that fails, times out or is cancelled ends the session, and RunAsync reconnects.
    /// An optional condition is evaluated under the send lock right before writing; the line is skipped when it returns false.
    /// </summary>
    private async Task SendAsync(Session session, string line, CancellationToken waitToken, Func<bool>? condition = null)
    {
        await _sendLock.WaitAsync(waitToken).ConfigureAwait(false);
        try
        {
            session.Lifetime.ThrowIfCancellationRequested();
            if (condition is not null && !condition()) return;
            Task? send = null;
            try
            {
                send = session.Connection.SendLineAsync(line, session.Lifetime);
                await send.WaitAsync(_options.KeepaliveTimeout, _time, session.Lifetime).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not ArgumentException)
            {
                if (!session.Lifetime.IsCancellationRequested) _logger.LogWarning(ex, "Twitch IRC send failed; closing the connection.");
                Forget(session.CancelAsync());
                // Observe the write even if it faulted after the timeout fired, so no exception goes unobserved.
                if (send is not null) Forget(send);
                throw;
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Sends on behalf of the client. Returns false when the connection failed; RunAsync reports and recovers from that.</summary>
    private async Task<bool> TrySendAsync(Session session, string line, CancellationToken ct, Func<bool>? condition = null)
    {
        try
        {
            await SendAsync(session, line, ct, condition).ConfigureAwait(false);
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

    /// <summary>Tears a session down. Every step runs even when an earlier one fails, and none replaces the exception that ended the session.</summary>
    private async Task CloseSessionAsync(Session session, Task rejoin, Task<string?>? pending)
    {
        Interlocked.CompareExchange(ref _session, null, session);
        try
        {
            await session.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cancelling the Twitch IRC session failed.");
        }
        try
        {
            // A transport stuck in a close handshake must not block the reconnect.
            var dispose = session.Connection.DisposeAsync().AsTask();
            try { await dispose.WaitAsync(_options.KeepaliveTimeout, _time).ConfigureAwait(false); }
            catch (TimeoutException) { Forget(dispose); throw; }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Disposing the Twitch IRC connection failed.");
        }
        await ObserveAsync(rejoin).ConfigureAwait(false);
        // A transport may complete an abandoned receive only after disposal; observe it without waiting.
        if (pending is not null) Forget(pending);
        session.Dispose();
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

    private int ReportDiscardedLines(IIrcConnection connection, int reported)
    {
        var discarded = connection switch
        {
            TcpIrcConnection tcp => tcp.DiscardedLines,
            WebSocketIrcConnection webSocket => webSocket.DiscardedLines,
            _ => reported,
        };
        if (discarded > reported)
            _logger.LogWarning("Twitch IRC discarded {Count} received line(s) longer than the transport's line limit.", discarded - reported);
        return discarded;
    }

    /// <summary>Observes a task that nobody awaits, so its failure does not surface as an unobserved exception.</summary>
    private static void Forget(Task task)
        => _ = task.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private static string PassLine(AccessToken token)
    {
        var value = token.Value.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase) ? token.Value[6..] : token.Value;
        if (value.Length == 0 || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new TwitchIrcAuthenticationException("The access token contains characters that cannot be sent over IRC.");
        return "PASS oauth:" + value;
    }

    /// <summary>Echoes the PING origin, truncated so an oversized origin can neither exceed the line limit nor stop the client.</summary>
    private static string Pong(IrcMessage ping)
    {
        var origin = ping.GetParameter(ping.Parameters.Count - 1);
        if (string.IsNullOrEmpty(origin)) return "PONG :" + DefaultPingOrigin;
        if (origin.Length > MaxPongOriginLength)
            origin = origin[..(char.IsHighSurrogate(origin[MaxPongOriginLength - 1]) ? MaxPongOriginLength - 1 : MaxPongOriginLength)];
        // A parsed parameter never contains CR, LF or NUL, so the line is always valid.
        return "PONG :" + origin;
    }

    private TimeSpan Backoff(int failures)
        => TimeSpan.FromSeconds(Math.Min(_options.MaxReconnectDelay.TotalSeconds, Math.Pow(2, Math.Min(failures - 1, 20))));

    /// <summary>Transport failures, internal timeouts and transient token endpoint failures are retried; everything else stops the client.</summary>
    private static bool IsConnectionFailure(Exception exception, CancellationToken ct) => !ct.IsCancellationRequested && IsTransientFailure(exception);

    private static bool IsTransientFailure(Exception exception)
        => exception is WebSocketException or IOException or SocketException or OperationCanceledException or TimeoutException
            || (exception is AuthenticationException authentication && TcpIrcConnection.IsTransientHandshakeFailure(authentication))
            || exception is HttpRequestException { StatusCode: null or HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError };

    private enum LineResult { Continue, Reconnect, ConnectionLost }

    private sealed record Handshake(string? FailureNotice, IReadOnlyList<IrcMessage> Replies);

    private sealed record Login(IIrcConnection Connection, AccessToken Token, IReadOnlyList<IrcMessage> Replies);

    private sealed class PendingJoin
    {
        public int Claims { get; set; } = 1;
    }

    /// <summary>One logged-in connection. Its lifetime token ends when the session closes, the client stops or a send fails.</summary>
    private sealed class Session : IDisposable
    {
        private readonly CancellationTokenSource _lifetime;
        private readonly object _sync = new();
        private Task? _cancellation;
        private bool _disposed;

        public Session(IIrcConnection connection, AccessToken token, CancellationToken stop)
        {
            Connection = connection;
            Token = token;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(stop);
            Lifetime = _lifetime.Token;
        }

        public IIrcConnection Connection { get; }

        public AccessToken Token { get; }

        public CancellationToken Lifetime { get; }

        /// <summary>
        /// Ends the session. The token is cancelled immediately but its callbacks run asynchronously, so a sender that holds the send lock
        /// never runs the receive loop's teardown inline. Repeated calls return the first cancellation.
        /// </summary>
        public Task CancelAsync()
        {
            lock (_sync) return _disposed ? Task.CompletedTask : (_cancellation ??= _lifetime.CancelAsync());
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
            }
            _lifetime.Dispose();
        }
    }

    private sealed class CallbackException(Exception inner) : Exception("IRC callback failed.", inner);
}
