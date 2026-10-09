using System.Text;
using System.Text.Json;
using TwitchDock.Core;
using TwitchDock.Helix.Extensions;
using TwitchDock.Helix.Models;

namespace TwitchDock.Helix.Clients;

/// <summary>
/// Extension endpoints. Methods marked "EBS JWT" must run on a client whose transport uses <see cref="ExtensionJwtTokenProvider"/>
/// and the extension's client ID; known OAuth tokens are rejected locally for them. The other methods use OAuth app or user tokens.
/// </summary>
public sealed class ExtensionsClient(TwitchHttpClient transport)
{
    private const string ConfigurationsPath = "extensions/configurations";
    private const string SecretsPath = "extensions/jwt/secrets";
    private const string BitsProductsPath = "bits/extensions";
    private const int MaximumContentBytes = 5 * 1024;
    private const int MinimumSecretDelaySeconds = 300;
    private const int MaximumBitsProductAmount = 10000;
    private const int MaximumBitsProductText = 255;

    // JWTs carry Unknown kind and pass; App/User OAuth tokens would be rejected by Twitch with 401 and needlessly refreshed.
    private static readonly TwitchAuthorizationRequirement ExtensionJwtOnly = new([], allowAppToken: false, allowUserToken: false);
    private static readonly TwitchAuthorizationRequirement AppOrUserToken = new([], allowAppToken: true);
    private static readonly TwitchAuthorizationRequirement ExtensionAppToken = new([], allowAppToken: true, allowUserToken: false);

    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    /// <summary>EBS JWT. Gets one or more configuration segments in request order. Twitch allows 20 reads per segment per minute.</summary>
    public Task<HelixPage<ExtensionConfigurationSegment>> GetExtensionConfigurationSegmentsAsync(GetExtensionConfigurationSegmentsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExtensionId);
        ArgumentNullException.ThrowIfNull(request.Segments);
        if (request.Segments.Count == 0) throw new ArgumentException("At least one configuration segment is required.", nameof(request));
        foreach (var segment in request.Segments) ValidateSegmentName(segment);
        ValidateSegmentBroadcaster(request.Segments.Any(RequiresBroadcaster), request.BroadcasterId);
        return _transport.SendAsync(HttpMethod.Get, ConfigurationsPath, HelixJsonContext.Default.HelixPageExtensionConfigurationSegment,
            new HelixQuery().AddValue("broadcaster_id", request.BroadcasterId).AddValue("extension_id", request.ExtensionId).AddValues("segment", request.Segments),
            authorization: ExtensionJwtOnly, cancellationToken: cancellationToken);
    }

    /// <summary>EBS JWT. Sets a configuration segment (at most 5 KB). Active extension instances do not receive the update.</summary>
    public Task SetExtensionConfigurationSegmentAsync(SetExtensionConfigurationSegmentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExtensionId);
        ValidateSegmentName(request.Segment);
        ValidateSegmentBroadcaster(RequiresBroadcaster(request.Segment), request.BroadcasterId);
        if (request.Content is not null) ValidateContentSize(request.Content, "Configuration content");
        if (request.Version is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.Version);
        return _transport.SendAsync(HttpMethod.Put, ConfigurationsPath,
            jsonBody: JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.SetExtensionConfigurationSegmentRequest),
            authorization: ExtensionJwtOnly, cancellationToken: cancellationToken);
    }

    /// <summary>EBS JWT (user_id = extension owner). Sets the required_configuration string a broadcaster must match before activation.</summary>
    public Task SetExtensionRequiredConfigurationAsync(string broadcasterId, SetExtensionRequiredConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExtensionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExtensionVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequiredConfiguration);
        return _transport.SendAsync(HttpMethod.Put, "extensions/required_configuration", new HelixQuery().AddValue("broadcaster_id", broadcasterId),
            JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.SetExtensionRequiredConfigurationRequest),
            authorization: ExtensionJwtOnly, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// EBS JWT. Sends a PubSub message to one channel or, with IsGlobalBroadcast, to all channels. <see cref="ExtensionJwtTokenProvider"/>
    /// adds channel_id (broadcaster or "all") and pubsub_perms.send (the targets) to this request's JWT. Limit: 100 per minute per channel.
    /// </summary>
    public Task SendExtensionPubSubMessageAsync(SendExtensionPubSubMessageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var global = request.IsGlobalBroadcast == true;
        if (global && request.BroadcasterId is not null) throw new ArgumentException("BroadcasterId must be omitted for a global broadcast.", nameof(request));
        if (!global) ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        var targets = ExtensionPubSubTargetRules.Validate(request.Target, global, nameof(request));
        ArgumentException.ThrowIfNullOrEmpty(request.Message);
        ValidateContentSize(request.Message, "The PubSub message");
        var body = JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.SendExtensionPubSubMessageRequest);
        return new ExtensionJwtRequestClaims(global ? ExtensionJwt.AllChannels : request.BroadcasterId!, targets).RunAsync(() =>
            _transport.SendAsync(HttpMethod.Post, "extensions/pubsub", jsonBody: body, authorization: ExtensionJwtOnly, cancellationToken: cancellationToken));
    }

    /// <summary>App or user token. Lists live broadcasters that installed or activated the extension; updates lag by a few minutes.</summary>
    public Task<ExtensionLiveChannelsResponse> GetExtensionLiveChannelsAsync(GetExtensionLiveChannelsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExtensionId);
        return _transport.SendAsync(HttpMethod.Get, "extensions/live", HelixJsonContext.Default.ExtensionLiveChannelsResponse,
            new HelixQuery().AddValue("extension_id", request.ExtensionId).AddPage(request.First, request.After),
            authorization: AppOrUserToken, cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<ExtensionLiveChannel> EnumerateExtensionLiveChannelsAsync(GetExtensionLiveChannelsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return HelixPagination.EnumerateAsync(async (cursor, ct) =>
        {
            var page = await GetExtensionLiveChannelsAsync(request with { After = cursor ?? request.After }, ct).ConfigureAwait(false);
            return new HelixPage<ExtensionLiveChannel> { Data = page.Data, Pagination = new() { Cursor = page.Pagination } };
        }, cancellationToken);
    }

    /// <summary>EBS JWT. Gets the extension's shared secrets. Secret content is a credential; never log it.</summary>
    public Task<HelixPage<ExtensionSecretSet>> GetExtensionSecretsAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        return _transport.SendAsync(HttpMethod.Get, SecretsPath, HelixJsonContext.Default.HelixPageExtensionSecretSet,
            new HelixQuery().AddValue("extension_id", extensionId), authorization: ExtensionJwtOnly, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// EBS JWT. Creates a new shared secret that activates after <paramref name="delaySeconds"/> (minimum and Twitch default 300) and
    /// takes the current secrets out of service. Not retried after ambiguous failures.
    /// </summary>
    public Task<HelixPage<ExtensionSecretSet>> CreateExtensionSecretAsync(string extensionId, int? delaySeconds = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        if (delaySeconds is < MinimumSecretDelaySeconds) throw new ArgumentOutOfRangeException(nameof(delaySeconds), "The activation delay must be at least 300 seconds.");
        return _transport.SendAsync(HttpMethod.Post, SecretsPath, HelixJsonContext.Default.HelixPageExtensionSecretSet,
            new HelixQuery().AddValue("extension_id", extensionId).AddValue("delay", delaySeconds), authorization: ExtensionJwtOnly, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// EBS JWT. Sends a chat message under the extension's name (requires Chat Capabilities). <see cref="ExtensionJwtTokenProvider"/> sets
    /// this request's channel_id claim to <paramref name="broadcasterId"/>. Limit: 12 messages per minute per channel.
    /// </summary>
    public Task SendExtensionChatMessageAsync(string broadcasterId, SendExtensionChatMessageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentNullException.ThrowIfNull(request);
        HelixValidation.Text(request.Text, 280, nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExtensionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExtensionVersion);
        var body = JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.SendExtensionChatMessageRequest);
        return new ExtensionJwtRequestClaims(broadcasterId, null).RunAsync(() =>
            _transport.SendAsync(HttpMethod.Post, "extensions/chat", new HelixQuery().AddValue("broadcaster_id", broadcasterId), body,
                authorization: ExtensionJwtOnly, cancellationToken: cancellationToken));
    }

    /// <summary>EBS JWT. Gets an extension; without a version, the latest released version (empty if never released).</summary>
    public Task<HelixPage<TwitchExtension>> GetExtensionsAsync(string extensionId, string? extensionVersion = null, CancellationToken cancellationToken = default)
        => GetExtensionAsync("extensions", extensionId, extensionVersion, ExtensionJwtOnly, cancellationToken);

    /// <summary>App or user token. Gets a released extension; without a version, the latest version.</summary>
    public Task<HelixPage<TwitchExtension>> GetReleasedExtensionsAsync(string extensionId, string? extensionVersion = null, CancellationToken cancellationToken = default)
        => GetExtensionAsync("extensions/released", extensionId, extensionVersion, AppOrUserToken, cancellationToken);

    /// <summary>App token whose client ID is the extension's. Lists products in ascending SKU order, optionally including disabled/expired ones.</summary>
    public Task<HelixPage<ExtensionBitsProduct>> GetExtensionBitsProductsAsync(bool? shouldIncludeAll = null, CancellationToken cancellationToken = default)
        => _transport.SendAsync(HttpMethod.Get, BitsProductsPath, HelixJsonContext.Default.HelixPageExtensionBitsProduct,
            new HelixQuery().AddValue("should_include_all", shouldIncludeAll), authorization: ExtensionAppToken, cancellationToken: cancellationToken);

    /// <summary>App token whose client ID is the extension's. Adds the product or replaces every field except the SKU.</summary>
    public Task<HelixPage<ExtensionBitsProduct>> UpdateExtensionBitsProductAsync(UpdateExtensionBitsProductRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Sku);
        if (request.Sku.Length > MaximumBitsProductText || !request.Sku.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            throw new ArgumentException("SKU must contain 1-255 ASCII letters, digits, '-', '_' or '.'.", nameof(request));
        ArgumentNullException.ThrowIfNull(request.Cost);
        if (request.Cost.Type != "bits") throw new ArgumentException("Cost type must be bits.", nameof(request));
        if (request.Cost.Amount is < 1 or > MaximumBitsProductAmount) throw new ArgumentOutOfRangeException(nameof(request), "Bits cost must be between 1 and 10000.");
        HelixValidation.Text(request.DisplayName, MaximumBitsProductText, nameof(request));
        return _transport.SendAsync(HttpMethod.Put, BitsProductsPath, HelixJsonContext.Default.HelixPageExtensionBitsProduct,
            jsonBody: JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.UpdateExtensionBitsProductRequest),
            authorization: ExtensionAppToken, cancellationToken: cancellationToken);
    }

    private Task<HelixPage<TwitchExtension>> GetExtensionAsync(string path, string extensionId, string? extensionVersion, TwitchAuthorizationRequirement authorization, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        if (extensionVersion is not null) ArgumentException.ThrowIfNullOrWhiteSpace(extensionVersion);
        return _transport.SendAsync(HttpMethod.Get, path, HelixJsonContext.Default.HelixPageTwitchExtension,
            new HelixQuery().AddValue("extension_id", extensionId).AddValue("extension_version", extensionVersion),
            authorization: authorization, cancellationToken: cancellationToken);
    }

    private static bool RequiresBroadcaster(string segment) => segment is ExtensionConfigurationSegmentTypes.Broadcaster or ExtensionConfigurationSegmentTypes.Developer;

    private static void ValidateSegmentName(string segment)
    {
        if (segment is not (ExtensionConfigurationSegmentTypes.Broadcaster or ExtensionConfigurationSegmentTypes.Developer or ExtensionConfigurationSegmentTypes.Global))
            throw new ArgumentException("Segment must be broadcaster, developer or global.", nameof(segment));
    }

    private static void ValidateSegmentBroadcaster(bool required, string? broadcasterId)
    {
        if (required) ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        else if (broadcasterId is not null) throw new ArgumentException("BroadcasterId must be omitted for the global segment.", nameof(broadcasterId));
    }

    private static void ValidateContentSize(string content, string description)
    {
        if (Encoding.UTF8.GetByteCount(content) > MaximumContentBytes)
            throw new ArgumentException($"{description} may contain at most 5 KB (5120 bytes) of UTF-8.", nameof(content));
    }
}
