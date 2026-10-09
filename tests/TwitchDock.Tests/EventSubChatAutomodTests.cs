using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwitchDock.Chat;
using TwitchDock.Core;
using TwitchDock.EventSub;
using TwitchDock.EventSub.Events;
using TwitchDock.Helix;
using TwitchDock.Helix.Models;

namespace TwitchDock.Tests;

public sealed class EventSubChatAutomodTests
{
    private const string FixtureFile = "eventsub-chat-automod.json";
    private const string Accepted = """{"data":[],"total":1,"total_cost":0,"max_total_cost":10}""";

    private static readonly string[] GroupIds =
    [
        "automod.message.hold@1", "automod.message.hold@2", "automod.message.update@1", "automod.message.update@2", "automod.settings.update@1",
        "automod.terms.update@1", "channel.chat.clear@1", "channel.chat.clear_user_messages@1", "channel.chat.message@1",
        "channel.chat.message_delete@1", "channel.chat.notification@1", "channel.chat_settings.update@1", "channel.chat.user_message_hold@1",
        "channel.chat.user_message_update@1", "channel.shared_chat.begin@1", "channel.shared_chat.update@1", "channel.shared_chat.end@1",
        "channel.suspicious_user.message@1", "channel.suspicious_user.update@1", "channel.warning.acknowledge@1", "channel.warning.send@1",
    ];

    private static HelixClient Helix(HttpClient http, AccessToken token)
        => new(new(http, new StaticAccessTokenProvider(token), new() { ClientId = "client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 }));

    [Fact]
    public void EveryGroupDefinitionIsRegisteredAndHasAFixture()
    {
        foreach (var id in GroupIds)
        {
            var separator = id.IndexOf('@', StringComparison.Ordinal);
            Assert.True(EventSubEvents.TryGetDefinition(id[..separator], id[(separator + 1)..], out var definition), id);
            using var fixture = JsonDocument.Parse(ContractAssertions.Fixture(FixtureFile, id));
            Assert.IsType(definition.EventType, definition.Deserialize(fixture.RootElement));
        }
    }

    [Fact]
    public void AutomodContractsPreserveEveryDocumentedField()
    {
        var holdV1 = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.AutomodMessageHoldV1);
        Assert.Equal(("aggressive", 4), (holdV1.Category, holdV1.Level));
        Assert.Null(holdV1.Message.Fragments[0].Type);
        Assert.Equal("0", holdV1.Message.Fragments[1].Emote!.EmoteSetId);
        Assert.Equal(DateTimeOffset.Parse("2022-12-02T15:00:00Z"), holdV1.HeldAt);

        var holdV2 = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.AutomodMessageHoldV2);
        Assert.Equal("automod", holdV2.Reason);
        Assert.Equal(new[] { (0, 10), (20, 30) }, holdV2.Automod!.Boundaries.Select(b => (b.StartPos, b.EndPos)));
        Assert.Equal(1000, holdV2.Message.Fragments[2].Cheermote!.Bits);

        var updateV1 = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.AutomodMessageUpdateV1);
        Assert.Equal(("9001", "Approved"), (updateV1.ModeratorUserId, updateV1.Status));

        var updateV2 = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.AutomodMessageUpdateV2);
        var term = Assert.Single(updateV2.BlockedTerm!.TermsFound);
        Assert.Equal(("123", 30, "blahblah"), (term.TermId, term.Boundary.EndPos, term.OwnerBroadcasterUserName));

