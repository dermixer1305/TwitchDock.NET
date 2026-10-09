using System.Diagnostics.CodeAnalysis;
using TwitchSdk.Core;
using TwitchSdk.EventSub;
using TwitchSdk.EventSub.Events;
using TwitchSdk.Helix;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Chat;

public sealed class TwitchChatClient(HelixClient helix)
{
    private readonly HelixClient _helix = helix ?? throw new ArgumentNullException(nameof(helix));

    public Task<HelixPage<SendChatMessageResult>> SendAsync(SendChatMessageRequest request, CancellationToken cancellationToken = default)
        => _helix.SendChatMessageAsync(request, cancellationToken);

    /// <summary>
    /// Subscribes a WebSocket session to channel.chat.message v1 after its welcome message. Requires a user token for <paramref name="userId"/>
    /// with user:read:chat; the scope and user are preflighted before the request is sent.
    /// </summary>
    public Task<EventSubSubscriptionsResponse> SubscribeAsync(string broadcasterId, string userId, string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return _helix.SubscribeWebSocketAsync(EventSubSubscriptions.ChannelChatMessageV1(broadcasterId, userId), sessionId, cancellationToken);
    }

    /// <summary>
    /// Reads a channel.chat.message v1 notification. Returns false for other types, versions, and revocations; retain those in the host's
    /// EventSub handler.
    /// </summary>
    public static bool TryReadMessage(EventSubMessage message, [NotNullWhen(true)] out ChannelChatMessageEvent? chatMessage)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.TryReadEvent(EventSubEvents.ChannelChatMessageV1, out chatMessage);
    }
}
