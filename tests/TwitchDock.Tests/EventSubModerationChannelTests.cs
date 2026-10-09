using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwitchDock.Core;
using TwitchDock.EventSub;
using TwitchDock.EventSub.Events;
using TwitchDock.Helix;
using TwitchDock.Helix.Models;

namespace TwitchDock.Tests;

public sealed class EventSubModerationChannelTests
{
    private const string FixtureFile = "eventsub-moderation-channel.json";
    private const string Accepted = """{"data":[],"total":1,"total_cost":0,"max_total_cost":10}""";

    private static readonly string[] GroupIds =
    [
        "channel.update@2", "channel.follow@2", "channel.ad_break.begin@1", "channel.raid@1", "channel.ban@1", "channel.unban@1",
        "channel.unban_request.create@1", "channel.unban_request.resolve@1", "channel.moderate@1", "channel.moderate@2",
        "channel.moderator.add@1", "channel.moderator.remove@1", "channel.vip.add@1", "channel.vip.remove@1",
        "channel.shield_mode.begin@1", "channel.shield_mode.end@1", "channel.shoutout.create@1", "channel.shoutout.receive@1",
    ];

    private static HelixClient Helix(HttpClient http, AccessToken token)
        => new(new(http, new StaticAccessTokenProvider(token), new() { ClientId = "client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 }));

    private static AccessToken User(string userId, params string[] scopes) => new("user", scopes: scopes, kind: TwitchTokenKind.User, userId: userId);

    /// <summary>Round-trips an extra fixture variant keyed type@version#variant, keeping nulls so omitted model fields fail.</summary>
    private static TEvent VerifyVariant<TEvent>(EventSubEventDefinition<TEvent> definition, string variant) where TEvent : class
    {
        using var expected = JsonDocument.Parse(ContractAssertions.Fixture(FixtureFile, $"{definition}#{variant}"));
        var model = definition.Deserialize(expected.RootElement);
        var context = new EventSubEventsJsonContext(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.Never });
        using var actual = JsonDocument.Parse(JsonSerializer.Serialize(model, context.GetTypeInfo(typeof(TEvent))!));
        ContractAssertions.AssertSubset(expected.RootElement, actual.RootElement);
        return model;
    }

    private static TEvent Read<TEvent>(EventSubEventDefinition<TEvent> definition, string json) where TEvent : class
    {
        using var document = JsonDocument.Parse(json);
        return definition.Deserialize(document.RootElement);
    }

    private static EventSubMessage Notification(string type, string version, string eventJson) => EventSubMessage.Parse(Encoding.UTF8.GetBytes(
        $$$"""{"metadata":{"message_id":"m1","message_type":"notification","message_timestamp":"2026-10-09T12:00:00Z","subscription_type":"{{{type}}}","subscription_version":"{{{version}}}"},"payload":{"subscription":{"id":"s1","status":"enabled","type":"{{{type}}}","version":"{{{version}}}","condition":{"broadcaster_user_id":"1337","moderator_user_id":"1338"},"transport":{"method":"websocket","session_id":"x"},"created_at":"2026-10-09T12:00:00Z","cost":0},"event":{{{eventJson}}}}}"""));

    [Fact]
    public void ChannelEventContractsPreserveEveryDocumentedField()
    {
        var update = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelUpdateV2);
        Assert.Equal(["MatureGame"], update.ContentClassificationLabels);
        Assert.Equal(DateTimeOffset.Parse("2020-07-15T18:16:11.1710671Z"), EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelFollowV2).FollowedAt);
        var ad = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelAdBreakBeginV1);
        Assert.Equal((60, false, "1337"), (ad.DurationSeconds, ad.IsAutomatic, ad.RequesterUserId));
        var raid = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelRaidV1);
        Assert.Equal(("1234", "1337", 9001), (raid.FromBroadcasterUserId, raid.ToBroadcasterUserId, raid.Viewers));
        Assert.Equal("1339", EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelUnbanV1).ModeratorUserId);
        var create = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelUnbanRequestCreateV1);
        Assert.Equal(("60", "unban me"), (create.Id, create.Text));
        Assert.Equal("1234", EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelModeratorAddV1).UserId);
        Assert.Equal("not_mod_user", EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelModeratorRemoveV1).UserLogin);
        Assert.Equal("1234", EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelVipAddV1).UserId);
        Assert.Equal("1337", EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelVipRemoveV1).BroadcasterUserId);
        Assert.Equal(DateTimeOffset.Parse("2022-07-26T17:00:03.1710671Z"), EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelShieldModeBeginV1).StartedAt);
        Assert.Equal(DateTimeOffset.Parse("2022-07-27T01:30:23.1710671Z"), EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelShieldModeEndV1).EndedAt);
        var shoutout = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelShoutoutCreateV1);
        Assert.Equal(("626262", 860), (shoutout.ToBroadcasterUserId, shoutout.ViewerCount));
        Assert.Equal(DateTimeOffset.Parse("2022-07-26T18:00:03.1710671Z"), shoutout.TargetCooldownEndsAt);
        var received = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelShoutoutReceiveV1);
        Assert.Equal(("12345", 860), (received.FromBroadcasterUserId, received.ViewerCount));
    }

    [Fact]
    public void ChannelBanEndsAtIsNullOnlyForPermanentBans()
    {
        var timeout = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelBanV1);
        Assert.False(timeout.IsPermanent);
        Assert.Equal(DateTimeOffset.Parse("2020-07-15T18:16:11.1710671Z"), timeout.EndsAt);
        var permanent = VerifyVariant(EventSubEvents.ChannelBanV1, "permanent");
        Assert.True(permanent.IsPermanent);
        Assert.Null(permanent.EndsAt);
        Assert.Equal("", permanent.Reason);
        var emptyString = Read(EventSubEvents.ChannelBanV1, ContractAssertions.Fixture(FixtureFile, "channel.ban@1#permanent").Replace("\"ends_at\": null", "\"ends_at\": \"\"", StringComparison.Ordinal));
        Assert.Null(emptyString.EndsAt);
    }

    [Fact]
    public void AdBreakAcceptsTheQuotedValuesOfTheOfficialExample()
    {
        var ad = Read(EventSubEvents.ChannelAdBreakBeginV1, """{"duration_seconds":"60","started_at":"2019-11-16T10:11:12.634234626Z","is_automatic":"true","broadcaster_user_id":"1337","broadcaster_user_login":"cool_user","broadcaster_user_name":"Cool_User","requester_user_id":"1337","requester_user_login":"cool_user","requester_user_name":"Cool_User"}""");
        Assert.Equal((60, true), (ad.DurationSeconds, ad.IsAutomatic));
        Assert.Throws<JsonException>(() => Read(EventSubEvents.ChannelAdBreakBeginV1, """{"is_automatic":"maybe","broadcaster_user_id":"1","broadcaster_user_login":"a","broadcaster_user_name":"A","requester_user_id":"1","requester_user_login":"a","requester_user_name":"A"}"""));
    }

    [Fact]
    public void UnbanRequestResolveReadsBothDocumentedModeratorSpellings()
    {
        var example = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelUnbanRequestResolveV1);
        Assert.Equal(("1337", "denied", "no"), (example.ModeratorUserId, example.Status, example.ResolutionText));
        Assert.Null(example.ModeratorId);
        var table = VerifyVariant(EventSubEvents.ChannelUnbanRequestResolveV1, "field-table");
        Assert.Equal(("1338", "mod_user", "Mod_User", "canceled"), (table.ModeratorId, table.ModeratorLogin, table.ModeratorName, table.Status));
        Assert.Null(table.ModeratorUserId);
        Assert.Null(table.ResolutionText);
    }

    [Fact]
    public void ChannelModerateV1ModelsEveryActionObject()
    {
        var mod = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelModerateV1);
        Assert.Equal(("mod", "141981764"), (mod.Action, mod.Mod!.UserId));
        Assert.Null(mod.SourceBroadcasterUserId);
        Assert.Null(mod.Ban);

        var shared = VerifyVariant(EventSubEvents.ChannelModerateV1, "shared_chat_timeout");
        Assert.Equal(("41292030", "Does not like pineapple on pizza."), (shared.SourceBroadcasterUserId, shared.SharedChatTimeout!.Reason));
        Assert.Equal(DateTimeOffset.Parse("2022-03-15T02:00:28Z"), shared.SharedChatTimeout.ExpiresAt);
        Assert.Null(shared.Timeout);

        var all = VerifyVariant(EventSubEvents.ChannelModerateV1, "every-object");
        Assert.Equal((10, 30), (all.Followers!.FollowDurationMinutes, all.Slow!.WaitTimeSeconds));
        Assert.Equal(("2001", "2002", "2003", "2004"), (all.Vip!.UserId, all.Unvip!.UserId, all.Mod!.UserId, all.Unmod!.UserId));
        Assert.Equal(("spam", "2006", "2008", "2010"), (all.Ban!.Reason, all.Unban!.UserId, all.Untimeout!.UserId, all.Unraid!.UserId));
        Assert.Null(all.Timeout!.Reason);
        Assert.Equal(4242, all.Raid!.ViewerCount);
        Assert.Equal("bad words", all.Delete!.MessageBody);
        Assert.Equal(("add", "blocked", true), (all.AutomodTerms!.Action, all.AutomodTerms.List, all.AutomodTerms.FromAutomod));
        Assert.Equal(["foo", "bar"], all.AutomodTerms.Terms);
        Assert.Equal((true, "ok"), (all.UnbanRequest!.IsApproved, all.UnbanRequest.ModeratorMessage));
        Assert.Equal(("2013", "2014", "2016", "shared bad words"), (all.SharedChatBan!.UserId, all.SharedChatUnban!.UserId, all.SharedChatUntimeout!.UserId, all.SharedChatDelete!.MessageBody));
        Assert.Equal(DateTimeOffset.Parse("2024-02-23T21:32:33Z"), all.SharedChatTimeout!.ExpiresAt);
    }

    [Fact]
    public void ChannelModerateV2AddsWarnings()
    {
        var warn = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelModerateV2);
        Assert.Equal(("warn", "cut it out"), (warn.Action, warn.Warn!.Reason));
        Assert.Null(warn.Warn.ChatRulesCited);

        var timeout = VerifyVariant(EventSubEvents.ChannelModerateV2, "timeout");
        Assert.Equal(DateTimeOffset.Parse("2022-03-15T02:00:28Z"), timeout.Timeout!.ExpiresAt);
        Assert.Null(timeout.SourceBroadcasterUserId);
        Assert.Null(timeout.Warn);

        var all = VerifyVariant(EventSubEvents.ChannelModerateV2, "every-object");
        Assert.Equal(["No spam", "Be kind"], all.Warn!.ChatRulesCited!);
        Assert.Equal(("remove", "permitted", false), (all.AutomodTerms!.Action, all.AutomodTerms.List, all.AutomodTerms.FromAutomod));
        Assert.Null(all.UnbanRequest!.ModeratorMessage);
        Assert.Null(all.SharedChatTimeout!.Reason);
        Assert.IsAssignableFrom<ChannelModerateEventBase>(all);
    }

    [Fact]
    public void RegistryResolvesEveryModerationChannelDefinition()
    {
        foreach (var id in GroupIds)
        {
            var (type, version) = (id[..id.IndexOf('@', StringComparison.Ordinal)], id[(id.IndexOf('@', StringComparison.Ordinal) + 1)..]);
            Assert.True(EventSubEvents.TryGetDefinition(type, version, out var definition), id);
            Assert.Equal(id, definition.ToString());
            Assert.Contains(definition, EventSubEvents.All);
        }
        Assert.True(EventSubEvents.TryGetDefinition("channel.moderate", "2", out var moderate));
        Assert.Same(EventSubEvents.ChannelModerateV2, moderate);
        Assert.Equal(typeof(ChannelModerateEventV2), moderate.EventType);
    }

    [Fact]
    public void FactoriesBuildDocumentedConditionsAndAuthorization()
    {
        string[] none = [];
        string[] unbanRequests = [TwitchScopes.ModeratorReadUnbanRequests, TwitchScopes.ModeratorManageUnbanRequests];
        string[] moderateFixed = [TwitchScopes.ModeratorReadModerators, TwitchScopes.ModeratorReadVips];
        string[] moderateV1Pairs =
        [
            TwitchScopes.ModeratorReadBlockedTerms, TwitchScopes.ModeratorManageBlockedTerms, TwitchScopes.ModeratorReadChatSettings, TwitchScopes.ModeratorManageChatSettings,
            TwitchScopes.ModeratorReadUnbanRequests, TwitchScopes.ModeratorManageUnbanRequests, TwitchScopes.ModeratorReadBannedUsers, TwitchScopes.ModeratorManageBannedUsers,
            TwitchScopes.ModeratorReadChatMessages, TwitchScopes.ModeratorManageChatMessages,
        ];
        string[] moderateV2Pairs = [.. moderateV1Pairs, TwitchScopes.ModeratorReadWarnings, TwitchScopes.ModeratorManageWarnings];
        string[] vips = [TwitchScopes.ChannelReadVips, TwitchScopes.ChannelManageVips];
        string[] shield = [TwitchScopes.ModeratorReadShieldMode, TwitchScopes.ModeratorManageShieldMode];
        string[] shoutouts = [TwitchScopes.ModeratorReadShoutouts, TwitchScopes.ModeratorManageShoutouts];
        var broadcaster = new Dictionary<string, string> { ["broadcaster_user_id"] = "1337" };
        var moderated = new Dictionary<string, string> { ["broadcaster_user_id"] = "1337", ["moderator_user_id"] = "1338" };

        AssertSpec(EventSubSubscriptions.ChannelUpdateV2("1337"), "channel.update@2", broadcaster, none, none, null);
        AssertSpec(EventSubSubscriptions.ChannelFollowV2("1337", "1338"), "channel.follow@2", moderated, [TwitchScopes.ModeratorReadFollowers], none, "1338");
        AssertSpec(EventSubSubscriptions.ChannelAdBreakBeginV1("1337"), "channel.ad_break.begin@1", broadcaster, [TwitchScopes.ChannelReadAds], none, "1337");
        AssertSpec(EventSubSubscriptions.ChannelRaidFromBroadcasterV1("1234"), "channel.raid@1", new Dictionary<string, string> { ["from_broadcaster_user_id"] = "1234" }, none, none, null);
        AssertSpec(EventSubSubscriptions.ChannelRaidToBroadcasterV1("1337"), "channel.raid@1", new Dictionary<string, string> { ["to_broadcaster_user_id"] = "1337" }, none, none, null);
        AssertSpec(EventSubSubscriptions.ChannelBanV1("1337"), "channel.ban@1", broadcaster, [TwitchScopes.ChannelModerate], none, "1337");
        AssertSpec(EventSubSubscriptions.ChannelUnbanV1("1337"), "channel.unban@1", broadcaster, [TwitchScopes.ChannelModerate], none, "1337");
        AssertSpec(EventSubSubscriptions.ChannelUnbanRequestCreateV1("1337", "1338"), "channel.unban_request.create@1", moderated, none, unbanRequests, "1338");
        AssertSpec(EventSubSubscriptions.ChannelUnbanRequestResolveV1("1337", "1338"), "channel.unban_request.resolve@1", moderated, none, unbanRequests, "1338");
        AssertSpec(EventSubSubscriptions.ChannelModerateV1("1337", "1338"), "channel.moderate@1", moderated, moderateFixed, moderateV1Pairs, "1338");
        AssertSpec(EventSubSubscriptions.ChannelModerateV2("1337", "1338"), "channel.moderate@2", moderated, moderateFixed, moderateV2Pairs, "1338");
        AssertSpec(EventSubSubscriptions.ChannelModeratorAddV1("1337"), "channel.moderator.add@1", broadcaster, [TwitchScopes.ModerationRead], none, "1337");
        AssertSpec(EventSubSubscriptions.ChannelModeratorRemoveV1("1337"), "channel.moderator.remove@1", broadcaster, [TwitchScopes.ModerationRead], none, "1337");
        AssertSpec(EventSubSubscriptions.ChannelVipAddV1("1337"), "channel.vip.add@1", broadcaster, none, vips, "1337");
        AssertSpec(EventSubSubscriptions.ChannelVipRemoveV1("1337"), "channel.vip.remove@1", broadcaster, none, vips, "1337");
        AssertSpec(EventSubSubscriptions.ChannelShieldModeBeginV1("1337", "1338"), "channel.shield_mode.begin@1", moderated, none, shield, "1338");
        AssertSpec(EventSubSubscriptions.ChannelShieldModeEndV1("1337", "1338"), "channel.shield_mode.end@1", moderated, none, shield, "1338");
        AssertSpec(EventSubSubscriptions.ChannelShoutoutCreateV1("1337", "1338"), "channel.shoutout.create@1", moderated, none, shoutouts, "1338");
        AssertSpec(EventSubSubscriptions.ChannelShoutoutReceiveV1("1337", "1338"), "channel.shoutout.receive@1", moderated, none, shoutouts, "1338");
    }

    private static void AssertSpec(EventSubSubscriptionSpec spec, string id, IReadOnlyDictionary<string, string> condition, string[] requiredScopes, string[] anyOfScopes, string? authorizingUserId)
    {
        Assert.Equal(id, spec.ToString());
        Assert.Equal(condition.OrderBy(p => p.Key, StringComparer.Ordinal), spec.Condition.OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.Equal(requiredScopes, spec.RequiredScopes);
        Assert.Equal(anyOfScopes, spec.AnyOfScopes);
        Assert.Equal(authorizingUserId, spec.AuthorizingUserId);
        Assert.Equal(EventSubTransports.All, spec.Transports);
        Assert.Contains(id, GroupIds);
        Assert.True(EventSubEvents.TryGetDefinition(spec.Type, spec.Version, out _), id);
    }

    [Fact]
    public void FactoriesRejectBlankArguments()
    {
        Action[] calls =
        [
            () => EventSubSubscriptions.ChannelUpdateV2(" "),
            () => EventSubSubscriptions.ChannelFollowV2("1337", ""),
            () => EventSubSubscriptions.ChannelFollowV2(" ", "1338"),
            () => EventSubSubscriptions.ChannelAdBreakBeginV1(""),
            () => EventSubSubscriptions.ChannelBanV1(null!),
            () => EventSubSubscriptions.ChannelUnbanV1(" "),
            () => EventSubSubscriptions.ChannelUnbanRequestCreateV1("1337", " "),
            () => EventSubSubscriptions.ChannelUnbanRequestResolveV1(" ", "1338"),
            () => EventSubSubscriptions.ChannelModerateV1("1337", ""),
            () => EventSubSubscriptions.ChannelModerateV2("", "1338"),
            () => EventSubSubscriptions.ChannelModeratorAddV1(" "),
            () => EventSubSubscriptions.ChannelModeratorRemoveV1(" "),
            () => EventSubSubscriptions.ChannelVipAddV1(""),
            () => EventSubSubscriptions.ChannelVipRemoveV1(" "),
            () => EventSubSubscriptions.ChannelShieldModeBeginV1("1337", null!),
            () => EventSubSubscriptions.ChannelShieldModeEndV1(" ", "1338"),
            () => EventSubSubscriptions.ChannelShoutoutCreateV1("1337", " "),
            () => EventSubSubscriptions.ChannelShoutoutReceiveV1("", "1338"),
            () => EventSubSubscriptions.ChannelRaidFromBroadcasterV1(" "),
            () => EventSubSubscriptions.ChannelRaidToBroadcasterV1(null!),
        ];
        foreach (var call in calls) Assert.ThrowsAny<ArgumentException>(call);
    }

    [Fact]
    public void RaidConditionRequiresExactlyOneDirection()
    {
        Assert.Throws<ArgumentException>(() => EventSubSubscriptions.ChannelRaidV1());
        Assert.Throws<ArgumentException>(() => EventSubSubscriptions.ChannelRaidV1("1234", "1337"));
        Assert.Throws<ArgumentException>(() => EventSubSubscriptions.ChannelRaidV1(fromBroadcasterUserId: " "));
        var to = EventSubSubscriptions.ChannelRaidV1(toBroadcasterUserId: "1337");
        Assert.Equal(new KeyValuePair<string, string>("to_broadcaster_user_id", "1337"), Assert.Single(to.Condition));
        var from = EventSubSubscriptions.ChannelRaidV1(fromBroadcasterUserId: "1234");
        Assert.Equal(new KeyValuePair<string, string>("from_broadcaster_user_id", "1234"), Assert.Single(from.Condition));
    }

    [Fact]
    public async Task ChannelModerateWebSocketSubscriptionPreflightsScopesAndModerator()
    {
        var spec = EventSubSubscriptions.ChannelModerateV2("1337", "1338");
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            calls++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal(("channel.moderate", "2"), (body.RootElement.GetProperty("type").GetString(), body.RootElement.GetProperty("version").GetString()));
            var condition = body.RootElement.GetProperty("condition");
            Assert.Equal(("1337", "1338"), (condition.GetProperty("broadcaster_user_id").GetString(), condition.GetProperty("moderator_user_id").GetString()));
            Assert.Equal("session", body.RootElement.GetProperty("transport").GetProperty("session_id").GetString());
            return TestHttpHandler.Json(Accepted, HttpStatusCode.Accepted);
        }));
        await Helix(http, User("1338", TwitchScopes.ModeratorReadModerators, TwitchScopes.ModeratorReadVips, TwitchScopes.ModeratorManageWarnings)).SubscribeWebSocketAsync(spec, "session");

        var missingFixed = await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Helix(http, User("1338", TwitchScopes.ModeratorReadModerators, TwitchScopes.ModeratorReadWarnings)).SubscribeWebSocketAsync(spec, "session"));
        Assert.Equal([TwitchScopes.ModeratorReadVips], missingFixed.MissingScopes);
        var missingPair = await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Helix(http, User("1338", TwitchScopes.ModeratorReadModerators, TwitchScopes.ModeratorReadVips)).SubscribeWebSocketAsync(spec, "session"));
        Assert.Contains(TwitchScopes.ModeratorReadWarnings, missingPair.RequiredAnyOfScopes);
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Helix(http, User("1337", TwitchScopes.ModeratorReadModerators, TwitchScopes.ModeratorReadVips, TwitchScopes.ModeratorReadWarnings)).SubscribeWebSocketAsync(spec, "session"));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Helix(http, new AccessToken("app", kind: TwitchTokenKind.App)).SubscribeWebSocketAsync(spec, "session"));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AlternativeScopeSubscriptionsAcceptEitherScopeAndRejectNeither()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json(Accepted, HttpStatusCode.Accepted)); }));
        await Helix(http, User("1337", TwitchScopes.ChannelManageVips)).SubscribeWebSocketAsync(EventSubSubscriptions.ChannelVipAddV1("1337"), "session");
        await Helix(http, User("1338", TwitchScopes.ModeratorReadShoutouts)).SubscribeWebSocketAsync(EventSubSubscriptions.ChannelShoutoutReceiveV1("1337", "1338"), "session");
        var neither = await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Helix(http, User("1337", TwitchScopes.ChannelReadAds)).SubscribeWebSocketAsync(EventSubSubscriptions.ChannelVipRemoveV1("1337"), "session"));
        Assert.Equal([TwitchScopes.ChannelReadVips, TwitchScopes.ChannelManageVips], neither.RequiredAnyOfScopes);
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Helix(http, User("1337", TwitchScopes.ModeratorManageShieldMode)).SubscribeWebSocketAsync(EventSubSubscriptions.ChannelShieldModeBeginV1("1337", "1338"), "session"));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RaidWebhookSubscriptionNeedsNoUserAndSendsOneDirection()
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var condition = body.RootElement.GetProperty("condition");
            Assert.Equal("1337", condition.GetProperty("to_broadcaster_user_id").GetString());
            Assert.False(condition.TryGetProperty("from_broadcaster_user_id", out _));
            return TestHttpHandler.Json(Accepted, HttpStatusCode.Accepted);
        }));
        var webhook = new EventSubTransportRequest { Method = "webhook", Callback = "https://example.com/callback", Secret = "0123456789abcdef" };
        await Helix(http, new AccessToken("app", kind: TwitchTokenKind.App)).CreateEventSubSubscriptionAsync(EventSubSubscriptions.ChannelRaidToBroadcasterV1("1337"), webhook);
    }

    [Fact]
    public async Task RouterSeparatesChannelModerateVersions()
    {
        ChannelModerateEvent? v1 = null;
        ChannelModerateEventV2? v2 = null;
        var router = new EventSubEventRouter()
            .On(EventSubEvents.ChannelModerateV1, (evt, _, _) => { v1 = evt; return Task.CompletedTask; })
            .On(EventSubEvents.ChannelModerateV2, (evt, _, _) => { v2 = evt; return Task.CompletedTask; });
        Assert.True(await router.DispatchAsync(Notification("channel.moderate", "2", ContractAssertions.Fixture(FixtureFile, "channel.moderate@2"))));
        Assert.Equal("twitchdev", v2!.Warn!.UserLogin);
        Assert.Null(v1);
        Assert.True(await router.DispatchAsync(Notification("channel.moderate", "1", ContractAssertions.Fixture(FixtureFile, "channel.moderate@1"))));
        Assert.Equal("mod", v1!.Action);

        var message = Notification("channel.ban", "1", ContractAssertions.Fixture(FixtureFile, "channel.ban@1"));
        Assert.True(message.TryReadEvent(EventSubEvents.ChannelBanV1, out var ban));
        Assert.Equal("Offensive language", ban.Reason);
        Assert.False(message.TryReadEvent(EventSubEvents.ChannelUnbanV1, out _));
    }
}
