using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TwitchSdk.Core;
using TwitchSdk.Helix;
using TwitchSdk.Helix.Clients;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Tests;

public sealed class ConduitsTests
{
    private static ConduitsClient Client(HttpClient http, AccessToken? token = null) => new HelixClient(new(http,
        new StaticAccessTokenProvider(token ?? new("app", kind: TwitchTokenKind.App)), new() { ClientId = "client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 })).Conduits;
    private static string Fixture(string id) => ContractAssertions.Fixture("helix-conduits.json", id);
    private static ConduitShardUpdate Socket(string id) => new() { Id = id, Transport = new() { Method = "websocket", SessionId = "session1" } };

    public static IEnumerable<object[]> Contracts()
    {
        yield return ["get-conduits", HelixJsonContext.Default.HelixPageConduit];
        yield return ["create-conduits", HelixJsonContext.Default.HelixPageConduit];
        yield return ["update-conduits", HelixJsonContext.Default.HelixPageConduit];
        yield return ["get-conduit-shards", HelixJsonContext.Default.HelixPageConduitShard];
        yield return ["update-conduit-shards", HelixJsonContext.Default.UpdateConduitShardsResponse];
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void ResponseContractsPreserveEveryDocumentedField(string id, JsonTypeInfo type)
        => ContractAssertions.Verify("helix-conduits.json", id, type);

    [Fact]
    public async Task ConduitCrudUsesCorrectMethodsBodiesAndQuery()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal("/helix/eventsub/conduits", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer app", request.Headers.Authorization!.ToString());
            Assert.Equal("client", Assert.Single(request.Headers.GetValues("Client-Id")));
            switch (++step)
            {
                case 1:
                    Assert.Equal(HttpMethod.Get, request.Method);
                    Assert.Equal("", request.RequestUri.Query);
                    Assert.Null(request.Content);
                    return TestHttpHandler.Json(Fixture("get-conduits"));
                case 2:
                    Assert.Equal(HttpMethod.Post, request.Method);
                    Assert.Equal("", request.RequestUri.Query);
                    Assert.Equal("{\"shard_count\":3}", await request.Content!.ReadAsStringAsync(ct));
                    return TestHttpHandler.Json(Fixture("create-conduits"));
                case 3:
                    Assert.Equal(HttpMethod.Patch, request.Method);
                    Assert.Equal("", request.RequestUri.Query);
                    Assert.Equal("{\"id\":\"c1\",\"shard_count\":2}", await request.Content!.ReadAsStringAsync(ct));
                    return TestHttpHandler.Json(Fixture("update-conduits"));
                default:
                    Assert.Equal(HttpMethod.Delete, request.Method);
                    Assert.Equal("?id=c%2B1", request.RequestUri.Query);
                    Assert.Null(request.Content);
                    return new(HttpStatusCode.NoContent);
            }
        }));
        var client = Client(http);
        Assert.Equal("c1", Assert.Single((await client.GetConduitsAsync()).Data).Id);
        Assert.Equal(3, Assert.Single((await client.CreateConduitAsync(new() { ShardCount = 3 })).Data).ShardCount);
        Assert.Equal(2, Assert.Single((await client.UpdateConduitAsync(new() { Id = "c1", ShardCount = 2 })).Data).ShardCount);
        await client.DeleteConduitAsync("c+1");
        Assert.Equal(4, step);
    }

    [Fact]
    public async Task ShardPaginationKeepsFiltersAndConnectionMetadata()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/eventsub/conduits/shards", request.RequestUri!.AbsolutePath);
            Assert.Equal("?conduit_id=c%2B1&status=future_status&after=" + (++step == 1 ? "initial" : "next"), request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(step == 1 ? Fixture("get-conduit-shards") : "{\"data\":[],\"pagination\":{}}"));
        }));
        var results = new List<ConduitShard>();
        await foreach (var shard in Client(http).EnumerateConduitShardsAsync(new() { ConduitId = "c+1", Status = "future_status", After = "initial" })) results.Add(shard);
        Assert.Equal(3, results.Count);
        Assert.Equal(2, step);
        Assert.Equal("https://example.org/events", results[0].Transport.Callback);
        Assert.NotNull(results[1].Transport.DisconnectedAt);
        Assert.Null(results[2].Transport.DisconnectedAt);
    }

    [Fact]
    public async Task AcceptedShardUpdatePreservesPartialFailuresWithoutRetrying()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            calls++;
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("/helix/eventsub/conduits/shards", request.RequestUri!.AbsolutePath);
            Assert.Equal("", request.RequestUri.Query);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("c1", json.RootElement.GetProperty("conduit_id").GetString());
            var shards = json.RootElement.GetProperty("shards");
            Assert.Equal(3, shards.GetArrayLength());
            Assert.Equal("https://example.org/events", shards[0].GetProperty("transport").GetProperty("callback").GetString());
            Assert.Equal("secret-value", shards[0].GetProperty("transport").GetProperty("secret").GetString());
            Assert.False(shards[0].GetProperty("transport").TryGetProperty("session_id", out _));
            Assert.Equal("session1", shards[1].GetProperty("transport").GetProperty("session_id").GetString());
            Assert.False(shards[1].GetProperty("transport").TryGetProperty("secret", out _));
            return TestHttpHandler.Json(Fixture("update-conduit-shards"), HttpStatusCode.Accepted);
        }));
        var response = await Client(http).UpdateConduitShardsAsync(new()
        {
            ConduitId = "c1", Shards = [new() { Id = "0", Transport = new() { Method = "webhook", Callback = "https://example.org/events", Secret = "secret-value" } }, Socket("1"), Socket("3")]
        });
        Assert.Equal(2, response.Data.Count);
        var error = Assert.Single(response.Errors);
        Assert.Equal("3", error.Id);
        Assert.Equal("invalid_parameter", error.Code);
        Assert.Contains("outside", error.Message);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task OptionalTransportMethodRemainsOmittedAndAllFailedResultIsReturned()
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal("{\"conduit_id\":\"c1\",\"shards\":[{\"id\":\"0\",\"transport\":{}}]}", await request.Content!.ReadAsStringAsync(ct));
            return TestHttpHandler.Json("{\"data\":[],\"errors\":[{\"id\":\"0\",\"code\":\"invalid_parameter\",\"message\":\"No connected transport\"}]}", HttpStatusCode.Accepted);
        }));
        var response = await Client(http).UpdateConduitShardsAsync(new() { ConduitId = "c1", Shards = [new() { Id = "0", Transport = new() }] });
        Assert.Empty(response.Data);
        Assert.Single(response.Errors);
    }

    [Fact]
    public async Task EveryConduitOperationRequiresAppTokenIncludingSocketShards()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("No request expected")));
        foreach (var operation in Operations(Client(http, new("user", kind: TwitchTokenKind.User)))) await Assert.ThrowsAsync<TwitchAuthorizationException>(operation);
    }

    [Fact]
    public async Task CountBoundsAndTransportValidationAreAppliedBeforeSending()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"data\":[],\"errors\":[]}", HttpStatusCode.Accepted)); }));
        var client = Client(http);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.CreateConduitAsync(new() { ShardCount = 0 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.UpdateConduitAsync(new() { Id = "c1", ShardCount = -1 }));
        foreach (var count in new[] { 0, 101 })
            await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateConduitShardsAsync(new() { ConduitId = "c1", Shards = Enumerable.Range(0, count).Select(i => Socket(i.ToString())).ToArray() }));
        ConduitShardTransportRequest[] invalid =
        [
            new() { Method = "conduit" }, new() { Method = "websocket" },
            new() { Method = "websocket", SessionId = "s", Secret = "secret-value" },
            new() { Method = "webhook", Callback = "http://example.org", Secret = "secret-value" },
            new() { Method = "webhook", Callback = "https://example.org:8080", Secret = "secret-value" },
            new() { Method = "webhook", Callback = "https://example.org", Secret = "short" },
            new() { Method = "webhook", Callback = "https://example.org", Secret = "secret-valué" },
            new() { Method = "webhook", Callback = "https://example.org", Secret = "secret-value", SessionId = "s" }
        ];
        foreach (var transport in invalid)
            await Assert.ThrowsAnyAsync<ArgumentException>(() => client.UpdateConduitShardsAsync(new() { ConduitId = "c1", Shards = [new() { Id = "0", Transport = transport }] }));
        Assert.Equal(0, calls);
        await client.UpdateConduitShardsAsync(new() { ConduitId = "c1", Shards = Enumerable.Range(0, 100).Select(i => Socket(i.ToString())).ToArray() });
        Assert.Equal(1, calls);
        Assert.DoesNotContain("secret-value", new ConduitShardTransportRequest { Secret = "secret-value" }.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task HttpErrorsRemainDistinctFromPerShardErrors(HttpStatusCode status)
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json("{\"error\":\"Conduit failure\",\"message\":\"Conduit unavailable\"}", status))));
        foreach (var operation in Operations(Client(http)))
        {
            var error = await Assert.ThrowsAsync<TwitchApiException>(operation);
            Assert.Equal(status, error.StatusCode);
            Assert.Equal("Conduit unavailable", error.Message);
        }
    }

    private static Func<Task>[] Operations(ConduitsClient client) =>
    [
        () => client.GetConduitsAsync(), () => client.CreateConduitAsync(new() { ShardCount = 1 }),
        () => client.UpdateConduitAsync(new() { Id = "c1", ShardCount = 1 }), () => client.DeleteConduitAsync("c1"),
        () => client.GetConduitShardsAsync(new() { ConduitId = "c1" }),
        () => client.UpdateConduitShardsAsync(new() { ConduitId = "c1", Shards = [Socket("0")] })
    ];
}
