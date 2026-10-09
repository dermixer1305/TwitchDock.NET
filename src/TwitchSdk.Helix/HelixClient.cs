using System.Text;
using System.Text.Json;
using TwitchSdk.Core;
using TwitchSdk.Helix.Clients;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Helix;

public sealed class HelixClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    public AdsClient Ads { get; } = new(transport);
    public AnalyticsClient Analytics { get; } = new(transport);
    public GamesClient Games { get; } = new(transport);
    public SearchClient Search { get; } = new(transport);
    public GoalsClient Goals { get; } = new(transport);
    public RaidsClient Raids { get; } = new(transport);
    public ClipsClient Clips { get; } = new(transport);
    public VideosClient Videos { get; } = new(transport);
    public CharityClient Charity { get; } = new(transport);
    public TeamsClient Teams { get; } = new(transport);
    public ChannelsClient Channels { get; } = new(transport);
    public StreamsClient Streams { get; } = new(transport);
    public SubscriptionsClient Subscriptions { get; } = new(transport);
    public BitsClient Bits { get; } = new(transport);
    public ChannelPointsClient ChannelPoints { get; } = new(transport);
    public PollsClient Polls { get; } = new(transport);
    public PredictionsClient Predictions { get; } = new(transport);
    public UsersClient Users { get; } = new(transport);
    public WhispersClient Whispers { get; } = new(transport);
    public ScheduleClient Schedule { get; } = new(transport);
    public ConduitsClient Conduits { get; } = new(transport);
    public HypeTrainClient HypeTrain { get; } = new(transport);
    public ChatClient Chat { get; } = new(transport);
    public ModerationClient Moderation { get; } = new(transport);
    // <group:moderation-a>
    // </group:moderation-a>
    // <group:moderation-b>
    // </group:moderation-b>
    // <group:chat-a>
    public TagsClient Tags { get; } = new(transport);
    public ContentClassificationClient ContentClassification { get; } = new(transport);
    // </group:chat-a>
    // <group:chat-b>
    // </group:chat-b>
    // <group:extensions>
    // </group:extensions>
    // <group:guest-star>
    public GuestStarClient GuestStar { get; } = new(transport);
    // </group:guest-star>

    /// <summary>Uses an app or user token; no filters means the authenticated user. Email requires user:read:email.</summary>
    public Task<HelixPage<TwitchUser>> GetUsersAsync(GetUsersRequest? request = null, CancellationToken cancellationToken = default)
        => Users.GetUsersAsync(request, cancellationToken);

    public Task<HelixPage<TwitchStream>> GetStreamsAsync(GetStreamsRequest? request = null, CancellationToken cancellationToken = default)
        => Streams.GetStreamsAsync(request, cancellationToken);

    public IAsyncEnumerable<TwitchStream> EnumerateStreamsAsync(GetStreamsRequest? request = null, CancellationToken cancellationToken = default)
        => Streams.EnumerateStreamsAsync(request, cancellationToken);

    public Task<HelixPage<ChannelInformation>> GetChannelInformationAsync(IReadOnlyList<string> broadcasterIds, CancellationToken cancellationToken = default)
        => Channels.GetChannelInformationAsync(broadcasterIds, cancellationToken);

    /// <summary>Requires user:write:chat; app tokens additionally require the documented bot grants. Pin requires moderator:manage:chat_messages.</summary>
    public Task<HelixPage<SendChatMessageResult>> SendChatMessageAsync(SendChatMessageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SenderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Message);
        if (request.ReplyParentMessageId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.ReplyParentMessageId);
        if (request.Message.EnumerateRunes().Count() > 500) throw new ArgumentException("Chat messages may contain at most 500 Unicode code points.", nameof(request));
        if (request.Pin == true && (request.ReplyParentMessageId is not null || request.ForSourceOnly.HasValue)) throw new ArgumentException("Pinned messages cannot include reply_parent_message_id or for_source_only.", nameof(request));
        return _transport.SendAsync(HttpMethod.Post, "chat/messages", HelixJsonContext.Default.HelixPageSendChatMessageResult,
            jsonBody: JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.SendChatMessageRequest),
            authorization: new(request.Pin == true ? [TwitchScopes.UserWriteChat, TwitchScopes.ModeratorManageChatMessages] : [TwitchScopes.UserWriteChat], true, request.SenderId, !request.ForSourceOnly.HasValue), cancellationToken: cancellationToken);
    }

    public Task<EventSubSubscriptionsResponse> CreateEventSubSubscriptionAsync(CreateEventSubSubscriptionRequest request, CancellationToken cancellationToken = default)
        => CreateEventSubSubscriptionAsync(request, null, cancellationToken);

    /// <param name="request">The subscription to create.</param>
    /// <param name="webSocketUserRequirement">Subscription-specific scopes and user that Twitch checks against the user token of a WebSocket
    /// subscription. App-token transports (webhook, conduit) cannot be preflighted for user grants, so it is ignored for them.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public Task<EventSubSubscriptionsResponse> CreateEventSubSubscriptionAsync(CreateEventSubSubscriptionRequest request,
        TwitchAuthorizationRequirement? webSocketUserRequirement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Type);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Version);
        ArgumentNullException.ThrowIfNull(request.Condition);
        ArgumentNullException.ThrowIfNull(request.Transport);
        switch (request.Transport.Method)
        {
            case "websocket":
                ArgumentException.ThrowIfNullOrWhiteSpace(request.Transport.SessionId);
                if (request.Transport.Callback is not null || request.Transport.Secret is not null || request.Transport.ConduitId is not null) throw new ArgumentException("WebSocket transport may only specify session_id.", nameof(request));
                break;
            case "webhook":
                if (request.Transport.SessionId is not null || request.Transport.ConduitId is not null) throw new ArgumentException("Webhook transport may only specify callback and secret.", nameof(request));
                if (!Uri.TryCreate(request.Transport.Callback, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443) throw new ArgumentException("Webhook callback must use HTTPS port 443.", nameof(request));
                if (request.Transport.Secret is not { Length: >= 10 and <= 100 } secret || secret.Any(c => c > 127)) throw new ArgumentException("Webhook secret must contain 10 to 100 ASCII characters.", nameof(request));
                break;
            case "conduit":
                ArgumentException.ThrowIfNullOrWhiteSpace(request.Transport.ConduitId);
                if (request.Transport.Callback is not null || request.Transport.Secret is not null || request.Transport.SessionId is not null) throw new ArgumentException("Conduit transport may only specify conduit_id.", nameof(request));
                break;
            default: throw new ArgumentException("Unknown EventSub transport.", nameof(request));
        }
        return _transport.SendAsync(HttpMethod.Post, "eventsub/subscriptions", HelixJsonContext.Default.EventSubSubscriptionsResponse,
            jsonBody: JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.CreateEventSubSubscriptionRequest),
            authorization: request.Transport.Method == "websocket"
                ? new(webSocketUserRequirement?.RequiredUserScopes ?? [], requiredUserId: webSocketUserRequirement?.RequiredUserId, anyUserScopes: webSocketUserRequirement?.AnyUserScopes)
                : new([], allowAppToken: true, allowUserToken: false), cancellationToken: cancellationToken);
    }

    public Task<EventSubSubscriptionsResponse> GetEventSubSubscriptionsAsync(GetEventSubSubscriptionsRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new();
        if (new[] { request.Status, request.Type, request.UserId, request.SubscriptionId, request.ConduitId }.Count(s => s is not null) > 1) throw new ArgumentException("EventSub subscription filters are mutually exclusive.", nameof(request));
        return _transport.SendAsync(HttpMethod.Get, "eventsub/subscriptions", HelixJsonContext.Default.EventSubSubscriptionsResponse,
            [new("status", request.Status), new("type", request.Type), new("user_id", request.UserId), new("subscription_id", request.SubscriptionId), new("conduit_id", request.ConduitId), new("after", request.After)], cancellationToken: cancellationToken);
    }

    public Task DeleteEventSubSubscriptionAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _transport.SendAsync(HttpMethod.Delete, "eventsub/subscriptions", [new("id", id)], cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<EventSubSubscription> EnumerateEventSubSubscriptionsAsync(GetEventSubSubscriptionsRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new();
        return HelixPagination.EnumerateAsync(async (cursor, ct) =>
        {
            var page = await GetEventSubSubscriptionsAsync(request with { After = cursor ?? request.After }, ct).ConfigureAwait(false);
            return new HelixPage<EventSubSubscription> { Data = page.Data, Pagination = page.Pagination, Total = page.Total };
        }, cancellationToken);
    }

}
