using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TwitchSdk.Core;
using TwitchSdk.EventSub;
using TwitchSdk.EventSub.Events;
using TwitchSdk.Helix;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Tests;

public sealed class EventSubMonetizationInteractionTests
{
    private const string FixtureFile = "eventsub-monetization-interaction.json";
    private const string RewardId = "92af127c-7326-4483-a52b-b0da0be61c01";
    private const string AutomaticRewardType = "channel.channel_points_automatic_reward_redemption.add";

    [Fact]
    public void BitsUseContractKeepsFragmentsPowerUpsAndLargeBitCounts()
    {
        var evt = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelBitsUseV1);
        Assert.Equal(3_000_000_000L, evt.Bits);
        Assert.Equal("power_up", evt.Type);
        Assert.Equal(new[] { "cheermote", "text", "emote" }, evt.Message!.Fragments.Select(f => f.Type));
        var cheermote = evt.Message.Fragments[0].Cheermote!;
        Assert.Equal(("cheer", 1L, 1), (cheermote.Prefix, cheermote.Bits, cheermote.Tier));
        var emote = evt.Message.Fragments[2].Emote!;
        Assert.Equal(("25", "0", "0"), (emote.Id, emote.EmoteSetId, emote.OwnerId));
        Assert.Equal(new[] { "static", "animated" }, emote.Format);
        Assert.Equal(("gigantify_an_emote", "MegaLUL", "cosmic-abyss"), (evt.PowerUp!.Type, evt.PowerUp.Emote!.Name, evt.PowerUp.MessageEffectId));
        Assert.Equal(("Hydrate", RewardId), (evt.CustomPowerUp!.Title, evt.CustomPowerUp.RewardId));

