using System.Net;
using System.Text.Json;
using TwitchDock.Core;
using TwitchDock.Helix;
using TwitchDock.Helix.Clients;

namespace TwitchDock.Tests;

public sealed class HypeTrainTests
{
    private static HypeTrainClient Client(HttpClient http, AccessToken? token = null) => new HelixClient(new(http,
        new StaticAccessTokenProvider(token ?? new("user", scopes: [TwitchScopes.ChannelReadHypeTrain], kind: TwitchTokenKind.User, userId: "1")),
        new() { ClientId = "client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 })).HypeTrain;

    [Fact]
    public void ResponseContractPreservesEveryDocumentedField()
        => ContractAssertions.Verify("helix-hype-train.json", "get-hype-train-status", HelixJsonContext.Default.HelixPageHypeTrainStatus);

    [Fact]
    public async Task StatusPreservesSharedTrainContributionsRecordsAndLargeTotals()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/hypetrain/status", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1", request.RequestUri.Query);
            Assert.Null(request.Content);
            return Task.FromResult(TestHttpHandler.Json(ContractAssertions.Fixture("helix-hype-train.json", "get-hype-train-status")));
        }));
        var status = Assert.Single((await Client(http).GetHypeTrainStatusAsync("1")).Data);
        Assert.True(status.Current!.IsSharedTrain);
        Assert.Equal("4", Assert.Single(status.Current.SharedTrainParticipants!).BroadcasterUserId);
        Assert.Equal(4000000000, status.Current.Total);
        Assert.Equal(3000000000, status.Current.TopContributions[0].Total);
        Assert.Equal("other", status.Current.TopContributions[1].Type);
        Assert.Equal(11000000000, status.SharedAllTimeHigh!.Total);
        Assert.Equal(25, status.AllTimeHigh!.Level);
    }

    [Fact]
    public async Task ChannelWithoutHistoryReturnsNullTrainAndRecords()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json("{\"data\":[{\"current\":null,\"all_time_high\":null,\"shared_all_time_high\":null}]}"))));
        var status = Assert.Single((await Client(http).GetHypeTrainStatusAsync("1")).Data);
        Assert.Null(status.Current);
        Assert.Null(status.AllTimeHigh);
        Assert.Null(status.SharedAllTimeHigh);
    }

    [Fact]
    public void UnsharedTrainAcceptsNullParticipantsAndFutureType()
    {
        var status = JsonSerializer.Deserialize("""{"data":[{"current":{"id":"t1","broadcaster_user_id":"1","broadcaster_user_login":"channel","broadcaster_user_name":"Channel","type":"future_type","is_shared_train":false,"shared_train_participants":null}}]}""", HelixJsonContext.Default.HelixPageHypeTrainStatus)!.Data.Single();
        Assert.False(status.Current!.IsSharedTrain);
        Assert.Null(status.Current.SharedTrainParticipants);
        Assert.Equal("future_type", status.Current.Type);
    }

    [Fact]
    public async Task RequiresBroadcastersUserTokenAndHypeTrainScope()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("No request expected")));
        foreach (var token in new AccessToken[] { new("app", kind: TwitchTokenKind.App), new("user", scopes: [], kind: TwitchTokenKind.User), new("user", scopes: [TwitchScopes.ChannelReadHypeTrain], kind: TwitchTokenKind.User, userId: "2") })
            await Assert.ThrowsAsync<TwitchAuthorizationException>(() => Client(http, token).GetHypeTrainStatusAsync("1"));
        await Assert.ThrowsAsync<ArgumentException>(() => Client(http).GetHypeTrainStatusAsync(" "));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ErrorsRemainStructured(HttpStatusCode status)
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json("{\"error\":\"Hype Train error\",\"message\":\"Request rejected\"}", status))));
        var error = await Assert.ThrowsAsync<TwitchApiException>(() => Client(http).GetHypeTrainStatusAsync("1"));
        Assert.Equal(status, error.StatusCode);
        Assert.Equal("Request rejected", error.Message);
    }
}
