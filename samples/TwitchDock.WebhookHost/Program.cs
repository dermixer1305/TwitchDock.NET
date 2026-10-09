// An EventSub webhook receiver on ASP.NET Core minimal APIs. EventSubWebhookHandler verifies the signature on the raw
// bytes, answers the callback challenge, suppresses duplicate deliveries and dispatches typed events to the router.
// The secret comes only from the environment and must match the secret used when the subscriptions were created.
using TwitchDock.EventSub;

const int MaxBodyBytes = 1024 * 1024; // Twitch payloads are far smaller; the verifier rejects anything larger.

var secret = Environment.GetEnvironmentVariable("TWITCH_EVENTSUB_SECRET") is { Length: > 0 } value
    ? value
    : throw new InvalidOperationException("Set the TWITCH_EVENTSUB_SECRET environment variable (10 to 100 ASCII characters).");

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = MaxBodyBytes);
var app = builder.Build();
var logger = app.Logger;

var router = new EventSubEventRouter()
    .On(EventSubEvents.StreamOnlineV1, (online, _, _) =>
    {
        logger.LogInformation("{Broadcaster} went live ({StreamType}) at {StartedAt:O}", online.BroadcasterUserName, online.Type, online.StartedAt);
        return Task.CompletedTask;
    })
    .On(EventSubEvents.ChannelFollowV2, (follow, _, _) =>
    {
        logger.LogInformation("{User} followed {Broadcaster}", follow.UserName, follow.BroadcasterUserName);
        return Task.CompletedTask;
    })
    .OnRevocation((subscription, _) =>
    {
        logger.LogWarning("Subscription {SubscriptionId} ({Type}) was revoked: {Status}", subscription.Id, subscription.Type, subscription.Status);
        return Task.CompletedTask;
    });

// The default deduplicator is in memory. Several replicas need a shared, durable inbox instead.
var handler = new EventSubWebhookHandler(new EventSubWebhookVerifier(secret), router);

app.MapPost("/eventsub", async (HttpRequest request, HttpResponse response, CancellationToken cancellationToken) =>
{
    // Read the exact bytes Twitch signed. Parsing and re-serializing the JSON first would break the signature.
    using var body = new MemoryStream();
    await request.Body.CopyToAsync(body, cancellationToken);
    var result = await handler.HandleAsync(EventSubWebhookRequest.FromHeaders(name => request.Headers[name].ToString(), body.ToArray()), cancellationToken);
    // 200 + text/plain challenge for callback verification, 204 for notifications, 403 for bad signatures, 400 for malformed requests.
    // A handler exception propagates as HTTP 500, so Twitch retries the delivery.
    response.StatusCode = result.StatusCode;
    if (result.ContentType is not null) response.ContentType = result.ContentType;
    if (result.Body is not null) await response.WriteAsync(result.Body, cancellationToken);
});

app.Run();
