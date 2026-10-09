using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwitchSdk.Helix.Extensions;

/// <summary>
/// Creates compact HS256 JSON Web Tokens for Extension Backend Service (EBS) calls, signed with the decoded extension secret.
/// The returned strings are bearer credentials: do not log or persist them.
/// </summary>
public static class ExtensionJwt
{
    /// <summary>The <c>role</c> claim value that Twitch requires for EBS-signed Helix calls.</summary>
    public const string ExternalRole = "external";

    /// <summary>The <c>channel_id</c> claim value addressing every channel where the extension is active (global PubSub broadcast).</summary>
    public const string AllChannels = "all";

    /// <summary>Lifetime applied when none is specified. Short-lived tokens limit the impact of a leaked token.</summary>
    public static TimeSpan DefaultLifetime { get; } = TimeSpan.FromMinutes(3);

    /// <summary>The longest lifetime this SDK issues. Tokens are cheap to sign, so prefer issuing a new one per request.</summary>
    public static TimeSpan MaximumLifetime { get; } = TimeSpan.FromHours(1);

    private static readonly string EncodedHeader = Base64Url("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"u8);

    /// <summary>
    /// Creates a token with <c>exp</c>, <c>user_id</c> (the extension owner) and <c>role: "external"</c>.
    /// Supply <paramref name="channelId"/> for calls whose JWT must name a channel, such as Send Extension Chat Message.
    /// </summary>
    public static string CreateExternal(ExtensionSecret secret, string ownerUserId, string? channelId = null, TimeSpan? lifetime = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserId);
        if (channelId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(channelId);
        return Create(secret, ownerUserId, channelId, null, ComputeExpiry(lifetime, timeProvider));
    }

    /// <summary>
    /// Creates a token for Send Extension PubSub Message. <paramref name="channelId"/> is a channel ID with targets such as
    /// <c>broadcast</c> or <c>whisper-&lt;user-id&gt;</c>, or <see cref="AllChannels"/> with the single target <c>global</c>.
    /// The targets become <c>pubsub_perms.send</c>.
    /// </summary>
    public static string CreateForPubSub(ExtensionSecret secret, string ownerUserId, string channelId, IEnumerable<string> sendTargets, TimeSpan? lifetime = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);
        ArgumentNullException.ThrowIfNull(sendTargets);
        var targets = ExtensionPubSubTargetRules.Validate(sendTargets.ToArray(), channelId == AllChannels, nameof(sendTargets));
        return Create(secret, ownerUserId, channelId, targets, ComputeExpiry(lifetime, timeProvider));
    }

    internal static DateTimeOffset ComputeExpiry(TimeSpan? lifetime, TimeProvider? timeProvider)
    {
        var value = ValidateLifetime(lifetime);
        var expires = (timeProvider ?? TimeProvider.System).GetUtcNow() + value;
        // exp is a NumericDate in whole seconds; report the same truncated instant to callers.
        return DateTimeOffset.FromUnixTimeSeconds(expires.ToUnixTimeSeconds());
    }

    internal static TimeSpan ValidateLifetime(TimeSpan? lifetime)
    {
        var value = lifetime ?? DefaultLifetime;
        if (value < TimeSpan.FromSeconds(1) || value > MaximumLifetime)
            throw new ArgumentOutOfRangeException(nameof(lifetime), "The JWT lifetime must be between one second and one hour.");
        return value;
    }

    internal static string Create(ExtensionSecret secret, string ownerUserId, string? channelId, IReadOnlyList<string>? sendTargets, DateTimeOffset expiresAt)
    {
        var payload = new ExtensionJwtPayload
        {
            Exp = expiresAt.ToUnixTimeSeconds(),
            UserId = ownerUserId,
            Role = ExternalRole,
            ChannelId = channelId,
            PubsubPerms = sendTargets is null ? null : new ExtensionJwtPubSubPermissions { Send = sendTargets }
        };
        var signingInput = EncodedHeader + "." + Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload, ExtensionJwtJsonContext.Default.ExtensionJwtPayload));
        var signature = HMACSHA256.HashData(secret.Key, Encoding.ASCII.GetBytes(signingInput));
        return signingInput + "." + Base64Url(signature);
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Validation shared by the JWT helper and Send Extension PubSub Message.</summary>
internal static class ExtensionPubSubTargetRules
{
    private const string WhisperPrefix = "whisper-";

    public static string[] Validate(IReadOnlyList<string> targets, bool global, string parameter)
    {
        ArgumentNullException.ThrowIfNull(targets, parameter);
        if (targets.Count == 0) throw new ArgumentException("At least one PubSub target is required.", parameter);
        foreach (var target in targets)
        {
            if (target is "broadcast" or "global") continue;
            if (target is null || !target.StartsWith(WhisperPrefix, StringComparison.Ordinal) || target.Length == WhisperPrefix.Length || target.Any(char.IsWhiteSpace))
                throw new ArgumentException("PubSub targets must be broadcast, global or whisper-<user-id>.", parameter);
        }
        var distinct = targets.Distinct(StringComparer.Ordinal).ToArray();
        if (global && (distinct.Length != 1 || distinct[0] != "global"))
            throw new ArgumentException("A global broadcast must use the single target global.", parameter);
        if (!global && distinct.Contains("global", StringComparer.Ordinal))
            throw new ArgumentException("The global target requires a global broadcast to all channels.", parameter);
        return distinct;
    }
}

internal sealed class ExtensionJwtPayload
{
    public long Exp { get; init; }
    public required string UserId { get; init; }
    public required string Role { get; init; }
    public string? ChannelId { get; init; }
    public ExtensionJwtPubSubPermissions? PubsubPerms { get; init; }
}

internal sealed class ExtensionJwtPubSubPermissions
{
    public IReadOnlyList<string> Send { get; init; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ExtensionJwtPayload))]
internal sealed partial class ExtensionJwtJsonContext : JsonSerializerContext;
