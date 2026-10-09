using System.Net;
using System.Text.Json.Serialization.Metadata;
using TwitchSdk.Core;
using TwitchSdk.Helix;
using TwitchSdk.Helix.Clients;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Tests;

public sealed class EntitlementsTests
{
    private static EntitlementsClient Client(HttpClient http, AccessToken? token = null) => new HelixClient(new(http,
        new StaticAccessTokenProvider(token ?? new("token")), new() { ClientId = "client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 })).Entitlements;
    private static AccessToken AppToken => new("app", kind: TwitchTokenKind.App);
    private static AccessToken UserToken => new("user", scopes: [], kind: TwitchTokenKind.User, userId: "25009227");
    private static string Fixture(string id) => ContractAssertions.Fixture("helix-entitlements.json", id);

    public static IEnumerable<object[]> Contracts()
    {
        yield return ["get-drops-entitlements", HelixJsonContext.Default.HelixPageDropsEntitlement];
        yield return ["update-drops-entitlements", HelixJsonContext.Default.HelixPageDropsEntitlementUpdate];
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void ResponseContractsPreserveEveryDocumentedField(string id, JsonTypeInfo type)
        => ContractAssertions.Verify("helix-entitlements.json", id, type);

    [Fact]
    public async Task GetSendsEveryFilterAndReadsAllFields()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/entitlements/drops", request.RequestUri!.AbsolutePath);
            Assert.Equal("?id=e1&id=e2&user_id=25009227&game_id=33214&fulfillment_status=CLAIMED&after=a%2Bb&first=1000", request.RequestUri.Query);
            Assert.Null(request.Content);
            return Task.FromResult(TestHttpHandler.Json(Fixture("get-drops-entitlements")));
        }));
        var page = await Client(http, AppToken).GetDropsEntitlementsAsync(new()
        {
            Ids = ["e1", "e2"], UserId = "25009227", GameId = "33214", FulfillmentStatus = DropsFulfillmentStatuses.Claimed, After = "a+b", First = 1000
        });
        var first = page.Data[0];
        Assert.Equal("74c52265-e214-48a6-91b9-23b6014e8041", first.BenefitId);
        Assert.Equal(DateTimeOffset.Parse("2019-01-28T04:17:53.325Z"), first.Timestamp);
        Assert.Equal("CLAIMED", first.FulfillmentStatus);
        Assert.Equal("FULFILLED", page.Data[1].FulfillmentStatus);
        Assert.StartsWith("eyJiIjpudWxs", page.Pagination!.Cursor, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnfilteredRequestSendsNoQueryAndEnumeratorSnapshotsFilters()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(++step switch { 1 => "", 2 => "?id=e1&game_id=33214&after=initial&first=2", _ => "?id=e1&game_id=33214&after=next&first=2" }, request.RequestUri!.Query);
            return Task.FromResult(TestHttpHandler.Json(step switch
            {
                1 => "{\"data\":[],\"pagination\":{}}",
                2 => Fixture("get-drops-entitlements").Replace("eyJiIjpudWxsLCJhIjp7IkN1cnNvciI6ImV5SnBaQ0k2SW1aaU56Z3lOVGxsTFdaaU9ERXROR1F4WWkwNE16TXpMVE0wWVRBMlptWmpNalJqTUNJc0ltTmhJam9pTWpBeE9TMHdNUzB5T0ZRd05Eb3hOem8xTXk0ek1qVmFJbjA9In19", "next", StringComparison.Ordinal),
                _ => "{\"data\":[],\"pagination\":{}}"
            }));
        }));
        var client = Client(http);
        Assert.Empty((await client.GetDropsEntitlementsAsync()).Data);
        var ids = new List<string> { "e1" };
        var items = client.EnumerateDropsEntitlementsAsync(new() { Ids = ids, GameId = "33214", After = "initial", First = 2 });
        ids.Clear();
        var count = 0;
        await foreach (var _ in items) count++;
        Assert.Equal(2, count);
        Assert.Equal(3, step);
    }

    [Fact]
    public async Task UserTokensMayNotFilterByUserWhileAppTokensMay()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"data\":[]}")); }));
        var user = Client(http, UserToken);
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => user.GetDropsEntitlementsAsync(new() { UserId = "25009227" }));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => user.GetDropsEntitlementsAsync(new() { UserId = "25009227", GameId = "33214" }));
        await user.GetDropsEntitlementsAsync();
        await user.GetDropsEntitlementsAsync(new() { GameId = "33214", FulfillmentStatus = "FULFILLED" });
        await user.UpdateDropsEntitlementsAsync(new() { EntitlementIds = ["e1"], FulfillmentStatus = "FULFILLED" });
        var app = Client(http, AppToken);
        await app.GetDropsEntitlementsAsync();
        await app.GetDropsEntitlementsAsync(new() { UserId = "25009227" });
        await app.GetDropsEntitlementsAsync(new() { UserId = "25009227", GameId = "33214" });
        await app.GetDropsEntitlementsAsync(new() { GameId = "33214" });
        await app.UpdateDropsEntitlementsAsync(new() { EntitlementIds = ["e1"], FulfillmentStatus = "CLAIMED" });
        Assert.Equal(8, calls);
    }

    [Fact]
    public async Task UpdateSendsOnlySuppliedFieldsAndReturnsEveryStatusGroup()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("/helix/entitlements/drops", request.RequestUri!.AbsolutePath);
            Assert.Equal("", request.RequestUri.Query);
            Assert.Equal(++step switch
            {
                1 => "{\"entitlement_ids\":[\"fb78259e-fb81-4d1b-8333-34a06ffc24c0\",\"862750a5-265e-4ab6-9f0a-c64df3d54dd0\"],\"fulfillment_status\":\"FULFILLED\"}",
                _ => "{}"
            }, await request.Content!.ReadAsStringAsync(ct));
            return TestHttpHandler.Json(Fixture("update-drops-entitlements"));
        }));
        var client = Client(http);
        var result = await client.UpdateDropsEntitlementsAsync(new()
        {
            EntitlementIds = ["fb78259e-fb81-4d1b-8333-34a06ffc24c0", "862750a5-265e-4ab6-9f0a-c64df3d54dd0"], FulfillmentStatus = DropsFulfillmentStatuses.Fulfilled
        });
        Assert.Equal(
            new[] { DropsEntitlementUpdateStatuses.Success, DropsEntitlementUpdateStatuses.InvalidId, DropsEntitlementUpdateStatuses.NotFound, DropsEntitlementUpdateStatuses.Unauthorized, DropsEntitlementUpdateStatuses.UpdateFailed },
            result.Data.Select(group => group.Status));
        Assert.Equal(2, result.Data[0].Ids.Count);
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateDropsEntitlementsAsync(new() { EntitlementIds = [] }));
        await client.UpdateDropsEntitlementsAsync(new());
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task FiltersIdsStatusesAndPageSizeAreValidatedLocally()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"data\":[]}")); }));
        var client = Client(http);
        var ids = Enumerable.Range(1, 100).Select(i => "e" + i).ToArray();
        await client.GetDropsEntitlementsAsync(new() { Ids = ids, First = 1 });
        await client.UpdateDropsEntitlementsAsync(new() { EntitlementIds = ids });
        Func<Task>[] invalid =
        [
            () => client.GetDropsEntitlementsAsync(new() { Ids = [.. ids, "extra"] }),
            () => client.GetDropsEntitlementsAsync(new() { Ids = [" "] }),
            () => client.GetDropsEntitlementsAsync(new() { FulfillmentStatus = "claimed" }),
            () => client.GetDropsEntitlementsAsync(new() { UserId = "" }),
            () => client.GetDropsEntitlementsAsync(new() { GameId = " " }),
            () => client.UpdateDropsEntitlementsAsync(new() { EntitlementIds = [.. ids, "extra"] }),
            () => client.UpdateDropsEntitlementsAsync(new() { EntitlementIds = [""] }),
            () => client.UpdateDropsEntitlementsAsync(new() { FulfillmentStatus = "GRANTED" }),
            () => client.UpdateDropsEntitlementsAsync(null!)
        ];
        foreach (var call in invalid) await Assert.ThrowsAnyAsync<ArgumentException>(call);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetDropsEntitlementsAsync(new() { First = 0 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetDropsEntitlementsAsync(new() { First = 1001 }));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task OrganizationAndOwnershipErrorsArePreserved(HttpStatusCode status)
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) =>
        {
            calls++;
            var response = TestHttpHandler.Json($"{{\"error\":\"Error\",\"status\":{(int)status},\"message\":\"The organization must own the game\"}}", status);
            response.Headers.Add("Twitch-Trace-Id", "trace-drops");
            return Task.FromResult(response);
        }));
        var client = Client(http, AppToken);
        foreach (var call in new Func<Task>[] { () => client.GetDropsEntitlementsAsync(new() { GameId = "33214" }), () => client.UpdateDropsEntitlementsAsync(new() { EntitlementIds = ["e1"], FulfillmentStatus = "FULFILLED" }) })
        {
            var error = await Assert.ThrowsAsync<TwitchApiException>(call);
            Assert.Equal(status, error.StatusCode);
            Assert.Equal("The organization must own the game", error.Message);
            Assert.Equal("trace-drops", error.RequestId);
        }
        Assert.Equal(2, calls);
    }
}
