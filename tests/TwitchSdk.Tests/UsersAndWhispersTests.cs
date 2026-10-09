using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TwitchSdk.Core;
using TwitchSdk.Helix;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Tests;

public sealed class UsersAndWhispersTests
{
    private static HelixClient Client(HttpClient http, AccessToken? token = null) => new(new(http,
        new StaticAccessTokenProvider(token ?? new("token")), new() { ClientId = "client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 }));
    private static string Fixture(string id) => ContractAssertions.Fixture("helix-users.json", id);

    public static IEnumerable<object[]> Contracts()
    {
        yield return ["update-user", HelixJsonContext.Default.HelixPageTwitchUser];
        yield return ["get-user-block-list", HelixJsonContext.Default.HelixPageBlockedUser];
        yield return ["get-user-extensions", HelixJsonContext.Default.HelixPageInstalledUserExtension];
        yield return ["get-user-active-extensions", HelixJsonContext.Default.UserActiveExtensionsResponse];
        yield return ["update-user-extensions", HelixJsonContext.Default.UserActiveExtensionsResponse];
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void ResponseContractsPreserveEveryDocumentedField(string id, JsonTypeInfo type)
        => ContractAssertions.Verify("helix-users.json", id, type);

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "?description=")]
    [InlineData("A + B & C", "?description=A%20%2B%20B%20%26%20C")]
    public async Task ProfileUpdateDistinguishesOmittedAndClearedDescription(string? description, string query)
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("/helix/users", request.RequestUri!.AbsolutePath);
            Assert.Equal(query, request.RequestUri.Query);
            Assert.Null(request.Content);
            return Task.FromResult(TestHttpHandler.Json(Fixture("update-user")));
        }));
        var user = (await Client(http, new("user", scopes: [TwitchScopes.UserEdit], kind: TwitchTokenKind.User)).Users.UpdateUserAsync(new() { Description = description })).Data.Single();
        Assert.Equal("Updated", user.Description);
        Assert.Equal("example@example.org", user.Email);
    }

    [Fact]
    public async Task ProfileDescriptionUsesCodePointLimit()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json(Fixture("update-user"))); }));
        var client = Client(http).Users;
        await client.UpdateUserAsync(new() { Description = string.Concat(Enumerable.Repeat("😀", 300)) });
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateUserAsync(new() { Description = new('x', 301) }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task BlockingAndUnblockingUseQueriesAndNoContentResponses()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(++step == 1 ? HttpMethod.Put : HttpMethod.Delete, request.Method);
            Assert.Equal("/helix/users/blocks", request.RequestUri!.AbsolutePath);
            Assert.Equal(step == 1 ? "?target_user_id=2&source_context=whisper&reason=spam" : "?target_user_id=2", request.RequestUri.Query);
            Assert.Null(request.Content);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }));
        var client = Client(http, new("user", scopes: [TwitchScopes.UserManageBlockedUsers], kind: TwitchTokenKind.User)).Users;
        await client.BlockUserAsync(new() { TargetUserId = "2", SourceContext = "whisper", Reason = "spam" });
        await client.UnblockUserAsync("2");
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task BlockListFollowsCursorAndRetainsBroadcasterAndPageSize()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/users/blocks", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1&first=100&after=" + (++step == 1 ? "initial" : "next"), request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(step == 1 ? Fixture("get-user-block-list") : "{\"data\":[],\"pagination\":{}}"));
        }));
        var results = new List<BlockedUser>();
        await foreach (var item in Client(http).Users.EnumerateUserBlockListAsync(new() { BroadcasterId = "1", First = 100, After = "initial" })) results.Add(item);
        Assert.Equal("Blocked", Assert.Single(results).DisplayName);
        Assert.Equal(2, step);
    }

    [Theory]
    [InlineData(TwitchScopes.UserReadBroadcast)]
    [InlineData(TwitchScopes.UserEditBroadcast)]
    public async Task InstalledExtensionsAcceptEitherUserScope(string scope)
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/users/extensions/list", request.RequestUri!.AbsolutePath);
            Assert.Equal("", request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture("get-user-extensions")));
        }));
        var extension = (await Client(http, new("user", scopes: [scope], kind: TwitchTokenKind.User)).Users.GetUserExtensionsAsync()).Data.Single();
        Assert.True(extension.CanActivate);
        Assert.Contains("mobile", extension.Type);
    }

    [Fact]
    public async Task ActiveExtensionsRequireExplicitUserForAppTokenAndPreserveInactiveSlots()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/users/extensions", request.RequestUri!.AbsolutePath);
            Assert.Equal(++step == 1 ? "?user_id=1" : "", request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture("get-user-active-extensions")));
        }));
        var app = Client(http, new("app", kind: TwitchTokenKind.App)).Users;
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => app.GetUserActiveExtensionsAsync());
        var active = await app.GetUserActiveExtensionsAsync("1");
        Assert.True(active.Data.Panel["1"].Active);
        Assert.False(active.Data.Component["2"].Active);
        Assert.Null(active.Data.Component["2"].Id);
        Assert.Null(active.Data.Component["2"].X);
        Assert.Equal(0, active.Data.Component["1"].X);
        await Client(http, new("user", scopes: [], kind: TwitchTokenKind.User)).Users.GetUserActiveExtensionsAsync();
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task ExtensionUpdateSendsTypedSlotMapsWithoutResponseOnlyNames()
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("/helix/users/extensions", request.RequestUri!.AbsolutePath);
            Assert.Equal("", request.RequestUri.Query);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var data = json.RootElement.GetProperty("data");
            Assert.Equal(3, data.EnumerateObject().Count());
            Assert.Equal("panel1", data.GetProperty("panel").GetProperty("1").GetProperty("id").GetString());
            Assert.Equal("1.0.0", data.GetProperty("overlay").GetProperty("1").GetProperty("version").GetString());
            var component = data.GetProperty("component");
            Assert.Equal(0, component.GetProperty("1").GetProperty("x").GetInt32());
            Assert.Equal(10, component.GetProperty("1").GetProperty("y").GetInt32());
            Assert.False(component.GetProperty("2").GetProperty("active").GetBoolean());
            Assert.Single(component.GetProperty("2").EnumerateObject());
            Assert.False(component.GetProperty("1").TryGetProperty("name", out _));
            return TestHttpHandler.Json(Fixture("update-user-extensions"));
        }));
        var client = Client(http, new("user", scopes: [TwitchScopes.UserEditBroadcast], kind: TwitchTokenKind.User)).Users;
        var response = await client.UpdateUserExtensionsAsync(new()
        {
            Data = new()
            {
                Panel = new Dictionary<string, UserExtensionActivation> { ["1"] = new() { Active = true, Id = "panel1", Version = "1.0.0" } },
                Overlay = new Dictionary<string, UserExtensionActivation> { ["1"] = new() { Active = true, Id = "overlay1", Version = "1.0.0" } },
                Component = new Dictionary<string, UserComponentExtensionActivation>
                {
                    ["1"] = new() { Active = true, Id = "component1", Version = "1.0.0", X = 0, Y = 10 }, ["2"] = new() { Active = false }
                }
            }
        });
        Assert.Equal("Panel", response.Data.Panel["1"].Name);
    }

    [Fact]
    public async Task ExtensionUpdateCanSelectOnlyOneTypeAndDeactivateOneSlot()
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal("{\"data\":{\"panel\":{\"2\":{\"active\":false}}}}", await request.Content!.ReadAsStringAsync(ct));
            return TestHttpHandler.Json(Fixture("update-user-extensions"));
        }));
        await Client(http).Users.UpdateUserExtensionsAsync(new() { Data = new() { Panel = new Dictionary<string, UserExtensionActivation> { ["2"] = new() { Active = false } } } });
    }

    [Fact]
    public async Task UserOperationsRequireTheirSpecificScopesAndBlockListOwner()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("No request expected")));
        foreach (var token in new AccessToken[] { new("app", kind: TwitchTokenKind.App), new("user", scopes: [], kind: TwitchTokenKind.User) })
        {
            var client = Client(http, token);
            foreach (var operation in PrivateOperations(client)) await Assert.ThrowsAsync<TwitchAuthorizationException>(operation);
        }
        var reader = Client(http, new("user", scopes: [TwitchScopes.UserReadBroadcast, TwitchScopes.UserReadBlockedUsers], kind: TwitchTokenKind.User, userId: "other"));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => reader.Users.GetUserBlockListAsync(new() { BroadcasterId = "1" }));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => reader.Users.UpdateUserExtensionsAsync(new() { Data = new() }));
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => reader.Users.UnblockUserAsync("2"));
    }

    [Fact]
    public async Task BlockAndExtensionValidationRunsBeforeSending()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("No request expected")));
        var client = Client(http).Users;
        await Assert.ThrowsAsync<ArgumentException>(() => client.BlockUserAsync(new() { TargetUserId = "2", SourceContext = "email" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.BlockUserAsync(new() { TargetUserId = "2", Reason = "invalid" }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetUserBlockListAsync(new() { BroadcasterId = "1", First = 101 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateUserExtensionsAsync(new() { Data = new() { Panel = new Dictionary<string, UserExtensionActivation> { ["0"] = new() { Active = false } } } }));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.UpdateUserExtensionsAsync(new() { Data = new() { Panel = new Dictionary<string, UserExtensionActivation> { ["1"] = new() { Active = true } } } }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateUserExtensionsAsync(new() { Data = new() { Component = new Dictionary<string, UserComponentExtensionActivation> { ["1"] = new() { Active = true, Id = "e", Version = "1.0.0" } } } }));
    }

    [Fact]
    public async Task WhisperSeparatesSenderRecipientFromMessageAndDoesNotGuessServerTruncation()
    {
        var message = new string('x', 10001);
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/helix/whispers", request.RequestUri!.AbsolutePath);
            Assert.Equal("?from_user_id=1&to_user_id=2", request.RequestUri.Query);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Single(json.RootElement.EnumerateObject());
            Assert.Equal(message, json.RootElement.GetProperty("message").GetString());
            return new(HttpStatusCode.NoContent);
        }));
        await Client(http, new("user", scopes: [TwitchScopes.UserManageWhispers], kind: TwitchTokenKind.User, userId: "1")).Whispers.SendWhisperAsync(new() { FromUserId = "1", ToUserId = "2", Message = message });
    }

    [Fact]
    public async Task WhisperValidatesSenderIdentityNonemptyMessageAndDifferentRecipient()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("No request expected")));
        var client = Client(http, new("user", scopes: [TwitchScopes.UserManageWhispers], kind: TwitchTokenKind.User, userId: "other")).Whispers;
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => client.SendWhisperAsync(new() { FromUserId = "1", ToUserId = "2", Message = "Hi" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.SendWhisperAsync(new() { FromUserId = "1", ToUserId = "1", Message = "Hi" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.SendWhisperAsync(new() { FromUserId = "1", ToUserId = "2", Message = "" }));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task AllNewEndpointsPreserveServerErrors(HttpStatusCode status)
    {
        var count = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { count++; return Task.FromResult(TestHttpHandler.Json("{\"error\":\"Request failed\",\"message\":\"Twitch policy or state rejected request\"}", status)); }));
        var client = Client(http);
        Func<Task>[] operations = [.. PrivateOperations(client), () => client.Users.GetUserActiveExtensionsAsync("1")];
        foreach (var operation in operations)
        {
            var error = await Assert.ThrowsAsync<TwitchApiException>(operation);
            Assert.Equal(status, error.StatusCode);
            Assert.Equal("Twitch policy or state rejected request", error.Message);
        }
        Assert.Equal(8, count);
    }

    private static Func<Task>[] PrivateOperations(HelixClient client) =>
    [
        () => client.Users.UpdateUserAsync(new() { Description = "About" }),
        () => client.Users.GetUserBlockListAsync(new() { BroadcasterId = "1" }),
        () => client.Users.BlockUserAsync(new() { TargetUserId = "2" }),
        () => client.Users.UnblockUserAsync("2"),
        () => client.Users.GetUserExtensionsAsync(),
        () => client.Users.UpdateUserExtensionsAsync(new() { Data = new() }),
        () => client.Whispers.SendWhisperAsync(new() { FromUserId = "1", ToUserId = "2", Message = "Hi" })
    ];
}
