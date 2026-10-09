using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TwitchDock.Core;
using TwitchDock.Helix;
using TwitchDock.Helix.Models;

namespace TwitchDock.Tests;

public sealed class PollsAndPredictionsTests
{
    private static HelixClient Client(HttpClient http, AccessToken? token = null) => new(new(http,
        new StaticAccessTokenProvider(token ?? new("token")), new() { ClientId = "client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 }));
    private static string Fixture(string id) => ContractAssertions.Fixture("helix-polls-predictions.json", id);

    public static IEnumerable<object[]> Contracts()
    {
        yield return ["get-polls", HelixJsonContext.Default.HelixPageTwitchPoll];
        yield return ["create-poll", HelixJsonContext.Default.HelixPageTwitchPoll];
        yield return ["end-poll", HelixJsonContext.Default.HelixPageTwitchPoll];
        yield return ["get-predictions", HelixJsonContext.Default.HelixPageTwitchPrediction];
        yield return ["create-prediction", HelixJsonContext.Default.HelixPageTwitchPrediction];
        yield return ["end-prediction", HelixJsonContext.Default.HelixPageTwitchPrediction];
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void ResponseContractsPreserveEveryDocumentedField(string id, JsonTypeInfo type)
        => ContractAssertions.Verify("helix-polls-predictions.json", id, type);

    [Fact]
    public async Task CreatePollSendsCompleteBodyAndPreservesNullableEndTime()
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/helix/polls", request.RequestUri!.AbsolutePath);
            Assert.Equal("", request.RequestUri.Query);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var body = json.RootElement;
            Assert.Equal("1", body.GetProperty("broadcaster_id").GetString());
            Assert.Equal("Next game?", body.GetProperty("title").GetString());
            Assert.Equal(2, body.GetProperty("choices").GetArrayLength());
            Assert.Equal("One", body.GetProperty("choices")[0].GetProperty("title").GetString());
            Assert.Equal(60, body.GetProperty("duration").GetInt32());
            Assert.True(body.GetProperty("channel_points_voting_enabled").GetBoolean());
            Assert.Equal(10, body.GetProperty("channel_points_per_vote").GetInt32());
            Assert.False(body.TryGetProperty("bits_voting_enabled", out _));
            return TestHttpHandler.Json(Fixture("create-poll"));
        }));
        var poll = (await Client(http).Polls.CreatePollAsync(new()
        {
            BroadcasterId = "1", Title = "Next game?", Choices = [new() { Title = "One" }, new() { Title = "Two" }],
            Duration = 60, ChannelPointsVotingEnabled = true, ChannelPointsPerVote = 10
        })).Data.Single();
        Assert.Null(poll.EndedAt);
        Assert.Equal(2147483648, poll.Choices[0].Votes);
    }

