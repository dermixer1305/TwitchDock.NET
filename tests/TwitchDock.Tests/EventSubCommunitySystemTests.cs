using System.Net;
using System.Text;
using System.Text.Json;
using TwitchDock.Core;
using TwitchDock.EventSub;
using TwitchDock.EventSub.Events;
using TwitchDock.Helix;
using TwitchDock.Helix.Models;

namespace TwitchDock.Tests;

public sealed class EventSubCommunitySystemTests
{
    private const string Fixture = "eventsub-community-system.json";
    private const string Accepted = """{"data":[],"total":1,"total_cost":0,"max_total_cost":10}""";
    private const EventSubTransports AppTokenTransports = EventSubTransports.Webhook | EventSubTransports.Conduit;

    [Fact]
    public void CharityContractsPreserveEveryDocumentedField()
    {
        var donate = EventSubContractAssertions.Verify(Fixture, EventSubEvents.ChannelCharityCampaignDonateV1);
        Assert.Equal((10000L, 2, "USD"), (donate.Amount.Value, donate.Amount.DecimalPlaces, donate.Amount.Currency));
        Assert.Equal(("123-abc-456-def", "654321"), (donate.CampaignId, donate.UserId));
        var start = EventSubContractAssertions.Verify(Fixture, EventSubEvents.ChannelCharityCampaignStartV1);
        Assert.Equal(DateTimeOffset.Parse("2022-07-26T17:00:03.1710671Z"), start.StartedAt);
        Assert.Equal((0L, 1500000L), (start.CurrentAmount.Value, start.TargetAmount.Value));
        Assert.Equal(260000L, EventSubContractAssertions.Verify(Fixture, EventSubEvents.ChannelCharityCampaignProgressV1).CurrentAmount.Value);
        var stop = EventSubContractAssertions.Verify(Fixture, EventSubEvents.ChannelCharityCampaignStopV1);
        Assert.Equal((3_000_000_000L, 0, "JPY"), (stop.CurrentAmount.Value, stop.CurrentAmount.DecimalPlaces, stop.CurrentAmount.Currency));
        Assert.Equal(DateTimeOffset.Parse("2022-07-26T22:00:03.1710671Z"), stop.StoppedAt);
    }

    [Fact]
    public void GoalContractsPreserveEveryDocumentedField()
    {
        var begin = EventSubContractAssertions.Verify(Fixture, EventSubEvents.ChannelGoalBeginV1);
        Assert.Equal(("subscription", "Help me get partner!", 100L, 220L), (begin.Type, begin.Description, begin.CurrentAmount, begin.TargetAmount));
        var progress = EventSubContractAssertions.Verify(Fixture, EventSubEvents.ChannelGoalProgressV1);
        Assert.Equal(("follow", ""), (progress.Type, progress.Description));
        var end = EventSubContractAssertions.Verify(Fixture, EventSubEvents.ChannelGoalEndV1);
        Assert.True(end.IsAchieved);
        Assert.Equal(DateTimeOffset.Parse("2021-07-16T17:16:03.1710671Z"), end.EndedAt);
    }

    [Fact]
    public void HypeTrainV2ContractsPreserveEveryDocumentedField()
    {
        var begin = EventSubContractAssertions.Verify(Fixture, EventSubEvents.ChannelHypeTrainBeginV2);
        Assert.Equal((4, 4_294_967_296L), (begin.AllTimeHighLevel, begin.AllTimeHighTotal));
        Assert.Equal(("golden_kappa", true), (begin.Type, begin.IsSharedTrain));
        Assert.Equal(new[] { "1337", "1338" }, begin.SharedTrainParticipants!.Select(p => p.BroadcasterUserId));
        Assert.Equal(("subscription", 2500L), (begin.TopContributions[1].Type, begin.TopContributions[1].Total));
        Assert.Equal(DateTimeOffset.Parse("2020-07-15T17:16:11.1710671Z"), begin.ExpiresAt);
        var progress = EventSubContractAssertions.Verify(Fixture, EventSubEvents.ChannelHypeTrainProgressV2);
        Assert.Null(progress.SharedTrainParticipants);
        Assert.Equal((2, 700L, 200L, 1800L), (progress.Level, progress.Total, progress.Progress, progress.Goal));
        Assert.Equal("other", Assert.Single(progress.TopContributions).Type);
        var end = EventSubContractAssertions.Verify(Fixture, EventSubEvents.ChannelHypeTrainEndV2);
        Assert.Equal(DateTimeOffset.Parse("2020-07-16T17:16:11.1710671Z"), end.CooldownEndsAt);
        Assert.Equal(DateTimeOffset.Parse("2020-07-15T17:16:11.1710671Z"), end.EndedAt);
        Assert.Equal("cooler_user", Assert.Single(end.SharedTrainParticipants!).BroadcasterUserLogin);
    }

