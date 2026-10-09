using System.Net.WebSockets;
using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace TwitchSdk.EventSub;

/// <summary>One receive loop per client. Callbacks run sequentially and must return promptly.</summary>
public sealed class EventSubWebSocketClient
{
    public static readonly Uri DefaultEndpoint = new("wss://eventsub.wss.twitch.tv/ws");
    private readonly Uri _endpoint;
    private readonly Func<IEventSubConnection> _factory;
    private readonly MessageDeduplicator _deduplicator;
    private readonly TimeProvider _time;
    private int _running;

    /// <param name="connectionFactory">Creates a connection per session; defaults to <see cref="ClientWebSocketConnection"/>.</param>
    /// <param name="deduplicator">Suppresses redelivered message IDs.</param>
    /// <param name="timeProvider">Clock for keepalive and backoff timing.</param>
    /// <param name="endpoint">Defaults to Twitch. A ws:// endpoint is only accepted on loopback hosts, for example the Twitch CLI mock server.</param>
    public EventSubWebSocketClient(Func<IEventSubConnection>? connectionFactory = null, MessageDeduplicator? deduplicator = null, TimeProvider? timeProvider = null,
        Uri? endpoint = null)
    {
        _endpoint = endpoint ?? DefaultEndpoint;
        if (!_endpoint.IsAbsoluteUri || !string.IsNullOrEmpty(_endpoint.UserInfo) || !(_endpoint.Scheme == "wss" || (_endpoint.Scheme == "ws" && _endpoint.IsLoopback)))
            throw new ArgumentException("The EventSub endpoint must use wss://, or ws:// on a loopback host.", nameof(endpoint));
        _factory = connectionFactory ?? (() => new ClientWebSocketConnection());
        _time = timeProvider ?? TimeProvider.System;
        _deduplicator = deduplicator ?? new(timeProvider: _time);
    }

    /// <param name="onSession">Create subscriptions promptly when resubscribe is true. False indicates a Twitch session migration.</param>
    /// <param name="onMessage">Handle notifications and revocations. Exceptions stop the client and propagate to its host.</param>
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
                        if (!Uri.TryCreate(reconnect, UriKind.Absolute, out var uri) || uri.Scheme != _endpoint.Scheme || !string.Equals(uri.Host, _endpoint.Host, StringComparison.OrdinalIgnoreCase)
                            || uri.Port != _endpoint.Port || !string.IsNullOrEmpty(uri.UserInfo))
                            throw new JsonException("Invalid Twitch reconnect URL.");
                        try
                        {
                            var migrated = await MigrateAsync(connection, session, uri, onMessage, cancellationToken).ConfigureAwait(false);
                            await connection.DisposeAsync().ConfigureAwait(false);
                            connection = migrated.Connection;
                            session = migrated.Session;
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
                await connection.DisposeAsync().ConfigureAwait(false);
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
            if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
            Volatile.Write(ref _running, 0);
        }
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
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private async Task<(IEventSubConnection Connection, EventSubSession Session)> MigrateAsync(IEventSubConnection old,
        EventSubSession session, Uri uri, Func<EventSubMessage, CancellationToken, Task> callback, CancellationToken ct)
    {
        // Keep receiving on the old connection until the replacement welcome arrives.
        using var migration = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var opening = OpenAsync(uri, migration.Token);
        using var oldReads = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<EventSubMessage>? pending = null;
        try
        {
            while (!opening.IsCompleted)
            {
                pending = ReceiveAsync(old, session, oldReads.Token);
                if (await Task.WhenAny(opening, pending).ConfigureAwait(false) == opening) break;
                var message = await pending.ConfigureAwait(false);
                pending = null;
                await DispatchAsync(message, callback, ct).ConfigureAwait(false);
            }
            var result = await opening.ConfigureAwait(false);
            try
            {
                if (pending is not null)
                {
                    if (pending.IsCompletedSuccessfully) await DispatchAsync(await pending.ConfigureAwait(false), callback, ct).ConfigureAwait(false);
                    else
                    {
                        oldReads.Cancel();
                        EventSubMessage? finalMessage = null;
                        try { finalMessage = await pending.ConfigureAwait(false); }
                        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
                        if (finalMessage is not null) await DispatchAsync(finalMessage, callback, ct).ConfigureAwait(false);
                    }
                }
                return result;
            }
            catch { await result.Connection.DisposeAsync().ConfigureAwait(false); throw; }
        }
        catch
        {
            migration.Cancel();
            oldReads.Cancel();
            if (pending is not null) { try { await pending.ConfigureAwait(false); } catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { } }
            try { var orphan = await opening.ConfigureAwait(false); await orphan.Connection.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or JsonException) { }
            throw;
        }
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
        if (message.Metadata.MessageType is "notification" or "revocation" && _deduplicator.TryAdd(message.Metadata.MessageId))
        {
            try { await callback(message, ct).ConfigureAwait(false); }
            catch (Exception ex)
            {
                _deduplicator.Remove(message.Metadata.MessageId);
                // Callback failures must not be misclassified as a socket failure during migration.
                throw new CallbackException(ex);
            }
        }
    }

    private sealed class CallbackException(Exception inner) : Exception("EventSub callback failed.", inner);

    private Task BackoffAsync(int failures, CancellationToken ct)
        => Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(failures - 1, 5)))), _time, ct);

    private static bool IsConnectionFailure(Exception exception, CancellationToken ct)
        => !ct.IsCancellationRequested && exception is WebSocketException or OperationCanceledException;
}