    [Fact]
    public async Task DisabledAdditionalVotingIsPreservedAndCostIsOmitted()
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.False(json.RootElement.GetProperty("channel_points_voting_enabled").GetBoolean());
            Assert.False(json.RootElement.TryGetProperty("channel_points_per_vote", out _));
            return TestHttpHandler.Json(Fixture("create-poll"));
        }));
        await Client(http).Polls.CreatePollAsync(new() { BroadcasterId = "1", Title = "Title", Choices = [new() { Title = "A" }, new() { Title = "B" }], Duration = 15, ChannelPointsVotingEnabled = false });
    }

    [Fact]
    public async Task CreatePredictionAllowsTenOutcomesAndPreservesNullableState()
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/helix/predictions", request.RequestUri!.AbsolutePath);
            Assert.Equal("", request.RequestUri.Query);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var body = json.RootElement;
            Assert.Equal(4, body.EnumerateObject().Count());
            Assert.Equal("1", body.GetProperty("broadcaster_id").GetString());
            Assert.Equal("Will it work?", body.GetProperty("title").GetString());
            Assert.Equal(1800, body.GetProperty("prediction_window").GetInt32());
            Assert.Equal(10, body.GetProperty("outcomes").GetArrayLength());
            Assert.Equal("Outcome 9", body.GetProperty("outcomes")[9].GetProperty("title").GetString());
            return TestHttpHandler.Json(Fixture("create-prediction"));
        }));
        var result = (await Client(http).Predictions.CreatePredictionAsync(new()
        {
            BroadcasterId = "1", Title = "Will it work?", PredictionWindow = 1800,
            Outcomes = Enumerable.Range(0, 10).Select(i => new PredictionOutcomeRequest { Title = $"Outcome {i}" }).ToArray()
        })).Data.Single();
        Assert.Null(result.WinningOutcomeId);
        Assert.Null(result.EndedAt);
        Assert.Null(result.LockedAt);
        Assert.Null(result.Outcomes[1].TopPredictors);
        Assert.Null(result.Outcomes[0].TopPredictors![0].ChannelPointsWon);
    }

    [Theory]
    [InlineData("TERMINATED")]
    [InlineData("ARCHIVED")]
    public async Task EndPollWritesSupportedTransition(string status)
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("/helix/polls", request.RequestUri!.AbsolutePath);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal(3, json.RootElement.EnumerateObject().Count());
            Assert.Equal("1", json.RootElement.GetProperty("broadcaster_id").GetString());
            Assert.Equal("p1", json.RootElement.GetProperty("id").GetString());
            Assert.Equal(status, json.RootElement.GetProperty("status").GetString());
            return TestHttpHandler.Json(Fixture("end-poll"));
        }));
        Assert.NotNull((await Client(http).Polls.EndPollAsync(new() { BroadcasterId = "1", Id = "p1", Status = status })).Data.Single().EndedAt);
    }

    [Theory]
    [InlineData("LOCKED")]
    [InlineData("CANCELED")]
    [InlineData("RESOLVED")]
    public async Task EndPredictionWritesWinnerOnlyWhenSupplied(string status)
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("/helix/predictions", request.RequestUri!.AbsolutePath);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("1", json.RootElement.GetProperty("broadcaster_id").GetString());
            Assert.Equal("p1", json.RootElement.GetProperty("id").GetString());
            Assert.Equal(status, json.RootElement.GetProperty("status").GetString());
            if (status == "RESOLVED") Assert.Equal("o1", json.RootElement.GetProperty("winning_outcome_id").GetString());
            else Assert.False(json.RootElement.TryGetProperty("winning_outcome_id", out _));
            return TestHttpHandler.Json(Fixture("end-prediction"));
        }));
        var result = (await Client(http).Predictions.EndPredictionAsync(new() { BroadcasterId = "1", Id = "p1", Status = status, WinningOutcomeId = status == "RESOLVED" ? "o1" : null })).Data.Single();
        Assert.Equal("o1", result.WinningOutcomeId);
        Assert.Equal(2147483649, result.Outcomes[0].TopPredictors![0].ChannelPointsWon);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothListEnumeratorsEncodeFiltersSnapshotIdsAndPreserveCursor(bool polls)
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(polls ? "/helix/polls" : "/helix/predictions", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1&id=p1&id=p2&first=5&after=" + (++step == 1 ? "a%2Bb" : "next"), request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(step == 1 ? Fixture(polls ? "get-polls" : "get-predictions") : "{\"data\":[],\"pagination\":{}}"));
        }));
        var ids = new List<string> { "p1", "p2" };
        var client = Client(http);
        var pollItems = client.Polls.EnumeratePollsAsync(new() { BroadcasterId = "1", Ids = ids, First = 5, After = "a+b" });
        var predictionItems = client.Predictions.EnumeratePredictionsAsync(new() { BroadcasterId = "1", Ids = ids, First = 5, After = "a+b" });
        ids.Clear();
        var count = 0;
        if (polls) await foreach (var _ in pollItems) count++;
        else await foreach (var _ in predictionItems) count++;
        Assert.Equal(1, count);
        Assert.Equal(2, step);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadOrManageScopesPermitReadsButOnlyManagePermitsWrites(bool manage)
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"data\":[]}")); }));
        var scopes = manage ? new[] { TwitchScopes.ChannelManagePolls, TwitchScopes.ChannelManagePredictions } : [TwitchScopes.ChannelReadPolls, TwitchScopes.ChannelReadPredictions];
        var client = Client(http, new("user", scopes: scopes, kind: TwitchTokenKind.User, userId: "1"));
        await client.Polls.GetPollsAsync(new() { BroadcasterId = "1" });
        await client.Predictions.GetPredictionsAsync(new() { BroadcasterId = "1" });
        foreach (var operation in Writes(client))
        {
            if (manage) await operation();
            else await Assert.ThrowsAsync<TwitchAuthorizationException>(operation);
        }
        Assert.Equal(manage ? 6 : 2, calls);
    }

    [Fact]
    public async Task AppWrongOwnerAndMissingScopeAreRejectedForAllSixEndpoints()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("No request expected")));
        foreach (var token in new AccessToken[]
        {
            new("app", kind: TwitchTokenKind.App), new("user", scopes: [], kind: TwitchTokenKind.User),
            new("other", scopes: [TwitchScopes.ChannelManagePolls, TwitchScopes.ChannelManagePredictions], kind: TwitchTokenKind.User, userId: "other")
        })
        {
            var client = Client(http, token);
            await Assert.ThrowsAsync<TwitchAuthorizationException>(() => client.Polls.GetPollsAsync(new() { BroadcasterId = "1" }));
            await Assert.ThrowsAsync<TwitchAuthorizationException>(() => client.Predictions.GetPredictionsAsync(new() { BroadcasterId = "1" }));
            foreach (var write in Writes(client)) await Assert.ThrowsAsync<TwitchAuthorizationException>(write);
        }
    }

    [Fact]
    public async Task ListLimitsDifferAndPredictionResolutionRequiresWinner()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"data\":[]}")); }));
        var client = Client(http);
        var ids = Enumerable.Range(1, 25).Select(i => i.ToString()).ToArray();
        await client.Polls.GetPollsAsync(new() { BroadcasterId = "1", Ids = ids[..20], First = 20 });
        await client.Predictions.GetPredictionsAsync(new() { BroadcasterId = "1", Ids = ids, First = 25 });
        await Assert.ThrowsAsync<ArgumentException>(() => client.Polls.GetPollsAsync(new() { BroadcasterId = "1", Ids = ids[..21] }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Predictions.GetPredictionsAsync(new() { BroadcasterId = "1", Ids = [.. ids, "extra"] }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.Polls.GetPollsAsync(new() { BroadcasterId = "1", First = 21 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.Predictions.GetPredictionsAsync(new() { BroadcasterId = "1", First = 26 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Polls.EndPollAsync(new() { BroadcasterId = "1", Id = "p1", Status = "COMPLETED" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Predictions.EndPredictionAsync(new() { BroadcasterId = "1", Id = "p1", Status = "resolved" }));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.Predictions.EndPredictionAsync(new() { BroadcasterId = "1", Id = "p1", Status = "RESOLVED" }));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CreationBoundsValidateChoicesTitlesWindowsAndPaidVoting()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json("{\"data\":[]}"))));
        var client = Client(http);
        PollChoiceRequest[] choices = [new() { Title = "A" }, new() { Title = "B" }];
        PredictionOutcomeRequest[] outcomes = [new() { Title = "A" }, new() { Title = "B" }];
        await Assert.ThrowsAsync<ArgumentException>(() => client.Polls.CreatePollAsync(new() { BroadcasterId = "1", Title = new('x', 61), Choices = choices, Duration = 15 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Polls.CreatePollAsync(new() { BroadcasterId = "1", Title = "Title", Choices = [new() { Title = "A" }], Duration = 15 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Polls.CreatePollAsync(new() { BroadcasterId = "1", Title = "Title", Choices = [new() { Title = new('x', 26) }, new() { Title = "B" }], Duration = 15 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.Polls.CreatePollAsync(new() { BroadcasterId = "1", Title = "Title", Choices = choices, Duration = 14 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.Polls.CreatePollAsync(new() { BroadcasterId = "1", Title = "Title", Choices = choices, Duration = 15, ChannelPointsVotingEnabled = true, ChannelPointsPerVote = 1000001 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Polls.CreatePollAsync(new() { BroadcasterId = "1", Title = "Title", Choices = choices, Duration = 15, ChannelPointsPerVote = 1 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Predictions.CreatePredictionAsync(new() { BroadcasterId = "1", Title = new('x', 46), Outcomes = outcomes, PredictionWindow = 30 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Predictions.CreatePredictionAsync(new() { BroadcasterId = "1", Title = "Title", Outcomes = Enumerable.Repeat(outcomes[0], 11).ToArray(), PredictionWindow = 30 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.Predictions.CreatePredictionAsync(new() { BroadcasterId = "1", Title = "Title", Outcomes = outcomes, PredictionWindow = 29 }));
        await client.Polls.CreatePollAsync(new() { BroadcasterId = "1", Title = string.Concat(Enumerable.Repeat("😀", 60)), Choices = Enumerable.Repeat(new PollChoiceRequest { Title = new('x', 25) }, 5).ToArray(), Duration = 1800, ChannelPointsVotingEnabled = true, ChannelPointsPerVote = 1000000 });
        await client.Predictions.CreatePredictionAsync(new() { BroadcasterId = "1", Title = string.Concat(Enumerable.Repeat("😀", 45)), Outcomes = outcomes, PredictionWindow = 30 });
    }

    [Fact]
    public async Task InvalidStateErrorsArePreservedAndMutationsAreNotRepeated()
    {
        var count = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { count++; return Task.FromResult(TestHttpHandler.Json("{\"error\":\"Bad Request\",\"message\":\"invalid current state\"}", HttpStatusCode.BadRequest)); }));
        var client = Client(http);
        Func<Task>[] calls = [() => client.Polls.GetPollsAsync(new() { BroadcasterId = "1" }), () => client.Predictions.GetPredictionsAsync(new() { BroadcasterId = "1" }), .. Writes(client)];
        foreach (var call in calls)
        {
            var error = await Assert.ThrowsAsync<TwitchApiException>(call);
            Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
            Assert.Equal("invalid current state", error.Message);
        }
        Assert.Equal(6, count);
    }

    private static Func<Task>[] Writes(HelixClient client) =>
    [
        () => client.Polls.CreatePollAsync(new() { BroadcasterId = "1", Title = "Title", Choices = [new() { Title = "A" }, new() { Title = "B" }], Duration = 15 }),
        () => client.Polls.EndPollAsync(new() { BroadcasterId = "1", Id = "p1", Status = "ARCHIVED" }),
        () => client.Predictions.CreatePredictionAsync(new() { BroadcasterId = "1", Title = "Title", Outcomes = [new() { Title = "A" }, new() { Title = "B" }], PredictionWindow = 30 }),
        () => client.Predictions.EndPredictionAsync(new() { BroadcasterId = "1", Id = "p1", Status = "CANCELED" })
    ];
}