    [Fact]
    public void UserAndSystemContractsPreserveEveryDocumentedField()
    {
        Assert.Equal("crq72vsaoijkc83xx42hz6i37", EventSubContractAssertions.Verify(Fixture, EventSubEvents.UserAuthorizationGrantV1).ClientId);
        var revoke = EventSubContractAssertions.Verify(Fixture, EventSubEvents.UserAuthorizationRevokeV1);
        Assert.Equal("1337", revoke.UserId);
        Assert.Null(revoke.UserLogin);
        Assert.Null(revoke.UserName);
        var update = EventSubContractAssertions.Verify(Fixture, EventSubEvents.UserUpdateV1);
        Assert.Equal(("user@email.com", true), (update.Email, update.EmailVerified));
        var whisper = EventSubContractAssertions.Verify(Fixture, EventSubEvents.UserWhisperMessageV1);
        Assert.Equal(("a secret", "424596340", "some-whisper-id"), (whisper.Whisper.Text, whisper.ToUserId, whisper.WhisperId));
        var shard = EventSubContractAssertions.Verify(Fixture, EventSubEvents.ConduitShardDisabledV1);
        Assert.Equal(("websocket", "websocket_disconnected"), (shard.Transport.Method, shard.Status));
        Assert.Null(shard.Transport.Callback);
        Assert.Equal(DateTimeOffset.Parse("2020-11-11T14:32:18.7302602Z"), shard.Transport.DisconnectedAt);
        var drops = EventSubContractAssertions.Verify(Fixture, EventSubEvents.DropEntitlementGrantV1);
        Assert.Equal(2, drops.Count);
        Assert.Equal(("bf7c8577-e3e3-4881-a78a-e9446641d45c", "cooler_user"), (drops[1].Id, drops[1].Data.UserLogin));
        Assert.Equal(DateTimeOffset.Parse("2019-01-28T04:17:53.325Z"), drops[0].Data.CreatedAt);
        var bits = EventSubContractAssertions.Verify(Fixture, EventSubEvents.ExtensionBitsTransactionCreateV1);
        Assert.Equal(("skuskusku", 1234, false), (bits.Product.Sku, bits.Product.Bits, bits.Product.InDevelopment));
        Assert.Equal("deadbeef", bits.ExtensionClientId);
    }

    [Fact]
    public void GuestStarBetaContractsPreserveEveryDocumentedField()
    {
        var begin = EventSubContractAssertions.Verify(Fixture, EventSubEvents.ChannelGuestStarSessionBeginBeta);
        Assert.Equal(("1338", "2KFRQbFtpmfyD3IevNRnCzOPRJI"), (begin.ModeratorUserId, begin.SessionId));
        var end = EventSubContractAssertions.Verify(Fixture, EventSubEvents.ChannelGuestStarSessionEndBeta);
        Assert.Equal(DateTimeOffset.Parse("2023-04-11T17:51:29.153485Z"), end.EndedAt);
        Assert.Equal("cool_user", end.HostUserLogin);
        var guest = EventSubContractAssertions.Verify(Fixture, EventSubEvents.ChannelGuestStarGuestUpdateBeta);
        Assert.Equal<(string?, string?, bool?, bool?, int?)>(("live", "1", true, false, 100),
            (guest.State, guest.SlotId, guest.HostVideoEnabled, guest.HostAudioEnabled, guest.HostVolume));
        var settings = EventSubContractAssertions.Verify(Fixture, EventSubEvents.ChannelGuestStarSettingsUpdateBeta);
        Assert.Equal((5, "tiled", true, true), (settings.SlotCount, settings.GroupLayout, settings.IsModeratorSendLiveEnabled, settings.IsBrowserSourceAudioEnabled));
    }

