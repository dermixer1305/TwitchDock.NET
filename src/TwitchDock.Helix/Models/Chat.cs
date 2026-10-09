namespace TwitchDock.Helix.Models;

public sealed class SendChatMessageRequest
{
    public required string BroadcasterId { get; init; }
    public required string SenderId { get; init; }
    public required string Message { get; init; }
    public string? ReplyParentMessageId { get; init; }
    public bool? ForSourceOnly { get; init; }
    public bool? Pin { get; init; }
}

public sealed class SendChatMessageResult
{
    public required string MessageId { get; init; }
    public bool IsSent { get; init; }
    public ChatDropReason? DropReason { get; init; }
}

public sealed class ChatDropReason
{
    public required string Code { get; init; }
    public required string Message { get; init; }
}
