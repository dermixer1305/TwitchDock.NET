using System.Security.Cryptography;
using System.Text.Json;

namespace TwitchDock.EventSub;

/// <summary>A framework-independent webhook request: the Twitch-Eventsub-Message-* headers and the exact raw body bytes.</summary>
public sealed class EventSubWebhookRequest
{
    public const string MessageIdHeader = "Twitch-Eventsub-Message-Id";
    public const string MessageTypeHeader = "Twitch-Eventsub-Message-Type";
    public const string MessageTimestampHeader = "Twitch-Eventsub-Message-Timestamp";
    public const string MessageSignatureHeader = "Twitch-Eventsub-Message-Signature";

    public required string MessageId { get; init; }
    public required string MessageType { get; init; }
    public required string MessageTimestamp { get; init; }
    public required string MessageSignature { get; init; }
    /// <summary>The unmodified request body. Re-serialized JSON fails signature verification.</summary>
    public required ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>Reads the headers through a host lookup, for example <c>name =&gt; request.Headers[name].ToString()</c>.</summary>
    public static EventSubWebhookRequest FromHeaders(Func<string, string?> header, ReadOnlyMemory<byte> body)
    {
        ArgumentNullException.ThrowIfNull(header);
        return new()
        {
            MessageId = header(MessageIdHeader) ?? "", MessageType = header(MessageTypeHeader) ?? "",
            MessageTimestamp = header(MessageTimestampHeader) ?? "", MessageSignature = header(MessageSignatureHeader) ?? "", Body = body,
        };
    }
}

/// <summary>The response the host should send back to Twitch.</summary>
public sealed class EventSubWebhookResponse
{
    public int StatusCode { get; init; }
    public string? ContentType { get; init; }
    public string? Body { get; init; }
}

/// <summary>
/// Verifies, deduplicates and dispatches webhook deliveries. Answers the callback verification challenge.
/// The Twitch-Eventsub-Message-Type header is not covered by the signature, so it must agree with the signed body (400 otherwise).
/// Handler exceptions propagate after the message ID is released, so the host returns 5xx and Twitch retries. Events that do
/// not match their typed model are acknowledged (see <see cref="EventSubEventRouter.OnDeserializationError"/>). A full
/// fail-closed deduplicator answers 503. The default deduplicator is process-local; share a durable store across replicas.
/// </summary>
public sealed class EventSubWebhookHandler
{
    private static readonly EventSubWebhookResponse NoContent = new() { StatusCode = 204 };
    private static readonly EventSubWebhookResponse BadRequest = new() { StatusCode = 400 };
    private static readonly EventSubWebhookResponse Forbidden = new() { StatusCode = 403 };
    private static readonly EventSubWebhookResponse ServiceUnavailable = new() { StatusCode = 503 };
    private readonly EventSubWebhookVerifier _verifier;
    private readonly EventSubEventRouter _router;
    private readonly MessageDeduplicator _deduplicator;

    public EventSubWebhookHandler(EventSubWebhookVerifier verifier, EventSubEventRouter router, MessageDeduplicator? deduplicator = null)
    {
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _deduplicator = deduplicator ?? new();
    }

    public async Task<EventSubWebhookResponse> HandleAsync(EventSubWebhookRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EventSubPayload payload;
        try { payload = _verifier.VerifyAndParse(request.MessageId, request.MessageTimestamp, request.MessageSignature, request.Body.Span); }
        catch (CryptographicException) { return Forbidden; }
        catch (Exception ex) when (ex is ArgumentException or JsonException) { return BadRequest; }
        switch (request.MessageType)
        {
            case "webhook_callback_verification":
                return string.IsNullOrEmpty(payload.Challenge) ? BadRequest : new() { StatusCode = 200, ContentType = "text/plain", Body = payload.Challenge };
            case "notification" or "revocation":
                if (!MatchesSignedBody(request.MessageType, payload)) return BadRequest;
                // A retry of an already accepted delivery is acknowledged without processing it twice.
                try { if (!_deduplicator.TryAdd(request.MessageId)) return NoContent; }
                catch (EventSubDeduplicationException) { return ServiceUnavailable; }
                try { await _router.DispatchAsync(request.MessageType, payload, cancellationToken).ConfigureAwait(false); }
                catch { _deduplicator.Remove(request.MessageId); throw; }
                return NoContent;
            default:
                // Acknowledge message types added by Twitch later instead of forcing endless retries.
                return NoContent;
        }
    }

    /// <summary>A notification carries an event (or batched events) and no challenge; a revocation carries a subscription that is no longer enabled.</summary>
    private static bool MatchesSignedBody(string messageType, EventSubPayload payload) => messageType switch
    {
        "notification" => payload.Challenge is null && payload.Subscription is not null && payload.HasEventData,
        "revocation" => payload.Challenge is null && payload.Subscription is { } subscription && !string.Equals(subscription.Status, "enabled", StringComparison.Ordinal),
        _ => false,
    };
}