    [Fact]
    public void OptionalFieldsTolerateNullEmptyAndAbsentValues()
    {
        var webhookShard = EventSubEvents.ConduitShardDisabledV1.Deserialize(Json(
            """{"conduit_id":"c","shard_id":"0","status":"webhook_callback_verification_failed","transport":{"method":"webhook","callback":"https://example.com/cb","session_id":null,"connected_at":"","disconnected_at":null}}"""));
        Assert.Equal("https://example.com/cb", webhookShard.Transport.Callback);
        Assert.Null(webhookShard.Transport.SessionId);
        Assert.Null(webhookShard.Transport.ConnectedAt);
        Assert.Null(webhookShard.Transport.DisconnectedAt);

        var emptySlot = EventSubEvents.ChannelGuestStarGuestUpdateBeta.Deserialize(Json(
            """{"broadcaster_user_id":"1","broadcaster_user_name":"B","broadcaster_user_login":"b","session_id":"s","moderator_user_id":null,"moderator_user_name":null,"moderator_user_login":null,"guest_user_id":null,"guest_user_name":null,"guest_user_login":null,"slot_id":null,"state":null,"host_video_enabled":null,"host_audio_enabled":null,"host_volume":null}"""));
        Assert.Null(emptySlot.ModeratorUserId);
        Assert.Null(emptySlot.GuestUserId);
        Assert.Null(emptySlot.State);
        Assert.Null(emptySlot.HostUserId);
        Assert.Null(emptySlot.HostVolume);

        var session = EventSubEvents.ChannelGuestStarSessionBeginBeta.Deserialize(Json(
            """{"broadcaster_user_id":"1","broadcaster_user_name":"B","broadcaster_user_login":"b","session_id":"s","started_at":"2023-04-11T16:20:03Z"}"""));
        Assert.Null(session.ModeratorUserId);

        const string user = "\"user_id\":\"1\",\"user_login\":\"a\",\"user_name\":\"A\",\"email_verified\":false,\"description\":\"\"";
        Assert.Equal("", EventSubEvents.UserUpdateV1.Deserialize(Json("{" + user + ",\"email\":\"\"}")).Email);
        Assert.Null(EventSubEvents.UserUpdateV1.Deserialize(Json("{" + user + "}")).Email);

        Assert.Throws<JsonException>(() => EventSubEvents.ChannelHypeTrainBeginV2.Deserialize(Json("""{"id":"x"}""")));
        Assert.Throws<JsonException>(() => EventSubEvents.DropEntitlementGrantV1.Deserialize(Json(ContractAssertions.Fixture(Fixture, "drop.entitlement.grant@1")).EnumerateArray().First()));
    }

