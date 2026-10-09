using System.Text.Json.Serialization;
using TwitchSdk.Core;

namespace TwitchSdk.Helix.Models;

public sealed record GetChattersRequest
{
    public required string BroadcasterId { get; init; }
    /// <summary>The broadcaster or one of their moderators; must match the token user.</summary>
    public required string ModeratorId { get; init; }
    /// <summary>Page size between 1 and 1000. Twitch defaults to 100.</summary>
    public int? First { get; init; }
    public string? After { get; init; }
}

public sealed class Chatter
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string UserName { get; init; }
}

/// <summary>Static light-theme emote images. Twitch recommends building URLs from the response template instead.</summary>
public sealed class EmoteImages
{
    [JsonPropertyName("url_1x")]
    public required string Url1x { get; init; }
    [JsonPropertyName("url_2x")]
    public required string Url2x { get; init; }
    [JsonPropertyName("url_4x")]
    public required string Url4x { get; init; }
}

/// <summary>Emote list with the CDN URL template that applies to every emote in it.</summary>
public sealed class ChatEmotesResponse<TEmote>
{
    public IReadOnlyList<TEmote> Data { get; init; } = [];
    /// <summary>Replace {{id}}, {{format}}, {{theme_mode}} and {{scale}} to build a CDN URL.</summary>
    public required string Template { get; init; }
}

public sealed class ChannelEmote
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required EmoteImages Images { get; init; }
    /// <summary>Subscriber tier for subscriptions emotes; otherwise an empty string.</summary>
    public string Tier { get; init; } = "";
    /// <summary>bitstier, follower or subscriptions.</summary>
    public required string EmoteType { get; init; }
    public required string EmoteSetId { get; init; }
    /// <summary>animated and/or static.</summary>
    public IReadOnlyList<string> Format { get; init; } = [];
    /// <summary>1.0, 2.0 and/or 3.0.</summary>
    public IReadOnlyList<string> Scale { get; init; } = [];
    /// <summary>dark and/or light.</summary>
    public IReadOnlyList<string> ThemeMode { get; init; } = [];
}

public sealed class GlobalEmote
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required EmoteImages Images { get; init; }
    public IReadOnlyList<string> Format { get; init; } = [];
    public IReadOnlyList<string> Scale { get; init; } = [];
    public IReadOnlyList<string> ThemeMode { get; init; } = [];
}

public sealed class EmoteSetEmote
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required EmoteImages Images { get; init; }
    public required string EmoteType { get; init; }
    public required string EmoteSetId { get; init; }
    public required string OwnerId { get; init; }
    public IReadOnlyList<string> Format { get; init; } = [];
    public IReadOnlyList<string> Scale { get; init; } = [];
    public IReadOnlyList<string> ThemeMode { get; init; } = [];
}

public sealed record GetUserEmotesRequest
{
    /// <summary>Must match the token user.</summary>
    public required string UserId { get; init; }
    /// <summary>Guarantees that this broadcaster's follower emotes are included.</summary>
    public string? BroadcasterId { get; init; }
    public string? After { get; init; }
}

public sealed class UserEmotesResponse
{
    public IReadOnlyList<UserEmote> Data { get; init; } = [];
    public required string Template { get; init; }
    public Pagination? Pagination { get; init; }
}

public sealed class UserEmote
{
    public required string Id { get; init; }
    /// <summary>Case-sensitive emote name.</summary>
    public required string Name { get; init; }
    /// <summary>Evolving discriminator such as none, bitstier, follower, subscriptions, channelpoints, rewards, hypetrain, prime, turbo, smilies, globals, owl2019, twofactor or limitedtime.</summary>
    public required string EmoteType { get; init; }
    /// <summary>Empty when the emote does not belong to a set.</summary>
    public string EmoteSetId { get; init; } = "";
    /// <summary>Empty when the emote has no owner.</summary>
    public string OwnerId { get; init; } = "";
    public IReadOnlyList<string> Format { get; init; } = [];
    public IReadOnlyList<string> Scale { get; init; } = [];
    public IReadOnlyList<string> ThemeMode { get; init; } = [];
}

public sealed class ChatBadgeSet
{
    public required string SetId { get; init; }
    public IReadOnlyList<ChatBadgeVersion> Versions { get; init; } = [];
}

public sealed class ChatBadgeVersion
{
    public required string Id { get; init; }
    [JsonPropertyName("image_url_1x")]
    public required string ImageUrl1x { get; init; }
    [JsonPropertyName("image_url_2x")]
    public required string ImageUrl2x { get; init; }
    [JsonPropertyName("image_url_4x")]
    public required string ImageUrl4x { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    /// <summary>Null when the badge has no click action.</summary>
    public string? ClickAction { get; init; }
    /// <summary>Null when the badge has no click URL.</summary>
    public string? ClickUrl { get; init; }
}
