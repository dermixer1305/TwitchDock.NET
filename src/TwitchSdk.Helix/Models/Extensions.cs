using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwitchSdk.Core;

namespace TwitchSdk.Helix.Models;

/// <summary>Case-sensitive configuration segment names.</summary>
public static class ExtensionConfigurationSegmentTypes
{
    public const string Broadcaster = "broadcaster";
    public const string Developer = "developer";
    public const string Global = "global";
}

public sealed class GetExtensionConfigurationSegmentsRequest
{
    public required string ExtensionId { get; init; }
    /// <summary>One or more of broadcaster, developer and global. Twitch ignores duplicates and answers in request order.</summary>
    public IReadOnlyList<string> Segments { get; init; } = [];
    /// <summary>Required when broadcaster or developer is requested; omitted when only global is requested.</summary>
    public string? BroadcasterId { get; init; }
}

public sealed class ExtensionConfigurationSegment
{
    public required string Segment { get; init; }
    /// <summary>Present only for broadcaster and developer segments.</summary>
    public string? BroadcasterId { get; init; }
    /// <summary>Plain text or a string-encoded JSON object.</summary>
    public string Content { get; init; } = "";
    public string Version { get; init; } = "";
}

/// <summary>Null fields are omitted. Content is limited to 5 KB of UTF-8.</summary>
public sealed class SetExtensionConfigurationSegmentRequest
{
    public required string ExtensionId { get; init; }
    public required string Segment { get; init; }
    /// <summary>Required for broadcaster and developer segments; must be null for global.</summary>
    public string? BroadcasterId { get; init; }
    public string? Content { get; init; }
    /// <summary>When null, Twitch updates the latest segment definition.</summary>
    public string? Version { get; init; }
}

public sealed class SetExtensionRequiredConfigurationRequest
{
    public required string ExtensionId { get; init; }
    public required string ExtensionVersion { get; init; }
    public required string RequiredConfiguration { get; init; }
}

/// <summary>Target values for Send Extension PubSub Message.</summary>
public static class ExtensionPubSubTargets
{
    public const string Broadcast = "broadcast";
    public const string Global = "global";

    public static string Whisper(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return "whisper-" + userId;
    }
}

public sealed class SendExtensionPubSubMessageRequest
{
    /// <summary>broadcast, global or whisper-&lt;user-id&gt;. broadcast and global are mutually exclusive; global requires IsGlobalBroadcast.</summary>
    public IReadOnlyList<string> Target { get; init; } = [];
    /// <summary>Required unless IsGlobalBroadcast is true, in which case it must be null.</summary>
    public string? BroadcasterId { get; init; }
    /// <summary>Null omits the field; Twitch defaults to false.</summary>
    public bool? IsGlobalBroadcast { get; init; }
    /// <summary>Plain text or a string-encoded JSON object, at most 5 KB of UTF-8.</summary>
    public required string Message { get; init; }
}

public sealed record GetExtensionLiveChannelsRequest
{
    public required string ExtensionId { get; init; }
    public int? First { get; init; }
    public string? After { get; init; }
}

/// <summary>Unlike most Helix lists, pagination is documented as a bare cursor string. An empty or missing cursor means no more pages.</summary>
public sealed class ExtensionLiveChannelsResponse
{
    public IReadOnlyList<ExtensionLiveChannel> Data { get; init; } = [];
    [JsonConverter(typeof(CursorStringConverter))]
    public string? Pagination { get; init; }
}

public sealed class ExtensionLiveChannel
{
    public required string BroadcasterId { get; init; }
    public required string BroadcasterName { get; init; }
    public string GameName { get; init; } = "";
    public string GameId { get; init; } = "";
    /// <summary>May be empty.</summary>
    public string Title { get; init; } = "";
}

public sealed class ExtensionSecretSet
{
    public int FormatVersion { get; init; }
    public IReadOnlyList<ExtensionSharedSecret> Secrets { get; init; } = [];
}

/// <summary>A shared signing secret. <see cref="Content"/> is a credential and is redacted from <see cref="ToString"/>.</summary>
public sealed class ExtensionSharedSecret
{
    /// <summary>The base64 secret; pass it to <c>ExtensionSecret.FromBase64</c> to sign JWTs.</summary>
    public required string Content { get; init; }
    public DateTimeOffset ActiveAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"ExtensionSharedSecret {{ ActiveAt = {ActiveAt:O}, ExpiresAt = {ExpiresAt:O}, Content = [redacted] }}");
}

public sealed class SendExtensionChatMessageRequest
{
    /// <summary>At most 280 characters.</summary>
    public required string Text { get; init; }
    public required string ExtensionId { get; init; }
    public required string ExtensionVersion { get; init; }
}