    [Fact]
    public void FactoriesAndRegistryAgreeOnEveryTypeAndVersion()
    {
        (EventSubSubscriptionSpec Spec, IEventSubEventDefinition Definition)[] pairs =
        [
            (EventSubSubscriptions.ChannelCharityCampaignDonateV1("1"), EventSubEvents.ChannelCharityCampaignDonateV1),
            (EventSubSubscriptions.ChannelCharityCampaignStartV1("1"), EventSubEvents.ChannelCharityCampaignStartV1),
            (EventSubSubscriptions.ChannelCharityCampaignProgressV1("1"), EventSubEvents.ChannelCharityCampaignProgressV1),
            (EventSubSubscriptions.ChannelCharityCampaignStopV1("1"), EventSubEvents.ChannelCharityCampaignStopV1),
            (EventSubSubscriptions.ChannelGoalBeginV1("1"), EventSubEvents.ChannelGoalBeginV1),
            (EventSubSubscriptions.ChannelGoalProgressV1("1"), EventSubEvents.ChannelGoalProgressV1),
            (EventSubSubscriptions.ChannelGoalEndV1("1"), EventSubEvents.ChannelGoalEndV1),
            (EventSubSubscriptions.ChannelHypeTrainBeginV2("1"), EventSubEvents.ChannelHypeTrainBeginV2),
            (EventSubSubscriptions.ChannelHypeTrainProgressV2("1"), EventSubEvents.ChannelHypeTrainProgressV2),
            (EventSubSubscriptions.ChannelHypeTrainEndV2("1"), EventSubEvents.ChannelHypeTrainEndV2),
            (EventSubSubscriptions.UserAuthorizationGrantV1("c"), EventSubEvents.UserAuthorizationGrantV1),
            (EventSubSubscriptions.UserAuthorizationRevokeV1("c"), EventSubEvents.UserAuthorizationRevokeV1),
            (EventSubSubscriptions.UserUpdateV1("1"), EventSubEvents.UserUpdateV1),
            (EventSubSubscriptions.UserWhisperMessageV1("1"), EventSubEvents.UserWhisperMessageV1),
            (EventSubSubscriptions.ConduitShardDisabledV1("c"), EventSubEvents.ConduitShardDisabledV1),
            (EventSubSubscriptions.DropEntitlementGrantV1("o"), EventSubEvents.DropEntitlementGrantV1),
            (EventSubSubscriptions.ExtensionBitsTransactionCreateV1("c"), EventSubEvents.ExtensionBitsTransactionCreateV1),
            (EventSubSubscriptions.ChannelGuestStarSessionBeginBeta("1", "2"), EventSubEvents.ChannelGuestStarSessionBeginBeta),
            (EventSubSubscriptions.ChannelGuestStarSessionEndBeta("1", "2"), EventSubEvents.ChannelGuestStarSessionEndBeta),
            (EventSubSubscriptions.ChannelGuestStarGuestUpdateBeta("1", "2"), EventSubEvents.ChannelGuestStarGuestUpdateBeta),
            (EventSubSubscriptions.ChannelGuestStarSettingsUpdateBeta("1", "2"), EventSubEvents.ChannelGuestStarSettingsUpdateBeta),
        ];
        foreach (var (spec, definition) in pairs)
        {
            Assert.Equal(spec.ToString(), definition.ToString());
            Assert.True(EventSubEvents.TryGetDefinition(spec.Type, spec.Version, out var resolved));
            Assert.Same(definition, resolved);
        }
        Assert.Equal(typeof(IReadOnlyList<DropEntitlementGrantEvent>), ((IEventSubEventDefinition)EventSubEvents.DropEntitlementGrantV1).EventType);
        Assert.Equal(4, pairs.Count(p => p.Spec.Version == "beta"));
    }

    [Fact]
    public void BroadcasterFactoriesRequireTheBroadcasterTokenAndDocumentedScope()
    {
        (Func<string, EventSubSubscriptionSpec> Create, string Scope)[] cases =
        [
            (EventSubSubscriptions.ChannelCharityCampaignDonateV1, TwitchScopes.ChannelReadCharity),
            (EventSubSubscriptions.ChannelCharityCampaignStartV1, TwitchScopes.ChannelReadCharity),
            (EventSubSubscriptions.ChannelCharityCampaignProgressV1, TwitchScopes.ChannelReadCharity),
            (EventSubSubscriptions.ChannelCharityCampaignStopV1, TwitchScopes.ChannelReadCharity),
            (EventSubSubscriptions.ChannelGoalBeginV1, TwitchScopes.ChannelReadGoals),
            (EventSubSubscriptions.ChannelGoalProgressV1, TwitchScopes.ChannelReadGoals),
            (EventSubSubscriptions.ChannelGoalEndV1, TwitchScopes.ChannelReadGoals),
            (EventSubSubscriptions.ChannelHypeTrainBeginV2, TwitchScopes.ChannelReadHypeTrain),
            (EventSubSubscriptions.ChannelHypeTrainProgressV2, TwitchScopes.ChannelReadHypeTrain),
            (EventSubSubscriptions.ChannelHypeTrainEndV2, TwitchScopes.ChannelReadHypeTrain),
        ];
        foreach (var (create, scope) in cases)
        {
            var spec = create("1337");
            AssertCondition(spec, ("broadcaster_user_id", "1337"));
            Assert.Equal(new[] { scope }, spec.RequiredScopes);
            Assert.Empty(spec.AnyOfScopes);
            Assert.Equal(("1337", EventSubTransports.All), (spec.AuthorizingUserId, spec.Transports));
            Assert.ThrowsAny<ArgumentException>(() => create(" "));
            Assert.ThrowsAny<ArgumentException>(() => create(null!));
        }
    }