        var cheer = RoundTrip("channel.bits.use@1#cheer", EventSubEvents.ChannelBitsUseV1);
        Assert.Equal(("cheer", 2L), (cheer.Type, cheer.Bits));
        Assert.Null(cheer.PowerUp);
        Assert.Null(cheer.CustomPowerUp);
        Assert.All(cheer.Message!.Fragments, fragment => Assert.Null(fragment.Emote));
    }

    [Fact]
    public void SubscriptionContractsExposeTiersAnonymousGiftersAndHiddenStreaks()
    {
        var subscribe = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelSubscribeV1);
        Assert.Equal(("1234", "1000", false), (subscribe.UserId, subscribe.Tier, subscribe.IsGift));
        var end = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelSubscriptionEndV1);
        Assert.Equal(("3000", true), (end.Tier, end.IsGift));

        var anonymous = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelSubscriptionGiftV1);
        Assert.True(anonymous.IsAnonymous);
        Assert.Null(anonymous.UserId);
        Assert.Null(anonymous.UserLogin);
        Assert.Null(anonymous.UserName);
        Assert.Null(anonymous.CumulativeTotal);
        Assert.Equal(50, anonymous.Total);
        var named = RoundTrip("channel.subscription.gift@1#named", EventSubEvents.ChannelSubscriptionGiftV1);
        Assert.Equal(("1234", "cool_user", "Cool_User", (int?)284, false), (named.UserId, named.UserLogin, named.UserName, named.CumulativeTotal, named.IsAnonymous));

        var resub = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelSubscriptionMessageV1);
        Assert.Null(resub.StreakMonths);
        Assert.Equal((15, 6), (resub.CumulativeMonths, resub.DurationMonths));
        var range = Assert.Single(resub.Message.Emotes);
        Assert.Equal("302976485", range.Id);
        Assert.Equal("FevziGG", resub.Message.Text[range.Begin..(range.End + 1)]);
    }

    [Fact]
    public void CheerContractsSeparateAnonymousCheersFromNamedCheers()
    {
        var anonymous = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelCheerV1);
        Assert.True(anonymous.IsAnonymous);
        Assert.Null(anonymous.UserId);
        Assert.Null(anonymous.UserLogin);
        Assert.Null(anonymous.UserName);
        Assert.Equal(2_500_000_000L, anonymous.Bits);

        var named = RoundTrip("channel.cheer@1#named", EventSubEvents.ChannelCheerV1);
        Assert.Equal(("1234", "cool_user", "Cool_User", "pogchamp", 1000L), (named.UserId, named.UserLogin, named.UserName, named.Message, named.Bits));
    }

    [Fact]
    public void AutomaticRewardVersionsUseSeparatePayloads()
    {
        var v1 = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelPointsAutomaticRewardRedemptionAddV1);
        Assert.Equal(("chosen_sub_emote_unlock", 100L, "MegaLUL"), (v1.Reward.Type, v1.Reward.Cost, v1.Reward.UnlockedEmote!.Name));
        var range = Assert.Single(v1.Message!.Emotes);
        Assert.Equal("VoHiYo", v1.Message.Text[range.Begin..(range.End + 1)]);
        Assert.Equal("Hello world! VoHiYo ", v1.UserInput);
        Assert.Equal(DateTimeOffset.Parse("2024-02-23T21:14:34.2603980Z"), v1.RedeemedAt);
        var official = RoundTrip($"{AutomaticRewardType}@1#official", EventSubEvents.ChannelPointsAutomaticRewardRedemptionAddV1);
        Assert.Null(official.Reward.UnlockedEmote);
        Assert.Null(official.UserInput);

        var v2 = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelPointsAutomaticRewardRedemptionAddV2);
        Assert.Equal((2_147_483_648L, "MegaLUL_BW"), (v2.Reward.ChannelPoints, v2.Reward.Emote!.Name));
        Assert.Equal(new[] { "text", "emote" }, v2.Message!.Fragments.Select(f => f.Type));
        Assert.Null(v2.Message.Fragments[0].Emote);
        Assert.Equal("81274", v2.Message.Fragments[1].Emote!.Id);
        var noMessage = RoundTrip($"{AutomaticRewardType}@2#no-message", EventSubEvents.ChannelPointsAutomaticRewardRedemptionAddV2);
        Assert.Null(noMessage.Message);
        Assert.Null(noMessage.Reward.Emote);
    }

    [Fact]
    public void CustomRewardContractsShareThePayloadAndKeepNullableCooldowns()
    {
        var added = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelPointsCustomRewardAddV1);
        Assert.Null(added.Image);
        Assert.Null(added.CooldownExpiresAt);
        Assert.Null(added.RedemptionsRedeemedCurrentStream);
        Assert.Equal("https://static-cdn.jtvnw.net/default-4.png", added.DefaultImage.Url4x);
        Assert.Equal((true, 1000L, true, 1000L), (added.MaxPerStream.IsEnabled, added.MaxPerStream.Value, added.GlobalCooldown.IsEnabled, added.GlobalCooldown.Seconds));

        var updated = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelPointsCustomRewardUpdateV1);
        Assert.Equal((3_000_000_000L, (long?)123), (updated.Cost, updated.RedemptionsRedeemedCurrentStream));
        Assert.Equal(DateTimeOffset.Parse("2019-11-16T10:11:12.6342346Z"), updated.CooldownExpiresAt);
        Assert.Equal((true, false, false, true), (updated.IsPaused, updated.IsInStock, updated.MaxPerUserPerStream.IsEnabled, updated.ShouldRedemptionsSkipRequestQueue));
        Assert.Equal("https://static-cdn.jtvnw.net/image-1.png", updated.Image!.Url1x);

        ChannelPointsCustomRewardEventBase removed = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelPointsCustomRewardRemoveV1);
        Assert.Equal(DateTimeOffset.Parse("2019-11-16T10:11:12.123Z"), removed.CooldownExpiresAt);

        var emptyCooldown = JsonNode.Parse(ContractAssertions.Fixture(FixtureFile, "channel.channel_points_custom_reward.add@1"))!.AsObject();
        emptyCooldown["cooldown_expires_at"] = "";
        using var document = JsonDocument.Parse(emptyCooldown.ToJsonString());
        Assert.Null(EventSubEvents.ChannelPointsCustomRewardAddV1.Deserialize(document.RootElement).CooldownExpiresAt);
    }

    [Fact]
    public void RedemptionContractsKeepRewardSnapshotsStatusesAndEmptyInput()
    {
        var added = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelPointsCustomRewardRedemptionAddV1);
        Assert.Equal(("unfulfilled", "pogchamp", RewardId, 100L), (added.Status, added.UserInput, added.Reward.Id, added.Reward.Cost));
        Assert.Equal(DateTimeOffset.Parse("2020-07-15T17:16:03.1710671Z"), added.RedeemedAt);
        var updated = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelPointsCustomRewardRedemptionUpdateV1);
        Assert.Equal(("canceled", "", ""), (updated.Status, updated.UserInput, updated.Reward.Prompt));

        var powerUp = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelCustomPowerUpRedemptionAddV1);
        Assert.Equal((RewardId, 100L, "Power-up prompt"), (powerUp.CustomPowerUp.Id, powerUp.CustomPowerUp.Bits, powerUp.CustomPowerUp.Prompt));
        Assert.Equal(("9001", "unfulfilled"), (powerUp.UserId, powerUp.Status));
    }

    [Fact]
    public void PollContractsKeepChoicesVotingSettingsAndTimestamps()
    {
        var begin = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelPollBeginV1);
        Assert.Equal(new[] { "Yeah!", "No!", "Maybe!" }, begin.Choices.Select(c => c.Title));
        Assert.All(begin.Choices, choice => Assert.Equal(0, choice.Votes));
        Assert.Equal((false, 0L, true, 10L), (begin.BitsVoting.IsEnabled, begin.BitsVoting.AmountPerVote, begin.ChannelPointsVoting.IsEnabled, begin.ChannelPointsVoting.AmountPerVote));
        Assert.Equal(TimeSpan.FromSeconds(5), begin.EndsAt - begin.StartedAt);

        var progress = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelPollProgressV1);
        Assert.Equal(new[] { 12L, 14L, 7L }, progress.Choices.Select(c => c.Votes));
        Assert.Equal(new[] { 7L, 4L, 7L }, progress.Choices.Select(c => c.ChannelPointsVotes));

        var end = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelPollEndV1);
        Assert.Equal(("completed", 3_000_000_120L), (end.Status, end.Choices[0].Votes));
        Assert.Equal(DateTimeOffset.Parse("2020-07-15T17:16:11.1710671Z"), end.EndedAt);
    }

    [Fact]
    public void PredictionContractsKeepOutcomesTopPredictorsAndResults()
    {
        var begin = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelPredictionBeginV1);
        Assert.Equal(new[] { "blue", "pink" }, begin.Outcomes.Select(o => o.Color));
        Assert.All(begin.Outcomes, outcome => Assert.Empty(outcome.TopPredictors));
        Assert.Equal(TimeSpan.FromMinutes(5), begin.LocksAt - begin.StartedAt);
        var nullPredictors = JsonNode.Parse(ContractAssertions.Fixture(FixtureFile, "channel.prediction.begin@1"))!.AsObject();
        nullPredictors["outcomes"]![0]!["top_predictors"] = null;
        using (var document = JsonDocument.Parse(nullPredictors.ToJsonString()))
            Assert.Empty(EventSubEvents.ChannelPredictionBeginV1.Deserialize(document.RootElement).Outcomes[0].TopPredictors);

        var progress = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelPredictionProgressV1);
        Assert.Equal((10L, 2_500_000_000L), (progress.Outcomes[0].Users, progress.Outcomes[0].ChannelPoints));
        Assert.All(progress.Outcomes.SelectMany(o => o.TopPredictors), predictor => Assert.Null(predictor.ChannelPointsWon));
        Assert.Equal(("1234", 500L), (progress.Outcomes[0].TopPredictors[0].UserId, progress.Outcomes[0].TopPredictors[0].ChannelPointsUsed));

        var locked = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelPredictionLockV1);
        Assert.Equal(DateTimeOffset.Parse("2020-07-15T17:21:03.1710671Z"), locked.LockedAt);

        var end = EventSubContractAssertions.Verify(FixtureFile, EventSubEvents.ChannelPredictionEndV1);
        Assert.Equal(("12345", "resolved"), (end.WinningOutcomeId, end.Status));
        Assert.Equal(10000L, end.Outcomes[0].TopPredictors[0].ChannelPointsWon);
        Assert.Equal(new long?[] { null, 0 }, end.Outcomes[1].TopPredictors.Select(p => p.ChannelPointsWon));
        var canceled = RoundTrip("channel.prediction.end@1#canceled", EventSubEvents.ChannelPredictionEndV1);
        Assert.Null(canceled.WinningOutcomeId);
        Assert.Equal("canceled", canceled.Status);
    }

    [Fact]
    public void EveryGroupDefinitionIsRegisteredAndMatchesItsFactory()
    {
        var pairs = new (IEventSubEventDefinition Definition, EventSubSubscriptionSpec Spec)[]
        {
            (EventSubEvents.ChannelBitsUseV1, EventSubSubscriptions.ChannelBitsUseV1("1337")),
            (EventSubEvents.ChannelSubscribeV1, EventSubSubscriptions.ChannelSubscribeV1("1337")),
            (EventSubEvents.ChannelSubscriptionEndV1, EventSubSubscriptions.ChannelSubscriptionEndV1("1337")),
            (EventSubEvents.ChannelSubscriptionGiftV1, EventSubSubscriptions.ChannelSubscriptionGiftV1("1337")),
            (EventSubEvents.ChannelSubscriptionMessageV1, EventSubSubscriptions.ChannelSubscriptionMessageV1("1337")),
            (EventSubEvents.ChannelCheerV1, EventSubSubscriptions.ChannelCheerV1("1337")),
            (EventSubEvents.ChannelPointsAutomaticRewardRedemptionAddV1, EventSubSubscriptions.ChannelPointsAutomaticRewardRedemptionAddV1("1337")),
            (EventSubEvents.ChannelPointsAutomaticRewardRedemptionAddV2, EventSubSubscriptions.ChannelPointsAutomaticRewardRedemptionAddV2("1337")),
            (EventSubEvents.ChannelPointsCustomRewardAddV1, EventSubSubscriptions.ChannelPointsCustomRewardAddV1("1337")),
            (EventSubEvents.ChannelPointsCustomRewardUpdateV1, EventSubSubscriptions.ChannelPointsCustomRewardUpdateV1("1337")),
            (EventSubEvents.ChannelPointsCustomRewardRemoveV1, EventSubSubscriptions.ChannelPointsCustomRewardRemoveV1("1337")),
            (EventSubEvents.ChannelPointsCustomRewardRedemptionAddV1, EventSubSubscriptions.ChannelPointsCustomRewardRedemptionAddV1("1337")),
            (EventSubEvents.ChannelPointsCustomRewardRedemptionUpdateV1, EventSubSubscriptions.ChannelPointsCustomRewardRedemptionUpdateV1("1337")),
            (EventSubEvents.ChannelCustomPowerUpRedemptionAddV1, EventSubSubscriptions.ChannelCustomPowerUpRedemptionAddV1("1337")),
            (EventSubEvents.ChannelPollBeginV1, EventSubSubscriptions.ChannelPollBeginV1("1337")),
            (EventSubEvents.ChannelPollProgressV1, EventSubSubscriptions.ChannelPollProgressV1("1337")),
            (EventSubEvents.ChannelPollEndV1, EventSubSubscriptions.ChannelPollEndV1("1337")),
            (EventSubEvents.ChannelPredictionBeginV1, EventSubSubscriptions.ChannelPredictionBeginV1("1337")),
            (EventSubEvents.ChannelPredictionProgressV1, EventSubSubscriptions.ChannelPredictionProgressV1("1337")),
            (EventSubEvents.ChannelPredictionLockV1, EventSubSubscriptions.ChannelPredictionLockV1("1337")),
            (EventSubEvents.ChannelPredictionEndV1, EventSubSubscriptions.ChannelPredictionEndV1("1337")),
        };
        foreach (var (definition, spec) in pairs)
        {
            Assert.Equal(definition.ToString(), spec.ToString());
            Assert.True(EventSubEvents.TryGetDefinition(definition.Type, definition.Version, out var registered));
            Assert.Same(definition, registered);
            Assert.Equal(new[] { KeyValuePair.Create("broadcaster_user_id", "1337") }, spec.Condition);
            Assert.Equal("1337", spec.AuthorizingUserId);
            Assert.Equal(EventSubTransports.All, spec.Transports);
        }
        Assert.Equal(pairs.Length, pairs.Select(p => p.Definition.ToString()).Distinct().Count());
    }

    [Fact]
    public void FactoriesCarryTheDocumentedScopes()
    {
        var bits = new[] { EventSubSubscriptions.ChannelBitsUseV1("1"), EventSubSubscriptions.ChannelCheerV1("1"), EventSubSubscriptions.ChannelCustomPowerUpRedemptionAddV1("1") };
        AssertScopes(bits, [TwitchScopes.BitsRead], []);
        var subscriptions = new[]
        {
            EventSubSubscriptions.ChannelSubscribeV1("1"), EventSubSubscriptions.ChannelSubscriptionEndV1("1"),
            EventSubSubscriptions.ChannelSubscriptionGiftV1("1"), EventSubSubscriptions.ChannelSubscriptionMessageV1("1"),
        };
        AssertScopes(subscriptions, [TwitchScopes.ChannelReadSubscriptions], []);
        var redemptions = new[]
        {
            EventSubSubscriptions.ChannelPointsAutomaticRewardRedemptionAddV1("1"), EventSubSubscriptions.ChannelPointsAutomaticRewardRedemptionAddV2("1"),
            EventSubSubscriptions.ChannelPointsCustomRewardAddV1("1"), EventSubSubscriptions.ChannelPointsCustomRewardUpdateV1("1"),
            EventSubSubscriptions.ChannelPointsCustomRewardRemoveV1("1"), EventSubSubscriptions.ChannelPointsCustomRewardRedemptionAddV1("1"),
            EventSubSubscriptions.ChannelPointsCustomRewardRedemptionUpdateV1("1"),
        };
        AssertScopes(redemptions, [], [TwitchScopes.ChannelReadRedemptions, TwitchScopes.ChannelManageRedemptions]);
        var polls = new[] { EventSubSubscriptions.ChannelPollBeginV1("1"), EventSubSubscriptions.ChannelPollProgressV1("1"), EventSubSubscriptions.ChannelPollEndV1("1") };
        AssertScopes(polls, [], [TwitchScopes.ChannelReadPolls, TwitchScopes.ChannelManagePolls]);
        var predictions = new[]
        {
            EventSubSubscriptions.ChannelPredictionBeginV1("1"), EventSubSubscriptions.ChannelPredictionProgressV1("1"),
            EventSubSubscriptions.ChannelPredictionLockV1("1"), EventSubSubscriptions.ChannelPredictionEndV1("1"),
        };
        AssertScopes(predictions, [], [TwitchScopes.ChannelReadPredictions, TwitchScopes.ChannelManagePredictions]);
        Assert.Equal(21, bits.Length + subscriptions.Length + redemptions.Length + polls.Length + predictions.Length);
    }

    [Fact]
    public void RewardFiltersAreOptionalAndAllConditionValuesAreValidated()
    {
        var filtered = new Func<string, string?, EventSubSubscriptionSpec>[]
        {
            EventSubSubscriptions.ChannelPointsCustomRewardUpdateV1, EventSubSubscriptions.ChannelPointsCustomRewardRemoveV1,
            EventSubSubscriptions.ChannelPointsCustomRewardRedemptionAddV1, EventSubSubscriptions.ChannelPointsCustomRewardRedemptionUpdateV1,
            EventSubSubscriptions.ChannelCustomPowerUpRedemptionAddV1,
        };
        foreach (var factory in filtered)
        {
            Assert.Equal(new[] { KeyValuePair.Create("broadcaster_user_id", "1337") }, factory("1337", null).Condition);
            Assert.Equal(new[] { KeyValuePair.Create("broadcaster_user_id", "1337"), KeyValuePair.Create("reward_id", RewardId) },
                factory("1337", RewardId).Condition.OrderBy(pair => pair.Key, StringComparer.Ordinal));
            Assert.Throws<ArgumentException>(() => factory("1337", " "));
            Assert.Throws<ArgumentException>(() => factory("1337", ""));
            Assert.Throws<ArgumentException>(() => factory(" ", RewardId));
        }

        var unfiltered = new Func<string, EventSubSubscriptionSpec>[]
        {
            EventSubSubscriptions.ChannelBitsUseV1, EventSubSubscriptions.ChannelSubscribeV1, EventSubSubscriptions.ChannelSubscriptionEndV1,
            EventSubSubscriptions.ChannelSubscriptionGiftV1, EventSubSubscriptions.ChannelSubscriptionMessageV1, EventSubSubscriptions.ChannelCheerV1,
            EventSubSubscriptions.ChannelPointsAutomaticRewardRedemptionAddV1, EventSubSubscriptions.ChannelPointsAutomaticRewardRedemptionAddV2,
            EventSubSubscriptions.ChannelPointsCustomRewardAddV1, EventSubSubscriptions.ChannelPollBeginV1, EventSubSubscriptions.ChannelPollProgressV1,
            EventSubSubscriptions.ChannelPollEndV1, EventSubSubscriptions.ChannelPredictionBeginV1, EventSubSubscriptions.ChannelPredictionProgressV1,
            EventSubSubscriptions.ChannelPredictionLockV1, EventSubSubscriptions.ChannelPredictionEndV1,
        };
        foreach (var factory in unfiltered)
        {
            Assert.Throws<ArgumentException>(() => factory(""));
            Assert.Throws<ArgumentException>(() => factory("  "));
            Assert.Throws<ArgumentNullException>(() => factory(null!));
        }
    }

    [Fact]
    public async Task RedemptionWebSocketSubscriptionSendsRewardFilterAndAcceptsEitherRedemptionScope()
    {
        var spec = EventSubSubscriptions.ChannelPointsCustomRewardRedemptionAddV1("1337", RewardId);
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            calls++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("channel.channel_points_custom_reward_redemption.add", body.RootElement.GetProperty("type").GetString());
            Assert.Equal("1", body.RootElement.GetProperty("version").GetString());
            var condition = body.RootElement.GetProperty("condition");
            Assert.Equal("1337", condition.GetProperty("broadcaster_user_id").GetString());
            Assert.Equal(RewardId, condition.GetProperty("reward_id").GetString());
            Assert.Equal("session", body.RootElement.GetProperty("transport").GetProperty("session_id").GetString());
            return TestHttpHandler.Json("""{"data":[],"total":1,"total_cost":0,"max_total_cost":10}""", HttpStatusCode.Accepted);
        }));
        foreach (var scope in new[] { TwitchScopes.ChannelReadRedemptions, TwitchScopes.ChannelManageRedemptions })
            await Helix(http, new("user", scopes: [scope], kind: TwitchTokenKind.User, userId: "1337")).SubscribeWebSocketAsync(spec, "session");
        Assert.Equal(2, calls);

        var missing = await Assert.ThrowsAsync<TwitchAuthorizationException>(() =>
            Helix(http, new("user", scopes: [TwitchScopes.BitsRead], kind: TwitchTokenKind.User, userId: "1337")).SubscribeWebSocketAsync(spec, "session"));
        Assert.Equal(new[] { TwitchScopes.ChannelReadRedemptions, TwitchScopes.ChannelManageRedemptions }, missing.RequiredAnyOfScopes);
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() =>
            Helix(http, new("user", scopes: [TwitchScopes.ChannelReadRedemptions], kind: TwitchTokenKind.User, userId: "42")).SubscribeWebSocketAsync(spec, "session"));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CheerSubscriptionRequiresBitsReadFromTheBroadcasterOnWebSocketsAndAcceptsWebhooks()
    {
        var spec = EventSubSubscriptions.ChannelCheerV1("1337");
        var methods = new List<string>();
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("channel.cheer", body.RootElement.GetProperty("type").GetString());
            methods.Add(body.RootElement.GetProperty("transport").GetProperty("method").GetString()!);
            return TestHttpHandler.Json("""{"data":[],"total":1,"total_cost":1,"max_total_cost":10}""", HttpStatusCode.Accepted);
        }));
        await Helix(http, new("user", scopes: [TwitchScopes.BitsRead], kind: TwitchTokenKind.User, userId: "1337")).SubscribeWebSocketAsync(spec, "session");
        var missing = await Assert.ThrowsAsync<TwitchAuthorizationException>(() =>
            Helix(http, new("user", scopes: [TwitchScopes.ChannelReadSubscriptions], kind: TwitchTokenKind.User, userId: "1337")).SubscribeWebSocketAsync(spec, "session"));
        Assert.Equal(new[] { TwitchScopes.BitsRead }, missing.MissingScopes);
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Helix(http, new("app", kind: TwitchTokenKind.App)).SubscribeWebSocketAsync(spec, "session"));

        var webhook = new EventSubTransportRequest { Method = "webhook", Callback = "https://example.com/callback", Secret = "0123456789abcdef" };
        await Helix(http, new("app", kind: TwitchTokenKind.App)).CreateEventSubSubscriptionAsync(spec, webhook);
        Assert.Equal(new[] { "websocket", "webhook" }, methods);
    }

    [Fact]
    public async Task RouterDispatchesAutomaticRewardVersionsToSeparateHandlers()
    {
        ChannelPointsAutomaticRewardRedemptionAddEvent? v1 = null;
        ChannelPointsAutomaticRewardRedemptionAddEventV2? v2 = null;
        var router = new EventSubEventRouter()
            .On(EventSubEvents.ChannelPointsAutomaticRewardRedemptionAddV1, (evt, _, _) => { v1 = evt; return Task.CompletedTask; })
            .On(EventSubEvents.ChannelPointsAutomaticRewardRedemptionAddV2, (evt, _, _) => { v2 = evt; return Task.CompletedTask; });
        var v1Json = ContractAssertions.Fixture(FixtureFile, $"{AutomaticRewardType}@1");
        var v2Json = ContractAssertions.Fixture(FixtureFile, $"{AutomaticRewardType}@2");

        Assert.True(await router.DispatchAsync(Notification(AutomaticRewardType, "2", v2Json)));
        Assert.Null(v1);
        Assert.Equal(2_147_483_648L, v2!.Reward.ChannelPoints);
        Assert.True(await router.DispatchAsync(Notification(AutomaticRewardType, "1", v1Json)));
        Assert.Equal(100L, v1!.Reward.Cost);

        Assert.False(Notification(AutomaticRewardType, "2", v2Json).TryReadEvent(EventSubEvents.ChannelPointsAutomaticRewardRedemptionAddV1, out _));
        Assert.True(Notification("channel.cheer", "1", ContractAssertions.Fixture(FixtureFile, "channel.cheer@1")).TryReadEvent(EventSubEvents.ChannelCheerV1, out var cheer));
        Assert.True(cheer.IsAnonymous);
    }

    private static void AssertScopes(IEnumerable<EventSubSubscriptionSpec> specs, string[] required, string[] anyOf)
    {
        foreach (var spec in specs)
        {
            Assert.Equal(required, spec.RequiredScopes);
            Assert.Equal(anyOf, spec.AnyOfScopes);
        }
    }

    /// <summary>Round-trips a variant fixture (a key beyond type@version) like <see cref="EventSubContractAssertions.Verify"/>.</summary>
    private static TEvent RoundTrip<TEvent>(string key, EventSubEventDefinition<TEvent> definition) where TEvent : class
    {
        using var expected = JsonDocument.Parse(ContractAssertions.Fixture(FixtureFile, key));
        var model = definition.Deserialize(expected.RootElement);
        var context = new EventSubEventsJsonContext(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.Never });
        using var actual = JsonDocument.Parse(JsonSerializer.Serialize(model, context.GetTypeInfo(typeof(TEvent))!));
        ContractAssertions.AssertSubset(expected.RootElement, actual.RootElement);
        return model;
    }

    private static HelixClient Helix(HttpClient http, AccessToken token)
        => new(new(http, new StaticAccessTokenProvider(token), new() { ClientId = "client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 }));

    private static EventSubMessage Notification(string type, string version, string eventJson) => EventSubMessage.Parse(Encoding.UTF8.GetBytes(
        $$$"""{"metadata":{"message_id":"m1","message_type":"notification","message_timestamp":"2026-10-09T12:00:00Z","subscription_type":"{{{type}}}","subscription_version":"{{{version}}}"},"payload":{"subscription":{"id":"s1","status":"enabled","type":"{{{type}}}","version":"{{{version}}}","condition":{"broadcaster_user_id":"12826"},"transport":{"method":"websocket","session_id":"x"},"created_at":"2026-10-09T12:00:00Z","cost":0},"event":{{{eventJson}}}}}"""));
}