/// <summary>An extension returned by Get Extensions and Get Released Extensions.</summary>
public sealed class TwitchExtension
{
    public required string AuthorName { get; init; }
    public bool BitsEnabled { get; init; }
    public bool CanInstall { get; init; }
    /// <summary>hosted, custom or none.</summary>
    public required string ConfigurationLocation { get; init; }
    public string Description { get; init; } = "";
    public string EulaTosUrl { get; init; } = "";
    public bool HasChatSupport { get; init; }
    public string IconUrl { get; init; } = "";
    /// <summary>Icon URLs keyed by size, for example 24x24.</summary>
    public IReadOnlyDictionary<string, string> IconUrls { get; init; } = new Dictionary<string, string>();
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string PrivacyPolicyUrl { get; init; } = "";
    public bool RequestIdentityLink { get; init; }
    public IReadOnlyList<string> ScreenshotUrls { get; init; } = [];
    /// <summary>Approved, AssetsUploaded, Deleted, Deprecated, InReview, InTest, PendingAction, Rejected or Released.</summary>
    public required string State { get; init; }
    /// <summary>none or optional.</summary>
    public required string SubscriptionsSupportLevel { get; init; }
    public string Summary { get; init; } = "";
    public string SupportEmail { get; init; } = "";
    public required string Version { get; init; }
    public string ViewerSummary { get; init; } = "";
    public ExtensionViews Views { get; init; } = new();
    public IReadOnlyList<string> AllowlistedConfigUrls { get; init; } = [];
    public IReadOnlyList<string> AllowlistedPanelUrls { get; init; } = [];
}

/// <summary>View definitions. A view is null when the extension does not define it.</summary>
public sealed class ExtensionViews
{
    public ExtensionMobileView? Mobile { get; init; }
    public ExtensionPanelView? Panel { get; init; }
    public ExtensionVideoOverlayView? VideoOverlay { get; init; }
    public ExtensionComponentView? Component { get; init; }
    public ExtensionConfigView? Config { get; init; }
}

public sealed class ExtensionMobileView
{
    public string ViewerUrl { get; init; } = "";
}

public sealed class ExtensionPanelView
{
    public string ViewerUrl { get; init; } = "";
    public int Height { get; init; }
    public bool CanLinkExternalContent { get; init; }
}

public sealed class ExtensionVideoOverlayView
{
    public string ViewerUrl { get; init; } = "";
    public bool CanLinkExternalContent { get; init; }
}

public sealed class ExtensionComponentView
{
    public string ViewerUrl { get; init; } = "";
    public int AspectRatioX { get; init; }
    public int AspectRatioY { get; init; }
    public bool Autoscale { get; init; }
    /// <summary>Ignored by Twitch when Autoscale is false.</summary>
    public int ScalePixels { get; init; }
    /// <summary>Percent (1–100) of the maximum video component height.</summary>
    public int TargetHeight { get; init; }
    public bool CanLinkExternalContent { get; init; }
}

public sealed class ExtensionConfigView
{
    public string ViewerUrl { get; init; } = "";
    public bool CanLinkExternalContent { get; init; }
}

public sealed class ExtensionBitsProduct
{
    public required string Sku { get; init; }
    public required ExtensionProductCost Cost { get; init; }
    public bool InDevelopment { get; init; }
    public required string DisplayName { get; init; }
    /// <summary>Null when the product does not expire (Twitch may send an empty string).</summary>
    [JsonConverter(typeof(EmptyStringAsNullDateTimeOffsetConverter))]
    public DateTimeOffset? Expiration { get; init; }
    public bool IsBroadcast { get; init; }
}

/// <summary>Adds or replaces the product with this SKU. Null optional fields are omitted so Twitch applies its defaults.</summary>
public sealed class UpdateExtensionBitsProductRequest
{
    /// <summary>1–255 characters: ASCII letters, digits, '-', '_' and '.'. Cannot be changed later.</summary>
    public required string Sku { get; init; }
    public required ExtensionBitsProductCost Cost { get; init; }
    /// <summary>At most 255 characters.</summary>
    public required string DisplayName { get; init; }
    public bool? InDevelopment { get; init; }
    /// <summary>Null means the product does not expire; a past date disables it.</summary>
    public DateTimeOffset? Expiration { get; init; }
    public bool? IsBroadcast { get; init; }
}

public sealed class ExtensionBitsProductCost
{
    /// <summary>1–10000 Bits.</summary>
    public required int Amount { get; init; }
    /// <summary>The currency; Twitch only accepts bits.</summary>
    public string Type { get; init; } = "bits";
}

/// <summary>Reads a cursor sent as a bare string (as documented) or as a standard <c>{"cursor": ...}</c> object, and writes a string.</summary>
internal sealed class CursorStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return reader.GetString();
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected a pagination cursor string or object.");
        string? cursor = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isCursor = reader.ValueTextEquals("cursor"u8);
            reader.Read();
            if (!isCursor) reader.Skip();
            else if (reader.TokenType == JsonTokenType.String) cursor = reader.GetString();
            else if (reader.TokenType != JsonTokenType.Null) throw new JsonException("Expected a string pagination cursor.");
        }
        return cursor;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value);
    }
}
