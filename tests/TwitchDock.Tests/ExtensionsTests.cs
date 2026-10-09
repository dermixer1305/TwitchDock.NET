using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TwitchDock.Core;
using TwitchDock.Helix;
using TwitchDock.Helix.Clients;
using TwitchDock.Helix.Extensions;
using TwitchDock.Helix.Models;

namespace TwitchDock.Tests;

public sealed class ExtensionsTests
{
    private const string ExtensionClientId = "ext-client";
    private const string OwnerId = "27419012";
    private const string SecretBase64 = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
    // Computed independently (Python hmac/hashlib) for exp = 2026-10-09T12:03:00Z; its standard base64 signature contains '+', '/' and '='.
    private const string GoldenToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjE3OTE1NDczODAsInVzZXJfaWQiOiIyNzQxOTAxMiIsInJvbGUiOiJleHRlcm5hbCJ9.XPEuK_57mmrXdWtmmLWVxGZglEeyVLI6NGTIfS0g7-A";
    private static readonly ExtensionSecret Secret = ExtensionSecret.FromBase64(SecretBase64);

    private static ExtensionsClient JwtClient(HttpClient http, TimeProvider? time = null) => new HelixClient(new(http,
        new ExtensionJwtTokenProvider(ExtensionClientId, OwnerId, Secret, timeProvider: time ?? new ManualTimeProvider()),
        new() { ClientId = ExtensionClientId, MaxTransientRetries = 0, MaxRateLimitRetries = 0 })).Extensions;
    private static ExtensionsClient OAuthClient(HttpClient http, AccessToken? token = null) => new HelixClient(new(http,
        new StaticAccessTokenProvider(token ?? new("token")), new() { ClientId = "client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 })).Extensions;
    private static AccessToken AppToken => new("app", kind: TwitchTokenKind.App);
    private static AccessToken UserToken => new("user", scopes: [], kind: TwitchTokenKind.User, userId: "1");
    private static string Fixture(string id) => ContractAssertions.Fixture("helix-extensions.json", id);
    private static string Payload(string extra = "", long exp = 1791547380) => $"{{\"exp\":{exp},\"user_id\":\"{OwnerId}\",\"role\":\"external\"{extra}}}";

    public static IEnumerable<object[]> Contracts()
    {
        yield return ["get-extension-configuration-segment", HelixJsonContext.Default.HelixPageExtensionConfigurationSegment];
        yield return ["get-extension-live-channels", HelixJsonContext.Default.ExtensionLiveChannelsResponse];
        yield return ["get-extension-secrets", HelixJsonContext.Default.HelixPageExtensionSecretSet];
        yield return ["create-extension-secret", HelixJsonContext.Default.HelixPageExtensionSecretSet];
        yield return ["get-extensions", HelixJsonContext.Default.HelixPageTwitchExtension];
        yield return ["get-released-extensions", HelixJsonContext.Default.HelixPageTwitchExtension];
        yield return ["get-extension-bits-products", HelixJsonContext.Default.HelixPageExtensionBitsProduct];
        yield return ["update-extension-bits-product", HelixJsonContext.Default.HelixPageExtensionBitsProduct];
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void ResponseContractsPreserveEveryDocumentedField(string id, JsonTypeInfo type)
        => ContractAssertions.Verify("helix-extensions.json", id, type);

    [Fact]
    public void ExternalJwtMatchesIndependentlyComputedHs256Token()
    {
        var token = ExtensionJwt.CreateExternal(Secret, OwnerId, timeProvider: new ManualTimeProvider());
        Assert.Equal(GoldenToken, token);
        Assert.Equal(Payload(), PayloadJson(token));
        VerifySignature(token, SecretBase64);
    }

    [Fact]
    public void JwtHelpersAddChannelAndPubSubClaimsAndHonorLifetime()
    {
        var time = new ManualTimeProvider();
        var chat = ExtensionJwt.CreateExternal(Secret, OwnerId, channelId: "1", lifetime: TimeSpan.FromSeconds(30), timeProvider: time);
        Assert.Equal(Payload(",\"channel_id\":\"1\"", 1791547230), PayloadJson(chat));
        var pubsub = ExtensionJwt.CreateForPubSub(Secret, OwnerId, "1", ["broadcast", "whisper-9", "broadcast"], timeProvider: time);
        Assert.Equal(Payload(",\"channel_id\":\"1\",\"pubsub_perms\":{\"send\":[\"broadcast\",\"whisper-9\"]}"), PayloadJson(pubsub));
        var global = ExtensionJwt.CreateForPubSub(Secret, OwnerId, ExtensionJwt.AllChannels, [ExtensionPubSubTargets.Global], TimeSpan.FromHours(1), time);
        Assert.Equal(Payload(",\"channel_id\":\"all\",\"pubsub_perms\":{\"send\":[\"global\"]}", 1791550800), PayloadJson(global));
        foreach (var token in new[] { chat, pubsub, global }) VerifySignature(token, SecretBase64);

        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var system = JsonDocument.Parse(PayloadJson(ExtensionJwt.CreateExternal(Secret, OwnerId)));
        Assert.InRange(system.RootElement.GetProperty("exp").GetInt64(), before + 179, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 180);
    }

    [Fact]
    public void JwtInputsAreValidated()
    {
        Assert.Throws<ArgumentNullException>(() => ExtensionJwt.CreateExternal(null!, OwnerId));
        Assert.Throws<ArgumentException>(() => ExtensionJwt.CreateExternal(Secret, " "));
        Assert.Throws<ArgumentException>(() => ExtensionJwt.CreateExternal(Secret, OwnerId, channelId: ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExtensionJwt.CreateExternal(Secret, OwnerId, lifetime: TimeSpan.FromSeconds(29)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExtensionJwt.CreateExternal(Secret, OwnerId, lifetime: TimeSpan.FromHours(1) + TimeSpan.FromTicks(1)));
        Assert.NotEmpty(ExtensionJwt.CreateExternal(Secret, OwnerId, lifetime: ExtensionJwt.MinimumLifetime));
        Assert.Throws<ArgumentNullException>(() => ExtensionJwt.CreateForPubSub(Secret, OwnerId, "1", null!));
        Assert.Throws<ArgumentException>(() => ExtensionJwt.CreateForPubSub(Secret, OwnerId, " ", ["broadcast"]));
        foreach (var (channel, targets) in new (string, string[])[]
        {
            ("1", []), ("1", ["global"]), ("1", ["broadcast", "global"]), ("all", ["broadcast"]), ("all", ["global", "whisper-1"]),
            ("1", ["whisper-"]), ("1", ["whisper-1 2"]), ("1", ["Broadcast"]), ("1", [null!])
        })
            Assert.Throws<ArgumentException>(() => ExtensionJwt.CreateForPubSub(Secret, OwnerId, channel, targets));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64!")]
    [InlineData("abc")]
    [InlineData("====")]
    public void InvalidSecretsAreRejectedWithoutEchoingThem(string value)
    {
        var error = Assert.ThrowsAny<ArgumentException>(() => ExtensionSecret.FromBase64(value));
        if (value.Trim().Length > 0) Assert.DoesNotContain(value, error.Message, StringComparison.Ordinal);
        Assert.ThrowsAny<ArgumentException>(() => ExtensionSecret.FromBase64(null!));
    }

    [Fact]
    public async Task SecretsTokensAndProvidersAreRedactedFromToString()
    {
        Assert.Equal("ExtensionSecret [redacted]", Secret.ToString());
        var provider = new ExtensionJwtTokenProvider(ExtensionClientId, OwnerId, Secret, timeProvider: new ManualTimeProvider());
        Assert.Equal("ExtensionJwtTokenProvider { ExtensionClientId = ext-client, OwnerUserId = 27419012, Secret = [redacted] }", provider.ToString());
        var token = await provider.GetTokenAsync();
        Assert.DoesNotContain(token.Value, token.ToString(), StringComparison.Ordinal);
        var shared = JsonSerializer.Deserialize(Fixture("get-extension-secrets"), HelixJsonContext.Default.HelixPageExtensionSecretSet)!.Data.Single().Secrets[0];
        Assert.Equal("ExtensionSharedSecret { ActiveAt = 2026-10-09T11:00:00.0000000+00:00, ExpiresAt = 2026-11-09T11:00:00.0000000+00:00, Content = [redacted] }", shared.ToString());
        Assert.Equal("ExtensionSecret [redacted]", ExtensionSecret.FromBase64(shared.Content).ToString());
    }

    [Fact]
    public async Task ProviderIssuesUnknownKindTokensRefreshesByReissuingAndRotatesSecrets()
    {
        var time = new ManualTimeProvider();
        var provider = new ExtensionJwtTokenProvider(ExtensionClientId, OwnerId, Secret, timeProvider: time);
        var token = await provider.GetTokenAsync();
        Assert.Equal(GoldenToken, token.Value);
        Assert.Equal(TwitchTokenKind.Unknown, token.Kind);
        Assert.Equal(ExtensionClientId, token.ClientId);
        Assert.Equal(OwnerId, token.UserId);
        Assert.False(token.ScopesKnown);
        Assert.Equal(DateTimeOffset.Parse("2026-10-09T12:03:00Z"), token.ExpiresAt);
        Assert.Equal(TimeSpan.FromMinutes(3), provider.Lifetime);

        time.Advance(TimeSpan.FromSeconds(1));
        var refreshed = await provider.RefreshTokenAsync(token);
        Assert.NotEqual(token.Value, refreshed.Value);
        Assert.Equal(DateTimeOffset.Parse("2026-10-09T12:03:01Z"), refreshed.ExpiresAt);

        var rotated = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray());
        provider.ReplaceSecret(ExtensionSecret.FromBase64(rotated));
        VerifySignature((await provider.GetTokenAsync()).Value, rotated);

        Assert.Throws<ArgumentNullException>(() => provider.ReplaceSecret(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await provider.RefreshTokenAsync(null!));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await provider.GetTokenAsync(new CancellationToken(true)));
        Assert.Throws<ArgumentException>(() => new ExtensionJwtTokenProvider("", OwnerId, Secret));
        Assert.Throws<ArgumentException>(() => new ExtensionJwtTokenProvider(ExtensionClientId, " ", Secret));
        Assert.Throws<ArgumentNullException>(() => new ExtensionJwtTokenProvider(ExtensionClientId, OwnerId, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExtensionJwtTokenProvider(ExtensionClientId, OwnerId, Secret, TimeSpan.FromHours(2)));
    }

    [Fact]
    public async Task JwtTransportMustBeConfiguredWithTheExtensionClientId()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("No request expected")));
        var client = new HelixClient(new(http, new ExtensionJwtTokenProvider(ExtensionClientId, OwnerId, Secret), new() { ClientId = "other" })).Extensions;
        await Assert.ThrowsAsync<TwitchAuthorizationException>(() => client.GetExtensionSecretsAsync("ext"));
    }

    [Fact]
    public async Task ConfigurationSegmentsUseJwtRepeatSegmentsAndOmitNullFields()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(Payload(), JwtPayload(request));
            Assert.Equal("/helix/extensions/configurations", request.RequestUri!.AbsolutePath);
            if (++step == 1)
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("?broadcaster_id=1&extension_id=ext&segment=broadcaster&segment=global", request.RequestUri.Query);
                Assert.Null(request.Content);
                return TestHttpHandler.Json(Fixture("get-extension-configuration-segment"));
            }
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("", request.RequestUri.Query);
            var body = await request.Content!.ReadAsStringAsync(ct);
            if (step == 2)
            {
                using var json = JsonDocument.Parse(body);
                Assert.Equal(5, json.RootElement.EnumerateObject().Count());
                Assert.Equal("ext", json.RootElement.GetProperty("extension_id").GetString());
                Assert.Equal("developer", json.RootElement.GetProperty("segment").GetString());
                Assert.Equal("1", json.RootElement.GetProperty("broadcaster_id").GetString());
                Assert.Equal("{\"a\":1}", json.RootElement.GetProperty("content").GetString());
                Assert.Equal("0.0.2", json.RootElement.GetProperty("version").GetString());
            }
            else Assert.Equal("{\"extension_id\":\"ext\",\"segment\":\"global\",\"content\":\"\"}", body);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        var client = JwtClient(http);
        var page = await client.GetExtensionConfigurationSegmentsAsync(new() { ExtensionId = "ext", BroadcasterId = "1", Segments = ["broadcaster", "global"] });
        Assert.Equal("41245072", page.Data[0].BroadcasterId);
        Assert.Null(page.Data[1].BroadcasterId);
        Assert.Equal("hello config!", page.Data[1].Content);
        await client.SetExtensionConfigurationSegmentAsync(new() { ExtensionId = "ext", Segment = "developer", BroadcasterId = "1", Content = "{\"a\":1}", Version = "0.0.2" });
        await client.SetExtensionConfigurationSegmentAsync(new() { ExtensionId = "ext", Segment = "global", Content = "" });
        Assert.Equal(3, step);
    }

    [Fact]
    public async Task RequiredConfigurationSendsBroadcasterQueryAndAllBodyFields()
    {
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(Payload(), JwtPayload(request));
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("/helix/extensions/required_configuration", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1", request.RequestUri.Query);
            Assert.Equal("{\"extension_id\":\"ext\",\"extension_version\":\"0.0.9\",\"required_configuration\":\"configured-v2\"}", await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        await JwtClient(http).SetExtensionRequiredConfigurationAsync("1", new() { ExtensionId = "ext", ExtensionVersion = "0.0.9", RequiredConfiguration = "configured-v2" });
    }

    [Fact]
    public async Task PubSubMessagesCarryChannelOrGlobalClaimsInTheirOwnJwt()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            var payload = JwtPayload(request);
            if (++step == 3)
            {
                Assert.Equal(Payload(), payload);
                return TestHttpHandler.Json(Fixture("get-extension-secrets"));
            }
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/helix/extensions/pubsub", request.RequestUri!.AbsolutePath);
            Assert.Equal("", request.RequestUri.Query);
            var body = await request.Content!.ReadAsStringAsync(ct);
            if (step == 1)
            {
                Assert.Equal(Payload(",\"channel_id\":\"1\",\"pubsub_perms\":{\"send\":[\"broadcast\",\"whisper-9\"]}"), payload);
                Assert.Equal("{\"target\":[\"broadcast\",\"whisper-9\"],\"broadcaster_id\":\"1\",\"is_global_broadcast\":false,\"message\":\"hello\"}", body);
            }
            else
            {
                Assert.Equal(Payload(",\"channel_id\":\"all\",\"pubsub_perms\":{\"send\":[\"global\"]}"), payload);
                Assert.Equal("{\"target\":[\"global\"],\"is_global_broadcast\":true,\"message\":\"x\"}", body);
            }
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        var client = JwtClient(http);
        await client.SendExtensionPubSubMessageAsync(new()
        {
            Target = [ExtensionPubSubTargets.Broadcast, ExtensionPubSubTargets.Whisper("9")], BroadcasterId = "1", IsGlobalBroadcast = false, Message = "hello"
        });
        await client.SendExtensionPubSubMessageAsync(new() { Target = [ExtensionPubSubTargets.Global], IsGlobalBroadcast = true, Message = "x" });
        // Channel claims never leak into later requests on the same flow.
        await client.GetExtensionSecretsAsync("ext");
        Assert.Equal(3, step);
    }

    [Fact]
    public async Task ChatChannelClaimsStayScopedToEachConcurrentRequest()
    {
        var arrived = 0;
        var bothArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            var payload = JwtPayload(request);
            if (request.RequestUri!.AbsolutePath != "/helix/extensions/chat")
            {
                Assert.Equal(Payload(), payload);
                return TestHttpHandler.Json(Fixture("get-extension-secrets"));
            }
            Assert.Equal(HttpMethod.Post, request.Method);
            var channel = request.RequestUri.Query["?broadcaster_id=".Length..];
            Assert.Equal(Payload($",\"channel_id\":\"{channel}\""), payload);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal(3, json.RootElement.EnumerateObject().Count());
            Assert.Equal("hi " + channel, json.RootElement.GetProperty("text").GetString());
            Assert.Equal("ext", json.RootElement.GetProperty("extension_id").GetString());
            Assert.Equal("0.0.9", json.RootElement.GetProperty("extension_version").GetString());
            if (Interlocked.Increment(ref arrived) == 2) bothArrived.SetResult();
            await bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        var client = JwtClient(http);
        await Task.WhenAll(Send("1"), Send("2"));
        await client.GetExtensionSecretsAsync("ext");
        Task Send(string channel) => client.SendExtensionChatMessageAsync(channel, new() { Text = "hi " + channel, ExtensionId = "ext", ExtensionVersion = "0.0.9" });
    }

    [Fact]
    public async Task RejectedJwtIsReissuedWithTheSameChannelClaimsOrSurfacedWhenUnchanged()
    {
        var time = new ManualTimeProvider();
        var tokens = new List<string>();
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            tokens.Add(request.Headers.Authorization!.Parameter!);
            if (tokens.Count is 1 or 3)
            {
                if (tokens.Count == 1) time.Advance(TimeSpan.FromSeconds(5));
                return Task.FromResult(TestHttpHandler.Json("{\"error\":\"Unauthorized\",\"status\":401,\"message\":\"JWT expired\"}", HttpStatusCode.Unauthorized));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }));
        var client = JwtClient(http, time);
        await client.SendExtensionChatMessageAsync("1", new() { Text = "hi", ExtensionId = "ext", ExtensionVersion = "0.0.9" });
        Assert.Equal(2, tokens.Count);
        Assert.Equal(Payload(",\"channel_id\":\"1\"", 1791547385), PayloadJson(tokens[1]));
        // Without a clock change the reissued JWT is identical, so the 401 is reported instead of resent.
        var error = await Assert.ThrowsAsync<TwitchApiException>(() => client.SendExtensionChatMessageAsync("1", new() { Text = "hi", ExtensionId = "ext", ExtensionVersion = "0.0.9" }));
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Equal(3, tokens.Count);
    }

    [Fact]
    public async Task SecretEndpointsUseExtensionIdAndOptionalDelayWithoutBody()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(Payload(), JwtPayload(request));
            Assert.Equal("/helix/extensions/jwt/secrets", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Content);
            Assert.Equal(++step == 1 ? HttpMethod.Get : HttpMethod.Post, request.Method);
            Assert.Equal(step switch { 1 or 3 => "?extension_id=ext", _ => "?extension_id=ext&delay=300" }, request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture(step == 1 ? "get-extension-secrets" : "create-extension-secret")));
        }));
        var client = JwtClient(http);
        var secrets = (await client.GetExtensionSecretsAsync("ext")).Data.Single();
        Assert.Equal(1, secrets.FormatVersion);
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 12, 5, 0, 123, TimeSpan.Zero).AddTicks(4560), secrets.Secrets[1].ActiveAt);
        var created = (await client.CreateExtensionSecretAsync("ext", 300)).Data.Single().Secrets.Single();
        Assert.Equal(DateTimeOffset.Parse("2026-10-09T12:10:00Z"), created.ActiveAt);
        await client.CreateExtensionSecretAsync("ext");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.CreateExtensionSecretAsync("ext", 299));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetExtensionSecretsAsync(" "));
        Assert.Equal(3, step);
    }

    [Fact]
    public async Task ExtensionLookupsSelectJwtOrOAuthEndpointAndOptionalVersion()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            if (request.RequestUri!.AbsolutePath == "/helix/extensions")
            {
                Assert.Equal(Payload(), JwtPayload(request));
                Assert.Equal("?extension_id=ext&extension_version=0.0.9", request.RequestUri.Query);
                return Task.FromResult(TestHttpHandler.Json(Fixture("get-extensions")));
            }
            Assert.Equal("/helix/extensions/released", request.RequestUri.AbsolutePath);
            Assert.Equal("app", request.Headers.Authorization!.Parameter);
            Assert.Equal("?extension_id=ext", request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture("get-released-extensions")));
        }));
        var extension = (await JwtClient(http).GetExtensionsAsync("ext", "0.0.9")).Data.Single();
        Assert.Equal("https://example.org/icon-24.png", extension.IconUrls["24x24"]);
        Assert.Equal(16, extension.Views.Component!.AspectRatioX);
        Assert.Equal(300, extension.Views.Panel!.Height);
        Assert.Equal("InTest", extension.State);
        var released = (await OAuthClient(http, AppToken).GetReleasedExtensionsAsync("ext")).Data.Single();
        Assert.Equal("Released", released.State);
        Assert.Empty(released.ScreenshotUrls);
    }

    [Fact]
    public async Task ExtensionViewsMayBePartial()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json(
            "{\"data\":[{\"author_name\":\"a\",\"configuration_location\":\"custom\",\"id\":\"e\",\"name\":\"n\",\"state\":\"Released\",\"subscriptions_support_level\":\"none\",\"version\":\"1\",\"views\":{\"panel\":{\"viewer_url\":\"p.html\",\"height\":100,\"can_link_external_content\":false}}}]}"))));
        var extension = (await OAuthClient(http, UserToken).GetReleasedExtensionsAsync("e")).Data.Single();
        Assert.Null(extension.Views.Mobile);
        Assert.Null(extension.Views.Component);
        Assert.Null(extension.Views.VideoOverlay);
        Assert.Null(extension.Views.Config);
        Assert.Equal("p.html", extension.Views.Panel!.ViewerUrl);
    }

    [Fact]
    public async Task LiveChannelsAcceptStringOrObjectCursorsAndEnumeratorFollowsThem()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/extensions/live", request.RequestUri!.AbsolutePath);
            Assert.Equal("user", request.Headers.Authorization!.Parameter);
            var expectedAfter = ++step switch { 1 => "initial", 2 => "YVc1emRHRnNiRjlqYUdGdWJtVnNYMmxrUFRFd01qSTVOVFE0TWpNbmNHRm5aVDB5", _ => "object-cursor" };
            Assert.Equal("?extension_id=ext&first=100&after=" + expectedAfter, request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(step switch
            {
                1 => Fixture("get-extension-live-channels"),
                2 => "{\"data\":[{\"broadcaster_id\":\"3\",\"broadcaster_name\":\"c\",\"game_name\":\"g\",\"game_id\":\"1\",\"title\":\"t\"}],\"pagination\":{\"cursor\":\"object-cursor\",\"other\":[1]}}",
                _ => "{\"data\":[],\"pagination\":\"\"}"
            }));
        }));
        var names = new List<string>();
        await foreach (var channel in OAuthClient(http, UserToken).EnumerateExtensionLiveChannelsAsync(new() { ExtensionId = "ext", First = 100, After = "initial" }))
            names.Add(channel.BroadcasterName);
        Assert.Equal(new[] { "swoosh_xii", "quiet", "c" }, names);
        Assert.Equal(3, step);

        using var single = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json("{\"data\":[],\"pagination\":{\"cursor\":null}}"))));
        Assert.Null((await OAuthClient(single, AppToken).GetExtensionLiveChannelsAsync(new() { ExtensionId = "ext" })).Pagination);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => OAuthClient(single).GetExtensionLiveChannelsAsync(new() { ExtensionId = "ext", First = 101 }));
        await Assert.ThrowsAsync<ArgumentException>(() => OAuthClient(single).GetExtensionLiveChannelsAsync(new() { ExtensionId = "" }));
        using var invalid = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json("{\"data\":[],\"pagination\":5}"))));
        await Assert.ThrowsAsync<JsonException>(() => OAuthClient(invalid).GetExtensionLiveChannelsAsync(new() { ExtensionId = "ext" }));
    }

    [Fact]
    public async Task BitsProductsUseExtensionAppTokenAndSerializeOnlySuppliedOptionalFields()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal("/helix/bits/extensions", request.RequestUri!.AbsolutePath);
            Assert.Equal("app", request.Headers.Authorization!.Parameter);
            switch (++step)
            {
                case 1:
                    Assert.Equal(HttpMethod.Get, request.Method);
                    Assert.Equal("?should_include_all=false", request.RequestUri.Query);
                    return TestHttpHandler.Json("{\"data\":[{\"sku\":\"s\",\"cost\":{\"amount\":1,\"type\":\"bits\"},\"in_development\":false,\"display_name\":\"d\",\"expiration\":\"\",\"is_broadcast\":false}]}");
                case 2:
                    Assert.Equal(HttpMethod.Put, request.Method);
                    Assert.Equal("{\"sku\":\"power.up-1_b\",\"cost\":{\"amount\":10000,\"type\":\"bits\"},\"display_name\":\"Power up\",\"in_development\":false,\"expiration\":\"2027-01-01T00:00:00+00:00\",\"is_broadcast\":true}",
                        await request.Content!.ReadAsStringAsync(ct));
                    break;
                default:
                    Assert.Equal("{\"sku\":\"1010\",\"cost\":{\"amount\":1,\"type\":\"bits\"},\"display_name\":\"Crate\"}", await request.Content!.ReadAsStringAsync(ct));
                    break;
            }
            return TestHttpHandler.Json(Fixture("update-extension-bits-product"));
        }));
        var client = OAuthClient(http, AppToken);
        Assert.Null((await client.GetExtensionBitsProductsAsync(false)).Data.Single().Expiration);
        var updated = await client.UpdateExtensionBitsProductAsync(new()
        {
            Sku = "power.up-1_b", Cost = new() { Amount = 10000 }, DisplayName = "Power up", InDevelopment = false,
            Expiration = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), IsBroadcast = true
        });
        Assert.Equal(DateTimeOffset.Parse("2027-01-01T00:00:00Z"), updated.Data.Single().Expiration);
        await client.UpdateExtensionBitsProductAsync(new() { Sku = "1010", Cost = new() { Amount = 1, Type = "bits" }, DisplayName = "Crate" });
        Assert.Equal(3, step);
    }

    [Fact]
    public async Task BitsProductLimitsAreValidatedLocally()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"data\":[]}")); }));
        var client = OAuthClient(http, AppToken);
        UpdateExtensionBitsProductRequest Product(string sku = "sku", int amount = 1, string type = "bits", string name = "name")
            => new() { Sku = sku, Cost = new() { Amount = amount, Type = type }, DisplayName = name };
        await client.UpdateExtensionBitsProductAsync(Product(new string('a', 255), 10000, name: string.Concat(Enumerable.Repeat("😀", 255))));
        foreach (var invalid in new[] { Product(""), Product("has space"), Product("sküu"), Product("a/b"), Product(new string('a', 256)), Product(type: "BITS"), Product(name: " "), Product(name: new string('x', 256)) })
            await Assert.ThrowsAnyAsync<ArgumentException>(() => client.UpdateExtensionBitsProductAsync(invalid));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.UpdateExtensionBitsProductAsync(Product(amount: 0)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.UpdateExtensionBitsProductAsync(Product(amount: 10001)));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.UpdateExtensionBitsProductAsync(new() { Sku = "s", Cost = null!, DisplayName = "d" }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ConfigurationPubSubChatAndLookupInputsAreValidatedBeforeSending()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{\"data\":[]}")); }));
        var client = JwtClient(http);
        await client.GetExtensionConfigurationSegmentsAsync(new() { ExtensionId = "ext", Segments = ["global", "global"] });
        await client.SetExtensionConfigurationSegmentAsync(new() { ExtensionId = "ext", Segment = "broadcaster", BroadcasterId = "1", Content = new string('a', 5120) });
        await client.SendExtensionPubSubMessageAsync(new() { Target = ["broadcast"], BroadcasterId = "1", Message = string.Concat(Enumerable.Repeat("é", 2560)) });
        await client.SendExtensionChatMessageAsync("1", new() { Text = string.Concat(Enumerable.Repeat("😀", 280)), ExtensionId = "ext", ExtensionVersion = "1" });
        Assert.Equal(4, calls);

        Func<Task>[] invalid =
        [
            () => client.GetExtensionConfigurationSegmentsAsync(new() { ExtensionId = "ext" }),
            () => client.GetExtensionConfigurationSegmentsAsync(new() { ExtensionId = "ext", Segments = ["Global"] }),
            () => client.GetExtensionConfigurationSegmentsAsync(new() { ExtensionId = "ext", Segments = ["global", "developer"] }),
            () => client.GetExtensionConfigurationSegmentsAsync(new() { ExtensionId = "ext", Segments = ["global"], BroadcasterId = "1" }),
            () => client.GetExtensionConfigurationSegmentsAsync(new() { ExtensionId = " ", Segments = ["global"] }),
            () => client.SetExtensionConfigurationSegmentAsync(new() { ExtensionId = "ext", Segment = "channel" }),
            () => client.SetExtensionConfigurationSegmentAsync(new() { ExtensionId = "ext", Segment = "developer" }),
            () => client.SetExtensionConfigurationSegmentAsync(new() { ExtensionId = "ext", Segment = "global", BroadcasterId = "1" }),
            () => client.SetExtensionConfigurationSegmentAsync(new() { ExtensionId = "ext", Segment = "global", Content = new string('a', 5121) }),
            () => client.SetExtensionConfigurationSegmentAsync(new() { ExtensionId = "ext", Segment = "global", Version = " " }),
            () => client.SetExtensionRequiredConfigurationAsync(" ", new() { ExtensionId = "ext", ExtensionVersion = "1", RequiredConfiguration = "c" }),
            () => client.SetExtensionRequiredConfigurationAsync("1", new() { ExtensionId = "ext", ExtensionVersion = "", RequiredConfiguration = "c" }),
            () => client.SetExtensionRequiredConfigurationAsync("1", new() { ExtensionId = "ext", ExtensionVersion = "1", RequiredConfiguration = "" }),
            () => client.SendExtensionPubSubMessageAsync(new() { BroadcasterId = "1", Message = "m" }),
            () => client.SendExtensionPubSubMessageAsync(new() { Target = ["broadcast", "global"], BroadcasterId = "1", Message = "m" }),
            () => client.SendExtensionPubSubMessageAsync(new() { Target = ["global"], BroadcasterId = "1", Message = "m" }),
            () => client.SendExtensionPubSubMessageAsync(new() { Target = ["global"], IsGlobalBroadcast = true, BroadcasterId = "1", Message = "m" }),
            () => client.SendExtensionPubSubMessageAsync(new() { Target = ["broadcast"], IsGlobalBroadcast = true, Message = "m" }),
            () => client.SendExtensionPubSubMessageAsync(new() { Target = ["broadcast"], Message = "m" }),
            () => client.SendExtensionPubSubMessageAsync(new() { Target = ["whisper-"], BroadcasterId = "1", Message = "m" }),
            () => client.SendExtensionPubSubMessageAsync(new() { Target = ["broadcast"], BroadcasterId = "1", Message = "" }),
            () => client.SendExtensionPubSubMessageAsync(new() { Target = ["broadcast"], BroadcasterId = "1", Message = string.Concat(Enumerable.Repeat("é", 2561)) }),
            () => client.SendExtensionChatMessageAsync("1", new() { Text = string.Concat(Enumerable.Repeat("😀", 281)), ExtensionId = "ext", ExtensionVersion = "1" }),
            () => client.SendExtensionChatMessageAsync("1", new() { Text = " ", ExtensionId = "ext", ExtensionVersion = "1" }),
            () => client.SendExtensionChatMessageAsync("1", new() { Text = "t", ExtensionId = "ext", ExtensionVersion = " " }),
            () => client.SendExtensionChatMessageAsync("", new() { Text = "t", ExtensionId = "ext", ExtensionVersion = "1" }),
            () => client.GetExtensionsAsync("ext", " "),
            () => client.GetExtensionsAsync(""),
            () => client.GetReleasedExtensionsAsync("ext", ""),
        ];
        foreach (var call in invalid) await Assert.ThrowsAnyAsync<ArgumentException>(call);
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task OAuthTokensAreRejectedForJwtEndpointsAndUserTokensForAppOnlyEndpoints()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("No request expected")));
        foreach (var token in new[] { AppToken, UserToken })
            foreach (var call in JwtOperations(OAuthClient(http, token))) await Assert.ThrowsAsync<TwitchAuthorizationException>(call);
        foreach (var call in AppOnlyOperations(OAuthClient(http, UserToken))) await Assert.ThrowsAsync<TwitchAuthorizationException>(call);
    }

    [Fact]
    public async Task OAuthEndpointsAcceptTheirDocumentedTokenKinds()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            calls++;
            return Task.FromResult(TestHttpHandler.Json(request.RequestUri!.AbsolutePath == "/helix/extensions/live" ? Fixture("get-extension-live-channels") : "{\"data\":[]}"));
        }));
        foreach (var call in AppOnlyOperations(OAuthClient(http, AppToken)).Concat(AppOrUserOperations(OAuthClient(http, AppToken))).Concat(AppOrUserOperations(OAuthClient(http, UserToken))))
            await call();
        Assert.Equal(6, calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ErrorsArePreservedWithMessageAndTraceIdForEveryEndpoint(HttpStatusCode status)
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) =>
        {
            calls++;
            var response = TestHttpHandler.Json($"{{\"error\":\"Error\",\"status\":{(int)status},\"message\":\"extension failure\"}}", status);
            response.Headers.Add("Twitch-Trace-Id", "trace-ext");
            return Task.FromResult(response);
        }));
        var operations = JwtOperations(JwtClient(http)).Concat(AppOnlyOperations(OAuthClient(http, AppToken))).Concat(AppOrUserOperations(OAuthClient(http, AppToken))).ToArray();
        foreach (var call in operations)
        {
            var error = await Assert.ThrowsAsync<TwitchApiException>(call);
            Assert.Equal(status, error.StatusCode);
            Assert.Equal("extension failure", error.Message);
            Assert.Equal("Error", error.Error);
            Assert.Equal("trace-ext", error.RequestId);
        }
        Assert.Equal(operations.Length, calls);
    }

    private static Func<Task>[] JwtOperations(ExtensionsClient client) =>
    [
        () => client.GetExtensionConfigurationSegmentsAsync(new() { ExtensionId = "ext", Segments = ["global"] }),
        () => client.SetExtensionConfigurationSegmentAsync(new() { ExtensionId = "ext", Segment = "global", Content = "c" }),
        () => client.SetExtensionRequiredConfigurationAsync("1", new() { ExtensionId = "ext", ExtensionVersion = "1", RequiredConfiguration = "c" }),
        () => client.SendExtensionPubSubMessageAsync(new() { Target = ["broadcast"], BroadcasterId = "1", Message = "m" }),
        () => client.GetExtensionSecretsAsync("ext"),
        () => client.CreateExtensionSecretAsync("ext"),
        () => client.SendExtensionChatMessageAsync("1", new() { Text = "t", ExtensionId = "ext", ExtensionVersion = "1" }),
        () => client.GetExtensionsAsync("ext")
    ];

    private static Func<Task>[] AppOnlyOperations(ExtensionsClient client) =>
    [
        () => client.GetExtensionBitsProductsAsync(true),
        () => client.UpdateExtensionBitsProductAsync(new() { Sku = "s", Cost = new() { Amount = 1 }, DisplayName = "d" })
    ];

    private static Func<Task>[] AppOrUserOperations(ExtensionsClient client) =>
    [
        () => client.GetExtensionLiveChannelsAsync(new() { ExtensionId = "ext" }),
        () => client.GetReleasedExtensionsAsync("ext")
    ];

    private static string JwtPayload(HttpRequestMessage request)
    {
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(ExtensionClientId, request.Headers.GetValues("Client-Id").Single());
        var token = request.Headers.Authorization.Parameter!;
        VerifySignature(token, SecretBase64);
        return PayloadJson(token);
    }

    private static string PayloadJson(string token) => Encoding.UTF8.GetString(FromBase64Url(token.Split('.')[1]));

    private static void VerifySignature(string token, string secretBase64)
    {
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        Assert.DoesNotContain(token, c => c is '+' or '/' or '=');
        Assert.Equal("{\"alg\":\"HS256\",\"typ\":\"JWT\"}", Encoding.UTF8.GetString(FromBase64Url(parts[0])));
        using var hmac = new HMACSHA256(Convert.FromBase64String(secretBase64));
        Assert.Equal(hmac.ComputeHash(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1])), FromBase64Url(parts[2]));
    }

    private static byte[] FromBase64Url(string value)
    {
        var standard = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(standard.PadRight(standard.Length + (4 - standard.Length % 4) % 4, '='));
    }
}
