using System.Net.WebSockets;
using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace TwitchDock.EventSub;

/// <summary>One receive loop per client. Callbacks run sequentially and must return promptly.</summary>
public sealed class EventSubWebSocketClient
{
    public static readonly Uri DefaultEndpoint = new("wss://eventsub.wss.twitch.tv/ws");
    private const double MaxBackoffSeconds = 30;
    private readonly Uri _endpoint;
    private readonly Func<IEventSubConnection> _factory;
    private readonly MessageDeduplicator _deduplicator;
    private readonly TimeProvider _time;
    private int _running;

    /// <param name="connectionFactory">Creates a connection per session; defaults to <see cref="ClientWebSocketConnection"/>.</param>
    /// <param name="deduplicator">Suppresses redelivered message IDs.</param>
    /// <param name="timeProvider">Clock for keepalive and backoff timing.</param>
    /// <param name="endpoint">Defaults to Twitch. A ws:// endpoint is only accepted on a loopback IP address or localhost, for example the Twitch CLI mock server.</param>
    public EventSubWebSocketClient(Func<IEventSubConnection>? connectionFactory = null, MessageDeduplicator? deduplicator = null, TimeProvider? timeProvider = null,
        Uri? endpoint = null)
    {
        _endpoint = endpoint ?? DefaultEndpoint;
        if (!ClientWebSocketConnection.IsAllowedEndpoint(_endpoint))
            throw new ArgumentException("The EventSub endpoint must use wss://, or ws:// on a loopback IP address or localhost.", nameof(endpoint));
        _factory = connectionFactory ?? (() => new ClientWebSocketConnection());
        _time = timeProvider ?? TimeProvider.System;
        _deduplicator = deduplicator ?? new(timeProvider: _time);
    }