    [Fact]
    public void UserAndGuestStarFactoriesDescribeTheirAuthorizingUser()
    {
        var whisper = EventSubSubscriptions.UserWhisperMessageV1("423374343");
        AssertCondition(whisper, ("user_id", "423374343"));
        Assert.Equal(new[] { TwitchScopes.UserReadWhispers, TwitchScopes.UserManageWhispers }, whisper.AnyOfScopes);
        Assert.Empty(whisper.RequiredScopes);
        Assert.Equal(("423374343", EventSubTransports.All), (whisper.AuthorizingUserId, whisper.Transports));

        var update = EventSubSubscriptions.UserUpdateV1("1337");
        AssertCondition(update, ("user_id", "1337"));
        Assert.Empty(update.RequiredScopes);
        Assert.Empty(update.AnyOfScopes);
        Assert.Null(update.AuthorizingUserId);
        Assert.Equal(EventSubTransports.All, update.Transports);
        Assert.ThrowsAny<ArgumentException>(() => EventSubSubscriptions.UserUpdateV1(""));
        Assert.ThrowsAny<ArgumentException>(() => EventSubSubscriptions.UserWhisperMessageV1(" "));

        Func<string, string, EventSubSubscriptionSpec>[] guestStar =
        [
            EventSubSubscriptions.ChannelGuestStarSessionBeginBeta, EventSubSubscriptions.ChannelGuestStarSessionEndBeta,
            EventSubSubscriptions.ChannelGuestStarGuestUpdateBeta, EventSubSubscriptions.ChannelGuestStarSettingsUpdateBeta,
        ];
        foreach (var create in guestStar)
        {
            var spec = create("1337", "1338");
            Assert.StartsWith("channel.guest_star_", spec.Type, StringComparison.Ordinal);
            Assert.Equal("beta", spec.Version);
            AssertCondition(spec, ("broadcaster_user_id", "1337"), ("moderator_user_id", "1338"));
            Assert.Equal(new[] { TwitchScopes.ChannelReadGuestStar, TwitchScopes.ChannelManageGuestStar, TwitchScopes.ModeratorReadGuestStar, TwitchScopes.ModeratorManageGuestStar }, spec.AnyOfScopes);
            Assert.Empty(spec.RequiredScopes);
            Assert.Equal(("1338", EventSubTransports.All), (spec.AuthorizingUserId, spec.Transports));
            Assert.ThrowsAny<ArgumentException>(() => create("1337", " "));
            Assert.ThrowsAny<ArgumentException>(() => create("", "1338"));
        }
    }

    [Fact]
    public void AppTokenFactoriesAreWebhookAndConduitOnlyAndOmitUnsetOptionalConditions()
    {
        foreach (var (spec, key, value) in AppTokenSpecs())
        {
            AssertCondition(spec, (key, value));
            Assert.Equal(AppTokenTransports, spec.Transports);
            Assert.Null(spec.AuthorizingUserId);
            Assert.Empty(spec.RequiredScopes);
            Assert.Empty(spec.AnyOfScopes);
        }
        AssertCondition(EventSubSubscriptions.ConduitShardDisabledV1("client", "conduit"), ("client_id", "client"), ("conduit_id", "conduit"));
        AssertCondition(EventSubSubscriptions.DropEntitlementGrantV1("9001", "9002", "9003"), ("organization_id", "9001"), ("category_id", "9002"), ("campaign_id", "9003"));
        AssertCondition(EventSubSubscriptions.DropEntitlementGrantV1("9001", campaignId: "9003"), ("organization_id", "9001"), ("campaign_id", "9003"));
        Assert.ThrowsAny<ArgumentException>(() => EventSubSubscriptions.ConduitShardDisabledV1("client", " "));
        Assert.ThrowsAny<ArgumentException>(() => EventSubSubscriptions.ConduitShardDisabledV1(""));
        Assert.ThrowsAny<ArgumentException>(() => EventSubSubscriptions.DropEntitlementGrantV1(" "));
        Assert.ThrowsAny<ArgumentException>(() => EventSubSubscriptions.DropEntitlementGrantV1("9001", categoryId: ""));
        Assert.ThrowsAny<ArgumentException>(() => EventSubSubscriptions.UserAuthorizationGrantV1(" "));
        Assert.ThrowsAny<ArgumentException>(() => EventSubSubscriptions.UserAuthorizationRevokeV1(null!));
        Assert.ThrowsAny<ArgumentException>(() => EventSubSubscriptions.ExtensionBitsTransactionCreateV1(""));
    }

