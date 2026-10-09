using System.Text.Json;
using TwitchSdk.Core;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Helix.Clients;

public sealed partial class ModerationClient
{
    private const string BansPath = "moderation/bans";
    private const string UnbanRequestsPath = "moderation/unban_requests";
    private const string BlockedTermsPath = "moderation/blocked_terms";
    private const string AutoModSettingsPath = "moderation/automod/settings";
    private const int MaxAutoModLevel = 4;
    private const int MaxTimeoutSeconds = 1_209_600;
    private const int MaxReasonLength = 500;

    /// <summary>
    /// Checks 1–100 messages against the broadcaster's AutoMod settings and blocked terms. Requires the broadcaster's token
    /// (or an app token with the broadcaster's prior grant) with moderation:read. Twitch applies extra per-channel rate limits (HTTP 429).
    /// </summary>
    public Task<HelixPage<AutoModCheckResult>> CheckAutoModStatusAsync(CheckAutoModStatusRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        ArgumentNullException.ThrowIfNull(request.Data);
        if (request.Data.Count is < 1 or > 100) throw new ArgumentException("Between 1 and 100 messages are required.", nameof(request));
        foreach (var message in request.Data)
        {
            ArgumentNullException.ThrowIfNull(message, nameof(request));
            ArgumentException.ThrowIfNullOrWhiteSpace(message.MsgId, nameof(request));
            ArgumentException.ThrowIfNullOrWhiteSpace(message.MsgText, nameof(request));
        }
        return _transport.SendAsync(HttpMethod.Post, "moderation/enforcements/status", HelixJsonContext.Default.HelixPageAutoModCheckResult,
            new HelixQuery().AddValue("broadcaster_id", request.BroadcasterId),
            JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.CheckAutoModStatusRequest),
            authorization: new([TwitchScopes.ModerationRead], allowAppToken: true, requiredUserId: request.BroadcasterId), cancellationToken: cancellationToken);
    }

    /// <summary>Allows or denies a held message. UserId is the acting moderator and must match the token user.</summary>
    public Task ManageHeldAutoModMessageAsync(ManageHeldAutoModMessageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MsgId);
        if (request.Action is not "ALLOW" and not "DENY") throw new ArgumentException("Action must be ALLOW or DENY.", nameof(request));
        return _transport.SendAsync(HttpMethod.Post, "moderation/automod/message", query: null,
            jsonBody: JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.ManageHeldAutoModMessageRequest),
            authorization: new([TwitchScopes.ModeratorManageAutomod], allowAppToken: true, requiredUserId: request.UserId), cancellationToken: cancellationToken);
    }

    /// <summary>Accepts moderator:read:automod_settings or moderator:manage:automod_settings.</summary>
    public Task<HelixPage<AutoModSettings>> GetAutoModSettingsAsync(string broadcasterId, string moderatorId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moderatorId);
        return _transport.SendAsync(HttpMethod.Get, AutoModSettingsPath, HelixJsonContext.Default.HelixPageAutoModSettings, ModeratorQuery(broadcasterId, moderatorId),
            authorization: Moderator(moderatorId, anyOf: [TwitchScopes.ModeratorReadAutomodSettings, TwitchScopes.ModeratorManageAutomodSettings]),
            cancellationToken: cancellationToken);
    }

    /// <summary>Overwrites the settings (PUT). Set either OverallLevel or individual levels, not both; omitted individual levels are not preserved.</summary>
    public Task<HelixPage<AutoModSettings>> UpdateAutoModSettingsAsync(UpdateAutoModSettingsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModeratorId);
        ValidateAutoModLevels(request);
        return _transport.SendAsync(HttpMethod.Put, AutoModSettingsPath, HelixJsonContext.Default.HelixPageAutoModSettings,
            ModeratorQuery(request.BroadcasterId, request.ModeratorId),
            JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.UpdateAutoModSettingsRequest),
            authorization: Moderator(request.ModeratorId, TwitchScopes.ModeratorManageAutomodSettings), cancellationToken: cancellationToken);
    }

    /// <summary>Requires the broadcaster's token (or app grant) with moderation:read or moderator:manage:banned_users.</summary>
    public Task<HelixPage<BannedUser>> GetBannedUsersAsync(GetBannedUsersRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        return _transport.SendAsync(HttpMethod.Get, "moderation/banned", HelixJsonContext.Default.HelixPageBannedUser,
            new HelixQuery().AddValue("broadcaster_id", request.BroadcasterId).AddValues("user_id", request.UserIds).AddPage(request.First, request.After, request.Before),
            authorization: new([], allowAppToken: true, requiredUserId: request.BroadcasterId,
                anyUserScopes: [TwitchScopes.ModerationRead, TwitchScopes.ModeratorManageBannedUsers]), cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<BannedUser> EnumerateBannedUsersAsync(GetBannedUsersRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.UserIds);
        if (request.Before is not null) throw new ArgumentException("Forward enumeration cannot use before.", nameof(request));
        var snapshot = request with { UserIds = request.UserIds.ToArray() };
        return HelixPagination.EnumerateAsync((cursor, ct) => GetBannedUsersAsync(snapshot with { After = cursor ?? snapshot.After }, ct), cancellationToken);
    }

    /// <summary>
    /// Bans a user, or times them out when Duration is set. A timeout can be changed or turned into a ban, but a ban cannot become a timeout.
    /// Twitch returns 409 while another moderator changes the same user's ban state and 429 when the per-broadcaster limit is exceeded.
    /// </summary>
    public Task<HelixPage<BanUserResult>> BanUserAsync(BanUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModeratorId);
        ArgumentNullException.ThrowIfNull(request.Data);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Data.UserId);
        if (request.Data.Duration is < 1 or > MaxTimeoutSeconds)
            throw new ArgumentOutOfRangeException(nameof(request), "Timeout duration must be between 1 and 1209600 seconds.");
        ValidateMaxLength(request.Data.Reason, MaxReasonLength, nameof(request));
        return _transport.SendAsync(HttpMethod.Post, BansPath, HelixJsonContext.Default.HelixPageBanUserResult,
            ModeratorQuery(request.BroadcasterId, request.ModeratorId),
            JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.BanUserRequest),
            authorization: Moderator(request.ModeratorId, TwitchScopes.ModeratorManageBannedUsers), cancellationToken: cancellationToken);
    }

    /// <summary>Removes a ban or timeout. Twitch returns 400 when the user is not banned and 409 during concurrent ban changes.</summary>
    public Task UnbanUserAsync(string broadcasterId, string moderatorId, string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moderatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return _transport.SendAsync(HttpMethod.Delete, BansPath, ModeratorQuery(broadcasterId, moderatorId).AddValue("user_id", userId),
            authorization: Moderator(moderatorId, TwitchScopes.ModeratorManageBannedUsers), cancellationToken: cancellationToken);
    }

    /// <summary>User token only, with moderator:read:unban_requests or moderator:manage:unban_requests.</summary>
    public Task<HelixPage<UnbanRequest>> GetUnbanRequestsAsync(GetUnbanRequestsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModeratorId);
        if (request.Status is not "pending" and not "approved" and not "denied" and not "acknowledged" and not "canceled")
            throw new ArgumentException("Status must be pending, approved, denied, acknowledged or canceled.", nameof(request));
        if (request.UserId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId, nameof(request));
        // Twitch documents no maximum page size for this endpoint, so only non-positive values are rejected locally.
        if (request.First is < 1) throw new ArgumentOutOfRangeException(nameof(request), "Page size must be at least 1.");
        return _transport.SendAsync(HttpMethod.Get, UnbanRequestsPath, HelixJsonContext.Default.HelixPageUnbanRequest,
            ModeratorQuery(request.BroadcasterId, request.ModeratorId).AddValue("status", request.Status).AddValue("user_id", request.UserId)
                .AddValue("after", request.After).AddValue("first", request.First),
            authorization: new([], requiredUserId: request.ModeratorId,
                anyUserScopes: [TwitchScopes.ModeratorReadUnbanRequests, TwitchScopes.ModeratorManageUnbanRequests]), cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<UnbanRequest> EnumerateUnbanRequestsAsync(GetUnbanRequestsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var snapshot = request with { };
        return HelixPagination.EnumerateAsync((cursor, ct) => GetUnbanRequestsAsync(snapshot with { After = cursor ?? snapshot.After }, ct), cancellationToken);
    }

    /// <summary>Approves or denies an unban request. User token only, with moderator:manage:unban_requests.</summary>
    public Task<HelixPage<UnbanRequest>> ResolveUnbanRequestAsync(UnbanRequestResolution resolution, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolution.BroadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolution.ModeratorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolution.UnbanRequestId);
        if (resolution.Status is not "approved" and not "denied") throw new ArgumentException("Status must be approved or denied.", nameof(resolution));
        ValidateMaxLength(resolution.ResolutionText, MaxReasonLength, nameof(resolution));
        return _transport.SendAsync(HttpMethod.Patch, UnbanRequestsPath, HelixJsonContext.Default.HelixPageUnbanRequest,
            ModeratorQuery(resolution.BroadcasterId, resolution.ModeratorId).AddValue("unban_request_id", resolution.UnbanRequestId)
                .AddValue("status", resolution.Status).AddValue("resolution_text", resolution.ResolutionText),
            authorization: new([TwitchScopes.ModeratorManageUnbanRequests], requiredUserId: resolution.ModeratorId), cancellationToken: cancellationToken);
    }

    /// <summary>Returns non-private blocked terms, newest first. Accepts moderator:read:blocked_terms or moderator:manage:blocked_terms.</summary>
    public Task<HelixPage<BlockedTerm>> GetBlockedTermsAsync(GetBlockedTermsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModeratorId);
        return _transport.SendAsync(HttpMethod.Get, BlockedTermsPath, HelixJsonContext.Default.HelixPageBlockedTerm,
            ModeratorQuery(request.BroadcasterId, request.ModeratorId).AddPage(request.First, request.After),
            authorization: Moderator(request.ModeratorId, anyOf: [TwitchScopes.ModeratorReadBlockedTerms, TwitchScopes.ModeratorManageBlockedTerms]),
            cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<BlockedTerm> EnumerateBlockedTermsAsync(GetBlockedTermsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var snapshot = request with { };
        return HelixPagination.EnumerateAsync((cursor, ct) => GetBlockedTermsAsync(snapshot with { After = cursor ?? snapshot.After }, ct), cancellationToken);
    }

    /// <summary>Adds a blocked term. If the term already exists, Twitch returns the existing term.</summary>
    public Task<HelixPage<BlockedTerm>> AddBlockedTermAsync(AddBlockedTermRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModeratorId);
        HelixValidation.Text(request.Text, 500, nameof(request));
        if (request.Text.EnumerateRunes().Count() < 2) throw new ArgumentException("A blocked term must contain at least 2 characters.", nameof(request));
        if (HasEmbeddedWildcard(request.Text)) throw new ArgumentException("A wildcard (*) may appear only at the beginning or end of a word.", nameof(request));
        return _transport.SendAsync(HttpMethod.Post, BlockedTermsPath, HelixJsonContext.Default.HelixPageBlockedTerm,
            ModeratorQuery(request.BroadcasterId, request.ModeratorId),
            JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.AddBlockedTermRequest),
            authorization: Moderator(request.ModeratorId, TwitchScopes.ModeratorManageBlockedTerms), cancellationToken: cancellationToken);
    }

    /// <summary>Removes a blocked term. Twitch also returns 204 when the ID does not exist.</summary>
    public Task RemoveBlockedTermAsync(string broadcasterId, string moderatorId, string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moderatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _transport.SendAsync(HttpMethod.Delete, BlockedTermsPath, ModeratorQuery(broadcasterId, moderatorId).AddValue("id", id),
            authorization: Moderator(moderatorId, TwitchScopes.ModeratorManageBlockedTerms), cancellationToken: cancellationToken);
    }

    /// <summary>Deletes one chat message (Delete Chat Messages with message_id). The message must be under 6 hours old and not from the broadcaster or a moderator.</summary>
    public Task DeleteChatMessageAsync(string broadcasterId, string moderatorId, string messageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        return DeleteChatMessagesCoreAsync(broadcasterId, moderatorId, messageId, cancellationToken);
    }

    /// <summary>Clears all messages in the broadcaster's chat room (Delete Chat Messages without message_id).</summary>
    public Task DeleteAllChatMessagesAsync(string broadcasterId, string moderatorId, CancellationToken cancellationToken = default)
        => DeleteChatMessagesCoreAsync(broadcasterId, moderatorId, null, cancellationToken);

    private Task DeleteChatMessagesCoreAsync(string broadcasterId, string moderatorId, string? messageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(moderatorId);
        return _transport.SendAsync(HttpMethod.Delete, "moderation/chat", ModeratorQuery(broadcasterId, moderatorId).AddValue("message_id", messageId),
            authorization: Moderator(moderatorId, TwitchScopes.ModeratorManageChatMessages), cancellationToken: cancellationToken);
    }

    private static HelixQuery ModeratorQuery(string broadcasterId, string moderatorId)
        => new HelixQuery().AddValue("broadcaster_id", broadcasterId).AddValue("moderator_id", moderatorId);

    // Moderator endpoints accept the moderator's user token or an app token holding that moderator's prior grant.
    private static TwitchAuthorizationRequirement Moderator(string moderatorId, string scope) => new([scope], allowAppToken: true, requiredUserId: moderatorId);
    private static TwitchAuthorizationRequirement Moderator(string moderatorId, IEnumerable<string> anyOf)
        => new([], allowAppToken: true, requiredUserId: moderatorId, anyUserScopes: anyOf);

    private static void ValidateAutoModLevels(UpdateAutoModSettingsRequest request)
    {
        int?[] individual = [request.Disability, request.Aggression, request.SexualitySexOrGender, request.Misogyny, request.Bullying,
            request.Swearing, request.RaceEthnicityOrReligion, request.SexBasedTerms];
        var hasIndividual = individual.Any(level => level.HasValue);
        if (request.OverallLevel.HasValue == hasIndividual)
            throw new ArgumentException("Set either OverallLevel or one or more individual levels, but not both.", nameof(request));
        if (individual.Append(request.OverallLevel).Any(level => level is < 0 or > MaxAutoModLevel))
            throw new ArgumentOutOfRangeException(nameof(request), "AutoMod levels must be between 0 and 4.");
    }

    private static void ValidateMaxLength(string? value, int maximum, string parameter)
    {
        if (value?.EnumerateRunes().Count() > maximum) throw new ArgumentException($"Text may contain at most {maximum} Unicode code points.", parameter);
    }

    // Twitch accepts wildcards only at the beginning or end of a word, e.g. *foo or foo*, not f*oo.
    private static bool HasEmbeddedWildcard(string text)
    {
        var start = text.IndexOf('*');
        while (start >= 0)
        {
            var end = start;
            while (end < text.Length && text[end] == '*') end++;
            if (start > 0 && !char.IsWhiteSpace(text[start - 1]) && end < text.Length && !char.IsWhiteSpace(text[end])) return true;
            start = end < text.Length ? text.IndexOf('*', end) : -1;
        }
        return false;
    }
}