        var settings = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.AutomodSettingsUpdateV1);
        Assert.Null(settings.OverallLevel);
        Assert.Equal((30, 0, 3), (settings.SexBasedTerms, settings.Swearing, settings.RaceEthnicityOrReligion));

        var terms = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.AutomodTermsUpdateV1);
        Assert.True(terms.FromAutomod);
        Assert.Equal(3, terms.Terms.Count);
    }

    [Fact]
    public void OfficialAutomodUpdateExampleKeepsTheUnpopulatedReasonNull()
    {
        var update = RoundTrip("automod.message.update@2 official", EventSubEvents.AutomodMessageUpdateV2);
        Assert.Null(update.Automod);
        Assert.Equal("text", update.Message.Fragments[0].Type);
        Assert.Null(update.Message.Fragments[0].Cheermote);
    }

    [Fact]
    public void AutomodV1AlsoReadsTheStringMessageFromTwitchsExample()
    {
        using var official = JsonDocument.Parse(ContractAssertions.Fixture(FixtureFile, "automod.message.hold@1 official"));
        var hold = EventSubEvents.AutomodMessageHoldV1.Deserialize(official.RootElement);
        Assert.Equal("This is a bad message… ", hold.Message.Text);
        Assert.Empty(hold.Message.Fragments);
        Assert.Equal(5, hold.Level);
        using var plain = JsonDocument.Parse("""
            {"broadcaster_user_id":"1","broadcaster_user_login":"b","broadcaster_user_name":"B","user_id":"2","user_login":"u","user_name":"U",
             "moderator_user_id":"3","moderator_user_login":"m","moderator_user_name":"M","message_id":"x","message":"plain","category":"swearing",
             "level":1,"status":"denied","held_at":"2022-12-02T15:00:00Z"}
            """);
        var update = EventSubEvents.AutomodMessageUpdateV1.Deserialize(plain.RootElement);
        Assert.Equal(("plain", "denied"), (update.Message.Text, update.Status));
    }

    [Fact]
    public void ChatMessageToleratesOmittedOrNullOptionalCollectionsAndStrings()
    {
        using var json = JsonDocument.Parse("""
            {"broadcaster_user_id":"1","broadcaster_user_login":"b","broadcaster_user_name":"B","chatter_user_id":"2","chatter_user_login":"c",
             "chatter_user_name":"C","message_id":"m","message":{"text":"hi","fragments":null},"badges":[{"set_id":"vip","id":"1","info":null}],
             "color":null}
            """);
        var chat = EventSubEvents.ChannelChatMessageV1.Deserialize(json.RootElement);
        Assert.Empty(chat.Message.Fragments);
        Assert.Equal(("", ""), (chat.Color, chat.MessageType));
        Assert.Equal("", Assert.Single(chat.Badges).Info);
        Assert.Null(chat.SourceBadges);
    }

    [Fact]
    public void ChatMessageContractPreservesEveryFragmentKindReplyCheerAndSharedChatSource()
    {
        var chat = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelChatMessageV1);
        Assert.Equal(["text", "cheermote", "emote", "mention", "gif"], chat.Message.Fragments.Select(f => f.Type));
        Assert.Equal(("cheer", 100, 1), (chat.Message.Fragments[1].Cheermote!.Prefix, chat.Message.Fragments[1].Cheermote!.Bits, chat.Message.Fragments[1].Cheermote!.Tier));
        Assert.Equal(["static", "animated"], chat.Message.Fragments[2].Emote!.Format);
        Assert.Equal("112233", chat.Message.Fragments[3].Mention!.UserId);
        Assert.Equal("https://media.example.org/gif-1.gif?token=abc", chat.Message.Fragments[4].Gif!.Url);
        Assert.Equal(100, chat.Cheer!.Bits);
        Assert.Equal(("parent-1", "thread-1"), (chat.Reply!.ParentMessageId, chat.Reply.ThreadMessageId));
        Assert.Equal(("reward-1", "rainbow-eclipse"), (chat.ChannelPointsCustomRewardId, chat.ChannelPointsAnimationId));
        Assert.Equal("16", chat.Badges[1].Info);
        Assert.Equal("112233", chat.SourceBroadcasterUserId);
        Assert.Equal("3", Assert.Single(chat.SourceBadges!).Info);
        Assert.False(chat.IsSourceOnly);
    }

    [Fact]
    public void OfficialChatMessageExampleLeavesOptionalObjectsNull()
    {
        var chat = RoundTrip("channel.chat.message@1 official", EventSubEvents.ChannelChatMessageV1);
        Assert.Equal("text", chat.MessageType);
        Assert.Null(chat.Cheer);
        Assert.Null(chat.Reply);
        Assert.Null(chat.SourceBadges);
        Assert.Null(chat.IsSourceOnly);
        Assert.Null(chat.ChannelPointsAnimationId);
        Assert.Null(Assert.Single(chat.Message.Fragments).Gif);
    }

    [Fact]
    public void ChatNotificationContractPreservesEveryNoticeVariant()
    {
        var notice = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelChatNotificationV1);
        Assert.Equal("resub", notice.NoticeType);
        Assert.Null(notice.Resub!.StreakMonths);
        Assert.Equal(("1000", true), (notice.Resub.SubTier, notice.Resub.IsGift));
        Assert.Null(notice.SubGift!.CumulativeTotal);
        Assert.Equal(25, notice.CommunitySubGift!.CumulativeTotal);
        Assert.Null(notice.GiftPaidUpgrade!.GifterUserId);
        Assert.Equal(1234, notice.Raid!.ViewerCount);
        Assert.NotNull(notice.Unraid);
        Assert.Equal((550, 2, "USD"), (notice.CharityDonation!.Amount.Value, notice.CharityDonation.Amount.DecimalPlace, notice.CharityDonation.Amount.Currency));
        Assert.Equal((7, 450), (notice.WatchStreak!.StreakCount, notice.WatchStreak.ChannelPointsAwarded));
        Assert.Equal(1000, notice.BitsBadgeTier!.Tier);
        Assert.Null(notice.SharedChatResub!.IsPrime);
        Assert.Equal("JPY", notice.SharedChatCharityDonation!.Amount.Currency);
        Assert.NotNull(notice.SharedChatUnraid);
        Assert.Equal(["emote", "cheermote", "mention", "text"], notice.Message.Fragments.Select(f => f.Type));
        Assert.True(notice.IsSourceOnly);
    }

    [Fact]
    public void SharedChatResubNotificationPopulatesOnlyItsVariant()
    {
        var notice = RoundTrip("channel.chat.notification@1 shared_chat_resub", EventSubEvents.ChannelChatNotificationV1);
        Assert.Equal("shared_chat_resub", notice.NoticeType);
        Assert.Null(notice.Resub);
        Assert.Equal(10, notice.SharedChatResub!.CumulativeMonths);
        Assert.Null(notice.SharedChatResub.IsPrime);
        Assert.Equal("", notice.Color);
        Assert.Empty(notice.Message.Fragments);
        Assert.Equal("112233", notice.SourceBroadcasterUserId);
    }

    [Fact]
    public void ChatRoomEventContractsPreserveEveryDocumentedField()
    {
        Assert.Equal("cool_user", EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelChatClearV1).BroadcasterUserLogin);
        Assert.Equal("7734", EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelChatClearUserMessagesV1).TargetUserId);
        Assert.Equal("ab24e0b0-2260-4bac-94e4-05eedd4ecd0e", EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelChatMessageDeleteV1).MessageId);

        var settings = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelChatSettingsUpdateV1);
        Assert.Null(settings.FollowerModeDurationMinutes);
        Assert.Equal(10, settings.SlowModeWaitTimeSeconds);
        Assert.True(settings.EmoteMode);

        var hold = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelChatUserMessageHoldV1);
        Assert.Equal(["emote", "cheermote", "text"], hold.Message.Fragments.Select(f => f.Type));
        Assert.Equal("foo", hold.Message.Fragments[0].Emote!.Id);

        var update = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelChatUserMessageUpdateV1);
        Assert.Equal(("approved", 100), (update.Status, update.Message.Fragments[1].Cheermote!.Bits));
    }

    [Fact]
    public void SharedChatSuspiciousUserAndWarningContractsPreserveEveryDocumentedField()
    {
        var begin = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelSharedChatBeginV1);
        Assert.Equal(["1971641", "112233"], begin.Participants.Select(p => p.BroadcasterUserId));
        Assert.Equal(3, EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelSharedChatUpdateV1).Participants.Count);
        Assert.Equal("1971641", EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelSharedChatEndV1).HostBroadcasterUserId);

        var suspicious = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelSuspiciousUserMessageV1);
        Assert.Equal(["100", "200"], suspicious.SharedBanChannelIds);
        Assert.Equal(["ban_evader"], suspicious.Types);
        Assert.Equal(("101010", "likely"), (suspicious.Message.MessageId, suspicious.BanEvasionEvaluation));
        Assert.Equal("restricted", EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelSuspiciousUserUpdateV1).LowTrustStatus);

        Assert.Equal("141981764", EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelWarningAcknowledgeV1).UserId);
        var warning = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelWarningSendV1);
        Assert.Equal(["Be kind", "No spam"], warning.ChatRulesCited!);
        Assert.Equal("cut it out", warning.Reason);
    }

    [Fact]
    public void CheermoteBitsAndTierAlsoReadNumericStrings()
    {
        // The suspicious-user field table documents bits and tier as strings while the example sends numbers.
        using var json = JsonDocument.Parse("""
            {"broadcaster_user_id":"1","broadcaster_user_name":"B","broadcaster_user_login":"b","user_id":"2","user_name":"U","user_login":"u",
             "low_trust_status":"restricted","shared_ban_channel_ids":[],"types":[],"ban_evasion_evaluation":"unknown",
             "message":{"message_id":"m","text":"cheer5","fragments":[{"type":"cheermote","text":"cheer5","cheermote":{"prefix":"cheer","bits":"5","tier":"1"},"emote":null}]}}
            """);
        var cheermote = EventSubEvents.ChannelSuspiciousUserMessageV1.Deserialize(json.RootElement).Message.Fragments[0].Cheermote!;
        Assert.Equal((5, 1), (cheermote.Bits, cheermote.Tier));
    }

    [Fact]
    public void FactoriesBuildDocumentedConditionsAndAuthorization()
    {
        string[] none = [];
        var cases = new (EventSubSubscriptionSpec Spec, string Id, string? UserKey, string[] Scopes, string[] AnyOf)[]
        {
            (EventSubSubscriptions.AutomodMessageHoldV1("1", "2"), "automod.message.hold@1", "moderator_user_id", [TwitchScopes.ModeratorManageAutomod], none),
            (EventSubSubscriptions.AutomodMessageHoldV2("1", "2"), "automod.message.hold@2", "moderator_user_id", [TwitchScopes.ModeratorManageAutomod], none),
            (EventSubSubscriptions.AutomodMessageUpdateV1("1", "2"), "automod.message.update@1", "moderator_user_id", [TwitchScopes.ModeratorManageAutomod], none),
            (EventSubSubscriptions.AutomodMessageUpdateV2("1", "2"), "automod.message.update@2", "moderator_user_id", [TwitchScopes.ModeratorManageAutomod], none),
            (EventSubSubscriptions.AutomodSettingsUpdateV1("1", "2"), "automod.settings.update@1", "moderator_user_id", [TwitchScopes.ModeratorReadAutomodSettings], none),
            (EventSubSubscriptions.AutomodTermsUpdateV1("1", "2"), "automod.terms.update@1", "moderator_user_id", [TwitchScopes.ModeratorManageAutomod], none),
            (EventSubSubscriptions.ChannelChatClearV1("1", "2"), "channel.chat.clear@1", "user_id", [TwitchScopes.UserReadChat], none),
            (EventSubSubscriptions.ChannelChatClearUserMessagesV1("1", "2"), "channel.chat.clear_user_messages@1", "user_id", [TwitchScopes.UserReadChat], none),
            (EventSubSubscriptions.ChannelChatMessageV1("1", "2"), "channel.chat.message@1", "user_id", [TwitchScopes.UserReadChat], none),
            (EventSubSubscriptions.ChannelChatMessageDeleteV1("1", "2"), "channel.chat.message_delete@1", "user_id", [TwitchScopes.UserReadChat], none),
            (EventSubSubscriptions.ChannelChatNotificationV1("1", "2"), "channel.chat.notification@1", "user_id", [TwitchScopes.UserReadChat], none),
            (EventSubSubscriptions.ChannelChatSettingsUpdateV1("1", "2"), "channel.chat_settings.update@1", "user_id", [TwitchScopes.UserReadChat], none),
            (EventSubSubscriptions.ChannelChatUserMessageHoldV1("1", "2"), "channel.chat.user_message_hold@1", "user_id", [TwitchScopes.UserReadChat], none),
            (EventSubSubscriptions.ChannelChatUserMessageUpdateV1("1", "2"), "channel.chat.user_message_update@1", "user_id", [TwitchScopes.UserReadChat], none),
            (EventSubSubscriptions.ChannelSharedChatBeginV1("1"), "channel.shared_chat.begin@1", null, none, none),
            (EventSubSubscriptions.ChannelSharedChatUpdateV1("1"), "channel.shared_chat.update@1", null, none, none),
            (EventSubSubscriptions.ChannelSharedChatEndV1("1"), "channel.shared_chat.end@1", null, none, none),
            (EventSubSubscriptions.ChannelSuspiciousUserMessageV1("1", "2"), "channel.suspicious_user.message@1", "moderator_user_id", [TwitchScopes.ModeratorReadSuspiciousUsers], none),
            (EventSubSubscriptions.ChannelSuspiciousUserUpdateV1("1", "2"), "channel.suspicious_user.update@1", "moderator_user_id", [TwitchScopes.ModeratorReadSuspiciousUsers], none),
            (EventSubSubscriptions.ChannelWarningAcknowledgeV1("1", "2"), "channel.warning.acknowledge@1", "moderator_user_id", none, [TwitchScopes.ModeratorReadWarnings, TwitchScopes.ModeratorManageWarnings]),
            (EventSubSubscriptions.ChannelWarningSendV1("1", "2"), "channel.warning.send@1", "moderator_user_id", none, [TwitchScopes.ModeratorReadWarnings, TwitchScopes.ModeratorManageWarnings]),
        };
        Assert.Equal(GroupIds, cases.Select(c => c.Id));
        foreach (var (spec, id, userKey, scopes, anyOf) in cases)
        {
            Assert.Equal(id, spec.ToString());
            Assert.Equal("1", spec.Condition["broadcaster_user_id"]);
            Assert.Equal(userKey is null ? 1 : 2, spec.Condition.Count);
            if (userKey is not null) Assert.Equal("2", spec.Condition[userKey]);
            Assert.Equal(userKey is null ? null : "2", spec.AuthorizingUserId);
            Assert.Equal(scopes, spec.RequiredScopes);
            Assert.Equal(anyOf, spec.AnyOfScopes);
            Assert.Equal(EventSubTransports.All, spec.Transports);
        }
    }

    [Fact]
    public void FactoriesRejectBlankRequiredArguments()
    {
        Func<string, string, EventSubSubscriptionSpec>[] pairs =
        [
            EventSubSubscriptions.AutomodMessageHoldV1, EventSubSubscriptions.AutomodMessageHoldV2, EventSubSubscriptions.AutomodMessageUpdateV1,
            EventSubSubscriptions.AutomodMessageUpdateV2, EventSubSubscriptions.AutomodSettingsUpdateV1, EventSubSubscriptions.AutomodTermsUpdateV1,
            EventSubSubscriptions.ChannelChatClearV1, EventSubSubscriptions.ChannelChatClearUserMessagesV1, EventSubSubscriptions.ChannelChatMessageV1,
            EventSubSubscriptions.ChannelChatMessageDeleteV1, EventSubSubscriptions.ChannelChatNotificationV1, EventSubSubscriptions.ChannelChatSettingsUpdateV1,
            EventSubSubscriptions.ChannelChatUserMessageHoldV1, EventSubSubscriptions.ChannelChatUserMessageUpdateV1,
            EventSubSubscriptions.ChannelSuspiciousUserMessageV1, EventSubSubscriptions.ChannelSuspiciousUserUpdateV1,
            EventSubSubscriptions.ChannelWarningAcknowledgeV1, EventSubSubscriptions.ChannelWarningSendV1,
        ];
        foreach (var create in pairs)
        {
            Assert.ThrowsAny<ArgumentException>(() => create(" ", "2"));
            Assert.ThrowsAny<ArgumentException>(() => create("1", ""));
            Assert.ThrowsAny<ArgumentException>(() => create("1", null!));
        }
        Func<string, EventSubSubscriptionSpec>[] singles = [EventSubSubscriptions.ChannelSharedChatBeginV1, EventSubSubscriptions.ChannelSharedChatUpdateV1, EventSubSubscriptions.ChannelSharedChatEndV1];
        foreach (var create in singles) Assert.ThrowsAny<ArgumentException>(() => create(" "));
    }

    [Fact]
    public async Task ChatClientSubscribesThroughTheTypedSpecAndPreflightsTheChattingUser()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            calls++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal(("channel.chat.message", "1"), (body.RootElement.GetProperty("type").GetString(), body.RootElement.GetProperty("version").GetString()));
            var condition = body.RootElement.GetProperty("condition");
            Assert.Equal(("1971641", "4145994"), (condition.GetProperty("broadcaster_user_id").GetString(), condition.GetProperty("user_id").GetString()));
            Assert.Equal(("websocket", "session"), (body.RootElement.GetProperty("transport").GetProperty("method").GetString(), body.RootElement.GetProperty("transport").GetProperty("session_id").GetString()));
            return TestHttpHandler.Json(Accepted, HttpStatusCode.Accepted);
        }));
        await new TwitchChatClient(Helix(http, new("user", scopes: [TwitchScopes.UserReadChat], kind: TwitchTokenKind.User, userId: "4145994"))).SubscribeAsync("1971641", "4145994", "session");
        foreach (var token in new AccessToken[] { new("user", scopes: [], kind: TwitchTokenKind.User, userId: "4145994"), new("user", scopes: [TwitchScopes.UserReadChat], kind: TwitchTokenKind.User, userId: "1"), new("app", kind: TwitchTokenKind.App) })
            await Assert.ThrowsAsync<TwitchAuthorizationException>(() => new TwitchChatClient(Helix(http, token)).SubscribeAsync("1971641", "4145994", "session"));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task WebSocketPreflightChecksModeratorScopesAndWarningAlternatives()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json(Accepted, HttpStatusCode.Accepted)); }));
        static AccessToken Moderator(params string[] scopes) => new("user", scopes: scopes, kind: TwitchTokenKind.User, userId: "9001");

        await Helix(http, Moderator(TwitchScopes.ModeratorManageAutomod)).SubscribeWebSocketAsync(EventSubSubscriptions.AutomodMessageHoldV2("1337", "9001"), "session");
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Helix(http, Moderator(TwitchScopes.ModeratorManageAutomod)).SubscribeWebSocketAsync(EventSubSubscriptions.AutomodMessageHoldV2("1337", "9002"), "session"));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Helix(http, Moderator(TwitchScopes.ModeratorManageAutomod)).SubscribeWebSocketAsync(EventSubSubscriptions.AutomodSettingsUpdateV1("1337", "9001"), "session"));

        await Helix(http, Moderator(TwitchScopes.ModeratorManageWarnings)).SubscribeWebSocketAsync(EventSubSubscriptions.ChannelWarningSendV1("1337", "9001"), "session");
        await Helix(http, Moderator(TwitchScopes.ModeratorReadWarnings)).SubscribeWebSocketAsync(EventSubSubscriptions.ChannelWarningAcknowledgeV1("1337", "9001"), "session");
        var missing = await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Helix(http, Moderator(TwitchScopes.ModeratorManageAutomod)).SubscribeWebSocketAsync(EventSubSubscriptions.ChannelWarningSendV1("1337", "9001"), "session"));
        Assert.Equal([TwitchScopes.ModeratorReadWarnings, TwitchScopes.ModeratorManageWarnings], missing.RequiredAnyOfScopes);

        await Helix(http, Moderator()).SubscribeWebSocketAsync(EventSubSubscriptions.ChannelSharedChatBeginV1("1337"), "session");
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task WebhookSubscriptionsUseTheAppTokenWithoutUserPreflight()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json(Accepted, HttpStatusCode.Accepted)); }));
        var webhook = new EventSubTransportRequest { Method = "webhook", Callback = "https://example.com/callback", Secret = "0123456789abcdef" };
        await Helix(http, new("app", kind: TwitchTokenKind.App)).CreateEventSubSubscriptionAsync(EventSubSubscriptions.ChannelChatMessageV1("1971641", "4145994"), webhook);
        await Helix(http, new("app", kind: TwitchTokenKind.App)).CreateEventSubSubscriptionAsync(EventSubSubscriptions.ChannelSuspiciousUserMessageV1("1337", "9001"), webhook);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RouterAndChatClientReadTypedChatNotifications()
    {
        var chatJson = ContractAssertions.Fixture(FixtureFile, "channel.chat.message@1");
        var message = Notification("channel.chat.message", chatJson);
        Assert.True(TwitchChatClient.TryReadMessage(message, out var chat));
        Assert.Equal("Hi chat cheer100 Kappa @streamer33 wave", chat.Message.Text);
        Assert.False(TwitchChatClient.TryReadMessage(Notification("channel.chat.notification", chatJson), out _));
        Assert.False(TwitchChatClient.TryReadMessage(Notification("channel.chat.message", chatJson, "revocation"), out _));

        ChannelChatNotificationEvent? received = null;
        var router = new EventSubEventRouter().On(EventSubEvents.ChannelChatNotificationV1, (evt, _, _) => { received = evt; return Task.CompletedTask; });
        Assert.True(await router.DispatchAsync(Notification("channel.chat.notification", ContractAssertions.Fixture(FixtureFile, "channel.chat.notification@1 shared_chat_resub"))));
        Assert.Equal("shared_chat_resub", received!.NoticeType);
        Assert.False(await router.DispatchAsync(message));
    }

    private static EventSubMessage Notification(string type, string eventJson, string messageType = "notification") => EventSubMessage.Parse(Encoding.UTF8.GetBytes(
        $$$"""{"metadata":{"message_id":"m1","message_type":"{{{messageType}}}","message_timestamp":"2026-10-09T12:00:00.123456789Z","subscription_type":"{{{type}}}","subscription_version":"1"},"payload":{"subscription":{"id":"s1","status":"enabled","type":"{{{type}}}","version":"1","condition":{"broadcaster_user_id":"1971641","user_id":"4145994"},"transport":{"method":"websocket","session_id":"x"},"created_at":"2026-10-09T12:00:00Z","cost":0},"event":{{{eventJson}}}}}"""));

    /// <summary>Like <see cref="EventSubContractAssertions.Verify"/> but for additional fixture keys such as official examples.</summary>
    private static TEvent RoundTrip<TEvent>(string key, EventSubEventDefinition<TEvent> definition) where TEvent : class
    {
        using var expected = JsonDocument.Parse(ContractAssertions.Fixture(FixtureFile, key));
        var model = definition.Deserialize(expected.RootElement);
        var context = new EventSubEventsJsonContext(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.Never });
        using var actual = JsonDocument.Parse(JsonSerializer.Serialize(model, context.GetTypeInfo(typeof(TEvent))!));
        ContractAssertions.AssertSubset(expected.RootElement, actual.RootElement);
        return model;
    }
}