    [Fact]
    public async Task AppTokenTypesRejectWebSocketsAndUserTokensBeforeHttp()
    {
        var bodies = new List<JsonElement>();
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            bodies.Add(body.RootElement.Clone());
            return TestHttpHandler.Json(Accepted, HttpStatusCode.Accepted);
        }));
        var webhook = new EventSubTransportRequest { Method = "webhook", Callback = "https://example.com/callback", Secret = "0123456789abcdef" };
        var conduit = new EventSubTransportRequest { Method = "conduit", ConduitId = "conduit" };
        var app = Helix(http, new("app", kind: TwitchTokenKind.App, clientId: "client"));
        var user = Helix(http, User("1337"));
        var specs = AppTokenSpecs().Select(s => s.Spec).ToArray();
        foreach (var spec in specs)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => user.SubscribeWebSocketAsync(spec, "session"));
            await Assert.ThrowsAsync<ArgumentException>(() => app.SubscribeWebSocketAsync(spec, "session"));
            await Assert.ThrowsAsync<TwitchAuthorizationException>(() => user.CreateEventSubSubscriptionAsync(spec, webhook));
            await Assert.ThrowsAsync<TwitchAuthorizationException>(() => user.CreateEventSubSubscriptionAsync(spec, conduit));
            await app.CreateEventSubSubscriptionAsync(spec, webhook);
            await app.CreateEventSubSubscriptionAsync(spec, conduit);
        }
        Assert.Equal(specs.Length * 2, bodies.Count);
        var drop = bodies.Single(b => b.GetProperty("type").GetString() == "drop.entitlement.grant" && b.GetProperty("transport").GetProperty("method").GetString() == "conduit");
        Assert.Equal("9001", drop.GetProperty("condition").GetProperty("organization_id").GetString());
        Assert.False(drop.GetProperty("condition").TryGetProperty("category_id", out _));
        Assert.Equal("conduit", drop.GetProperty("transport").GetProperty("conduit_id").GetString());
    }

    [Fact]
    public async Task WebSocketSubscriptionsPreflightTheConditionUserAndAlternativeScopes()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json(Accepted, HttpStatusCode.Accepted)); }));
        var whisper = EventSubSubscriptions.UserWhisperMessageV1("1337");
        await Helix(http, User("1337", TwitchScopes.UserManageWhispers)).SubscribeWebSocketAsync(whisper, "session");
        await Helix(http, User("1337", TwitchScopes.UserReadWhispers)).SubscribeWebSocketAsync(whisper, "session");
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Helix(http, User("1337", TwitchScopes.UserReadChat)).SubscribeWebSocketAsync(whisper, "session"));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Helix(http, User("42", TwitchScopes.UserReadWhispers)).SubscribeWebSocketAsync(whisper, "session"));

        var guestStar = EventSubSubscriptions.ChannelGuestStarGuestUpdateBeta("1337", "1338");
        await Helix(http, User("1338", TwitchScopes.ModeratorReadGuestStar)).SubscribeWebSocketAsync(guestStar, "session");
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Helix(http, User("1337", TwitchScopes.ChannelReadGuestStar)).SubscribeWebSocketAsync(guestStar, "session"));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Helix(http, User("1338", TwitchScopes.ModeratorReadChatters)).SubscribeWebSocketAsync(guestStar, "session"));

        var hypeTrain = EventSubSubscriptions.ChannelHypeTrainBeginV2("1337");
        await Helix(http, User("1337", TwitchScopes.ChannelReadHypeTrain)).SubscribeWebSocketAsync(hypeTrain, "session");
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Helix(http, User("1337")).SubscribeWebSocketAsync(hypeTrain, "session"));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Helix(http, new("app", kind: TwitchTokenKind.App)).SubscribeWebSocketAsync(hypeTrain, "session"));

        await Helix(http, User("42")).SubscribeWebSocketAsync(EventSubSubscriptions.UserUpdateV1("1337"), "session");
        Assert.Equal(5, calls);
    }

    [Fact]
    public async Task RouterDispatchesBatchedDropEntitlementsAsOneTypedList()
    {
        IReadOnlyList<DropEntitlementGrantEvent>? batch = null;
        ChannelHypeTrainBeginEvent? train = null;
        var router = new EventSubEventRouter()
            .On(EventSubEvents.DropEntitlementGrantV1, (events, subscription, _) => { batch = events; Assert.Equal("drop.entitlement.grant", subscription.Type); return Task.CompletedTask; })
            .On(EventSubEvents.ChannelHypeTrainBeginV2, (evt, _, _) => { train = evt; return Task.CompletedTask; });

        var drops = Notification("drop.entitlement.grant", "1", ContractAssertions.Fixture(Fixture, "drop.entitlement.grant@1"));
        Assert.True(await router.DispatchAsync(drops));
        Assert.NotNull(batch);
        Assert.Equal(new[] { "bf7c8577-e3e3-4881-a78a-e9446641d45d", "bf7c8577-e3e3-4881-a78a-e9446641d45c" }, batch.Select(e => e.Id));
        Assert.Equal(new[] { "1234", "12345" }, batch.Select(e => e.Data.UserId));
        Assert.True(drops.TryReadEvent(EventSubEvents.DropEntitlementGrantV1, out var read));
        Assert.Equal("fb78259e-fb81-4d1b-8333-34a06ffc24c1", read[1].Data.EntitlementId);
        Assert.False(drops.TryReadEvent(EventSubEvents.ChannelHypeTrainBeginV2, out _));

        var hypeTrain = ContractAssertions.Fixture(Fixture, "channel.hype_train.begin@2");
        Assert.True(await router.DispatchAsync(Notification("channel.hype_train.begin", "2", hypeTrain)));
        Assert.Equal(4_294_967_296L, train!.AllTimeHighTotal);
        Assert.False(await router.DispatchAsync(Notification("channel.hype_train.begin", "1", hypeTrain)));
    }

    private static IEnumerable<(EventSubSubscriptionSpec Spec, string Key, string Value)> AppTokenSpecs() =>
    [
        (EventSubSubscriptions.UserAuthorizationGrantV1("client"), "client_id", "client"),
        (EventSubSubscriptions.UserAuthorizationRevokeV1("client"), "client_id", "client"),
        (EventSubSubscriptions.ConduitShardDisabledV1("client"), "client_id", "client"),
        (EventSubSubscriptions.DropEntitlementGrantV1("9001"), "organization_id", "9001"),
        (EventSubSubscriptions.ExtensionBitsTransactionCreateV1("deadbeef"), "extension_client_id", "deadbeef"),
    ];

    private static void AssertCondition(EventSubSubscriptionSpec spec, params (string Key, string Value)[] expected)
        => Assert.Equal(expected.OrderBy(e => e.Key, StringComparer.Ordinal), spec.Condition.Select(c => (c.Key, c.Value)).OrderBy(e => e.Key, StringComparer.Ordinal));

    private static HelixClient Helix(HttpClient http, AccessToken token)
        => new(new(http, new StaticAccessTokenProvider(token), new() { ClientId = "client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 }));

    private static AccessToken User(string userId, params string[] scopes) => new("user", scopes: scopes, kind: TwitchTokenKind.User, userId: userId);

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static EventSubMessage Notification(string type, string version, string eventJson) => EventSubMessage.Parse(Encoding.UTF8.GetBytes(
        $$$"""{"metadata":{"message_id":"m1","message_type":"notification","message_timestamp":"2026-10-09T12:00:00.123456789Z","subscription_type":"{{{type}}}","subscription_version":"{{{version}}}"},"payload":{"subscription":{"id":"s1","status":"enabled","type":"{{{type}}}","version":"{{{version}}}","condition":{"organization_id":"9001"},"transport":{"method":"conduit","conduit_id":"conduit"},"created_at":"2026-10-09T12:00:00Z","cost":0},"event":{{{eventJson}}}}}"""));
}
