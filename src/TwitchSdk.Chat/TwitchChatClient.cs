using TwitchSdk.Core;
using TwitchSdk.EventSub;
using TwitchSdk.Helix;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Chat;

public sealed class TwitchChatClient(HelixClient helix)
{
    private readonly HelixClient _helix = helix ?? throw new ArgumentNullException(nameof(helix));

    public Task<HelixPage<SendChatMessageResult>> SendAsync(SendChatMessageRequest request, CancellationToken cancellationToken = default)
        => _helix.SendChatMessageAsync(request, cancellationToken);

    /// <summary>Subscribe after a WebSocket welcome using a user token with user:read:chat.</summary>
    public Task<EventSubSubscriptionsResponse> SubscribeAsync(string broadcasterId, string userId, string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return _helix.CreateEventSubSubscriptionAsync(new()
        {
            Type = "channel.chat.message", Version = "1",
            Condition = new Dictionary<string, string> { ["broadcaster_user_id"] = broadcasterId, ["user_id"] = userId },
            Transport = new() { Method = "websocket", SessionId = sessionId }
        }, cancellationToken);
    }

    /// <summary>Returns false for other types, versions, and revocations; retain those in the host's EventSub handler.</summary>
    public static bool TryReadMessage(EventSubMessage message, out ChatMessage? chatMessage)
    {
        ArgumentNullException.ThrowIfNull(message);
        chatMessage = null;
        if (message.Metadata.MessageType != "notification" || message.Metadata.SubscriptionType != "channel.chat.message" || message.Metadata.SubscriptionVersion != "1") return false;
        chatMessage = message.ReadEvent(ChatJsonContext.Default.ChatMessage);
        return true;
    }
}
