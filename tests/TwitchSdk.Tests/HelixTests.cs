using System.Text.Json;
using TwitchSdk.Core;
using TwitchSdk.Helix;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Tests;

public sealed class HelixTests
{
    [Fact]
    public void DeserializesUserAndOptionalEmailWithSourceGeneratedContext()
    {
        var page = JsonSerializer.Deserialize("""
            {"data":[{"id":"1","login":"tester","display_name":"Tester","type":"","broadcaster_type":"affiliate",
            "description":"about","profile_image_url":"https://example.org/a","offline_image_url":"","view_count":0,
            "created_at":"2020-01-01T00:00:00Z"}]}
            """, HelixJsonContext.Default.HelixPageTwitchUser)!;
        var user = Assert.Single(page.Data);
        Assert.Null(user.Email);
        Assert.Equal("Tester", user.DisplayName);
        Assert.Equal(2020, user.CreatedAt.Year);
    }

    [Fact]
    public async Task PaginationFollowsCursorEvenWhenIntermediatePageIsEmpty()
    {
        var cursors = new List<string?>();
        var results = new List<int>();
        await foreach (var item in HelixPagination.EnumerateAsync<int>((cursor, _) =>
        {
            cursors.Add(cursor);
            return Task.FromResult(cursor is null ? new HelixPage<int> { Pagination = new() { Cursor = "next" } } : new() { Data = [42] });
        })) results.Add(item);
        Assert.Equal(new string?[] { null, "next" }, cursors);
        Assert.Equal([42], results);
    }

    [Fact]
    public async Task PaginationRejectsCursorCycles()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in HelixPagination.EnumerateAsync<int>((_, _) => Task.FromResult(new HelixPage<int> { Pagination = new() { Cursor = "same" } }))) { }
        });
    }

    [Fact]
    public async Task StreamsSupportAllFiltersAndBothPaginationDirections()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal("?user_id=1&user_login=test&game_id=2&language=de&type=live&first=5&before=prev", request.RequestUri!.Query);
            return Task.FromResult(TestHttpHandler.Json("{\"data\":[],\"pagination\":{\"cursor\":\"next\"}}"));
        }));
        var client = new HelixClient(new(http, new StaticAccessTokenProvider(new("token")), new() { ClientId = "client" }));
        var page = await client.GetStreamsAsync(new() { UserIds = ["1"], UserLogins = ["test"], GameIds = ["2"], Languages = ["de"], Type = "live", First = 5, Before = "prev" });
        Assert.Equal("next", page.Pagination!.Cursor);
    }

    [Fact]
    public async Task ChatPreservesFalseOptionAndRejectsInvalidPinCombination()
    {
        var body = JsonSerializer.Serialize(new SendChatMessageRequest { BroadcasterId = "1", SenderId = "2", Message = "hi", ForSourceOnly = false }, HelixJsonContext.Default.SendChatMessageRequest);
        Assert.Contains("\"for_source_only\":false", body);
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException()));
        var client = new HelixClient(new(http, new StaticAccessTokenProvider(new("token")), new() { ClientId = "client" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.SendChatMessageAsync(new() { BroadcasterId = "1", SenderId = "2", Message = "hi", Pin = true, ForSourceOnly = false }));
    }

    [Fact]
    public async Task DeletesSubscriptionWithNoContentResponse()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("?id=subscription-1", request.RequestUri!.Query);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
        }));
        await new HelixClient(new(http, new StaticAccessTokenProvider(new("token")), new() { ClientId = "client" })).DeleteEventSubSubscriptionAsync("subscription-1");
    }
}