    /// <param name="onSession">Create subscriptions promptly when resubscribe is true. False indicates a Twitch session migration.</param>
    /// <param name="onMessage">Handle notifications and revocations. Exceptions stop the client and propagate to its host.</param>
    /// <param name="cancellationToken">Stops the client and closes the connection.</param>
    /// <remarks>
    /// Connection failures reconnect with jittered exponential backoff. Besides callback exceptions, the loop ends with
    /// <see cref="EventSubDeduplicationException"/> when a fail-closed deduplicator is full and with <see cref="JsonException"/>
    /// for protocol violations such as a reconnect URL outside the configured origin.
    /// </remarks>
    public async Task RunAsync(Func<EventSubSession, bool, CancellationToken, Task> onSession,
        Func<EventSubMessage, CancellationToken, Task> onMessage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onSession);
        ArgumentNullException.ThrowIfNull(onMessage);
        if (Interlocked.Exchange(ref _running, 1) != 0) throw new InvalidOperationException("This EventSub client is already running.");
        IEventSubConnection? connection = null;
        var failures = 0;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EventSubSession session;
                try
                {
                    (connection, session) = await OpenAsync(_endpoint, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsConnectionFailure(ex, cancellationToken))
                {
                    await BackoffAsync(++failures, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                await onSession(session, true, cancellationToken).ConfigureAwait(false);
                while (true)
                {
                    EventSubMessage message;
                    try { message = await ReceiveAsync(connection, session, cancellationToken).ConfigureAwait(false); }
                    catch (Exception ex) when (IsConnectionFailure(ex, cancellationToken)) { break; }
                    if (message.Metadata.MessageType == "session_reconnect")
                    {
                        var reconnect = message.Payload.Session?.ReconnectUrl;
                        // Only follow reconnects to the configured origin so a forged URL cannot redirect the session.
                        // JsonException is kept (rather than InvalidDataException) because callers already handle it as a protocol violation.
                        if (!Uri.TryCreate(reconnect, UriKind.Absolute, out var uri) || uri.Scheme != _endpoint.Scheme || !string.Equals(uri.Host, _endpoint.Host, StringComparison.OrdinalIgnoreCase)
                            || uri.Port != _endpoint.Port || !string.IsNullOrEmpty(uri.UserInfo))
                            throw new JsonException("Invalid Twitch reconnect URL.");
                        try
                        {
                            var migrated = await MigrateAsync(connection, session, uri, onMessage, cancellationToken).ConfigureAwait(false);
                            // Take ownership of the replacement before retiring the old connection.
                            var retired = connection;
                            connection = migrated.Connection;
                            session = migrated.Session;
                            await DisposeQuietlyAsync(retired).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (IsConnectionFailure(ex, cancellationToken)) { break; }
                        await onSession(session, false, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await DispatchAsync(message, onMessage, cancellationToken).ConfigureAwait(false);
                        failures = 0;
                    }
                }
                await DisposeQuietlyAsync(connection).ConfigureAwait(false);
                connection = null;
                await BackoffAsync(++failures, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (CallbackException ex)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
            throw;
        }
        finally
        {
            if (connection is not null) await DisposeQuietlyAsync(connection).ConfigureAwait(false);
            Volatile.Write(ref _running, 0);
        }
    }

    /// <summary>Disposes a connection that is being abandoned; a failing (custom) DisposeAsync must not replace the outcome.</summary>
    private static async ValueTask DisposeQuietlyAsync(IEventSubConnection connection)
    {
        try { await connection.DisposeAsync().ConfigureAwait(false); }
        catch (Exception) { /* The connection is abandoned either way. */ }
    }

    private async Task<(IEventSubConnection Connection, EventSubSession Session)> OpenAsync(Uri uri, CancellationToken ct)
    {
        var connection = _factory();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30), _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            await connection.ConnectAsync(uri, linked.Token).ConfigureAwait(false);
            var welcome = await connection.ReceiveAsync(linked.Token).ConfigureAwait(false);
            if (welcome.Metadata.MessageType != "session_welcome" || welcome.Payload.Session is not { } session || string.IsNullOrEmpty(session.Id)) throw new JsonException("Expected EventSub session_welcome.");
            return (connection, session);
        }
        catch { await DisposeQuietlyAsync(connection).ConfigureAwait(false); throw; }
    }

    private async Task<(IEventSubConnection Connection, EventSubSession Session)> MigrateAsync(IEventSubConnection old,
        EventSubSession session, Uri uri, Func<EventSubMessage, CancellationToken, Task> callback, CancellationToken ct)
    {
        using var migration = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var opening = OpenAsync(uri, migration.Token);
        using var oldReads = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<EventSubMessage>? pending = null;
        try
        {
            // Keep receiving on the old connection until the replacement welcome arrives. If the old socket closes or fails
            // first it is drained: keep awaiting the replacement instead of discarding it and resubscribing.
            var drained = false;
            while (!drained && !opening.IsCompleted)
            {
                pending = ReceiveAsync(old, session, oldReads.Token);
                if (await Task.WhenAny(opening, pending).ConfigureAwait(false) == opening) break;
                var message = await TryReceiveFromRetiringAsync(pending, ct).ConfigureAwait(false);
                pending = null;
                if (message is null) drained = true;
                else await DispatchAsync(message, callback, ct).ConfigureAwait(false);
            }
            var result = await opening.ConfigureAwait(false);
            if (pending is not null)
            {
                // A read raced the welcome: deliver what it already received, otherwise stop reading the old socket.
                if (!pending.IsCompleted) oldReads.Cancel();
                var final = await TryReceiveFromRetiringAsync(pending, ct).ConfigureAwait(false);
                pending = null;
                if (final is not null) await DispatchAsync(final, callback, ct).ConfigureAwait(false);
            }
            return result;
        }
        catch
        {
            migration.Cancel();
            oldReads.Cancel();
            // Cleanup must neither mask the original failure nor leak the replacement, whatever the reads threw.
            if (pending is not null)
            {
                try { await pending.ConfigureAwait(false); }
                catch (Exception) { /* The original failure is rethrown below. */ }
            }
            // An opened replacement is disposed exactly once here; a failed one already disposed itself in OpenAsync.
            (IEventSubConnection Connection, EventSubSession Session)? orphan = null;
            try { orphan = await opening.ConfigureAwait(false); }
            catch (Exception) { /* The original failure is rethrown below. */ }
            if (orphan is { } opened) await DisposeQuietlyAsync(opened.Connection).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Awaits a read on the retiring connection. Returns null when it closed, timed out or sent an invalid frame.</summary>
    private static async Task<EventSubMessage?> TryReceiveFromRetiringAsync(Task<EventSubMessage> pending, CancellationToken ct)
    {
        try { return await pending.ConfigureAwait(false); }
        catch (Exception ex) when (IsConnectionFailure(ex, ct) || ex is JsonException) { return null; }
    }

    private async Task<EventSubMessage> ReceiveAsync(IEventSubConnection connection, EventSubSession session, CancellationToken ct)
    {
        var seconds = session.KeepaliveTimeoutSeconds ?? 10;
        if (seconds is < 1 or > 600) throw new JsonException("Invalid keepalive timeout.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds), _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        return await connection.ReceiveAsync(linked.Token).ConfigureAwait(false);
    }

    private async Task DispatchAsync(EventSubMessage message, Func<EventSubMessage, CancellationToken, Task> callback, CancellationToken ct)
    {
        if (message.Metadata.MessageType is not ("notification" or "revocation")) return;
        var id = message.Metadata.MessageId;
        // Twitch always sends an ID; a message without one cannot be deduplicated and is delivered instead of stopping the client.
        var tracked = !string.IsNullOrWhiteSpace(id);
        if (tracked && !_deduplicator.TryAdd(id)) return;
        try { await callback(message, ct).ConfigureAwait(false); }
        catch (Exception ex)
        {
            if (tracked) _deduplicator.Remove(id);
            // Callback failures must not be misclassified as a socket failure during migration.
            throw new CallbackException(ex);
        }
    }

    private sealed class CallbackException(Exception inner) : Exception("EventSub callback failed.", inner);

    /// <summary>
    /// Exponential backoff (1 s doubling to 30 s) with jitter between half and all of the step, so clients that lost their
    /// connections together do not reconnect in lockstep, and a delay never exceeds its step.
    /// </summary>
    private Task BackoffAsync(int failures, CancellationToken ct)
    {
        var step = Math.Min(MaxBackoffSeconds, Math.Pow(2, Math.Min(failures - 1, 5)));
        return Task.Delay(TimeSpan.FromMilliseconds(step * 1000 * (0.5 + Random.Shared.NextDouble() / 2)), _time, ct);
    }

    private static bool IsConnectionFailure(Exception exception, CancellationToken ct)
        => !ct.IsCancellationRequested && exception is WebSocketException or OperationCanceledException;
}
