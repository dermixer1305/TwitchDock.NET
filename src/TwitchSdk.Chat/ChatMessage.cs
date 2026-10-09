using System.Text.Json.Serialization;

namespace TwitchSdk.Chat;

public sealed class ChatMessage
{
    public required string BroadcasterUserId { get; init; }
    public required string BroadcasterUserLogin { get; init; }
    public required string BroadcasterUserName { get; init; }
    public required string ChatterUserId { get; init; }
    public required string ChatterUserLogin { get; init; }
    public required string ChatterUserName { get; init; }
    public required string MessageId { get; init; }
    public required ChatMessageContent Message { get; init; }
    public string Color { get; init => field = value ?? ""; } = "";
    public IReadOnlyList<ChatBadge> Badges { get; init => field = value ?? []; } = [];
    public string MessageType { get; init => field = value ?? ""; } = "";
    public ChatCheer? Cheer { get; init; }
    public ChatReply? Reply { get; init; }
    public string? ChannelPointsCustomRewardId { get; init; }
    public string? SourceBroadcasterUserId { get; init; }
    public string? SourceBroadcasterUserLogin { get; init; }
    public string? SourceBroadcasterUserName { get; init; }
    public string? SourceMessageId { get; init; }
    public IReadOnlyList<ChatBadge>? SourceBadges { get; init; }
    public bool? IsSourceOnly { get; init; }
}

public sealed class ChatMessageContent
{
    public required string Text { get; init; }
    public IReadOnlyList<ChatFragment> Fragments { get; init => field = value ?? []; } = [];
}

public sealed class ChatFragment
{
    public required string Type { get; init; }
    public required string Text { get; init; }
    public ChatCheermote? Cheermote { get; init; }
    public ChatEmote? Emote { get; init; }
    public ChatMention? Mention { get; init; }
    public ChatGif? Gif { get; init; }
}

public sealed class ChatGif
{
    public required string Id { get; init; }
    public required string Url { get; init; }
}

public sealed class ChatBadge
{
    public required string SetId { get; init; }
    public required string Id { get; init; }
    public string Info { get; init => field = value ?? ""; } = "";
}
public sealed class ChatCheer { public int Bits { get; init; } }
public sealed class ChatCheermote
{
    public required string Prefix { get; init; }
    public int Bits { get; init; }
    public int Tier { get; init; }
}
public sealed class ChatEmote
{
    public required string Id { get; init; }
    public required string EmoteSetId { get; init; }
    public required string OwnerId { get; init; }
    public IReadOnlyList<string> Format { get; init => field = value ?? []; } = [];
}
public sealed class ChatMention
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string UserLogin { get; init; }
}
public sealed class ChatReply
{
    public required string ParentMessageId { get; init; }
    public required string ParentMessageBody { get; init; }
    public required string ParentUserId { get; init; }
    public required string ParentUserName { get; init; }
    public required string ParentUserLogin { get; init; }
    public required string ThreadMessageId { get; init; }
    public required string ThreadUserId { get; init; }
    public required string ThreadUserName { get; init; }
    public required string ThreadUserLogin { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ChatMessage))]
public partial class ChatJsonContext : JsonSerializerContext;
