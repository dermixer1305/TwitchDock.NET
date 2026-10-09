using System.Net;
using System.Net.Http.Headers;
using TwitchSdk.Core;
using TwitchSdk.Helix;
using TwitchSdk.Helix.Models;

namespace TwitchSdk.Tests;

public sealed class HttpTransportTests
{
    [Fact]
    public async Task ConcurrentRateLimitResponsesCanExtendAnExistingWait()
    {
        var time = new ManualTimeProvider();
        var firstResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => Interlocked.Increment(ref calls) switch
        {
            1 => firstResponse.Task,
            2 => secondResponse.Task,
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent))
        }));
        var transport = new TwitchHttpClient(http, new StaticAccessTokenProvider(new("token")), new() { ClientId = "client" }, time);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = transport.SendAsync(HttpMethod.Get, "users", cancellationToken: stop.Token);
        var second = transport.SendAsync(HttpMethod.Get, "users", cancellationToken: stop.Token);
        var limited1 = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        limited1.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(10));
        firstResponse.SetResult(limited1);
        await ManualTimeProvider.WaitUntilAsync(() => time.TimerCount == 1);
        time.Advance(TimeSpan.FromSeconds(5));
        var limited2 = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        limited2.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(15));
        secondResponse.SetResult(limited2);
        await ManualTimeProvider.WaitUntilAsync(() => time.TimerCount == 2);
        time.Advance(TimeSpan.FromSeconds(5));
        await ManualTimeProvider.WaitUntilAsync(() => time.TimerCount == 2 || Volatile.Read(ref calls) > 2);
        Assert.Equal(2, calls);
        time.Advance(TimeSpan.FromSeconds(10));
        await Task.WhenAll(first, second);
        Assert.Equal(4, calls);
    }

    private static TwitchHttpClient Create(HttpClient http, IAccessTokenProvider? tokens = null, int retries = 2) => new(http,
        tokens ?? new StaticAccessTokenProvider(new("test-token")), new()
        {
            ClientId = "test-client", FallbackRetryDelay = TimeSpan.FromMilliseconds(1), MaxRateLimitRetries = retries
        });

    [Fact]
    public async Task EncodesRepeatedQueryValuesAndSetsPerRequestHeaders()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal("https://api.twitch.tv/helix/users?id=1&id=2&login=a%26b", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer test-token", request.Headers.Authorization!.ToString());
            Assert.Equal("test-client", Assert.Single(request.Headers.GetValues("Client-Id")));
            return Task.FromResult(TestHttpHandler.Json("{\"data\":[]}"));
        }));
        await new HelixClient(Create(http)).GetUsersAsync(new() { Ids = ["1", "2"], Logins = ["a&b"] });
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task RetriesRejectedRateLimitedWriteWithIdenticalBody()
    {
        var bodies = new List<string>();
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            if (bodies.Count == 1)
            {
                var limited = TestHttpHandler.Json("{}", HttpStatusCode.TooManyRequests);
                limited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(1));
                return limited;
            }
            return TestHttpHandler.Json("{\"data\":[{\"message_id\":\"m\",\"is_sent\":true}]}");
        }));
        var result = await new HelixClient(Create(http)).SendChatMessageAsync(new() { BroadcasterId = "1", SenderId = "2", Message = "hello" });
        Assert.True(Assert.Single(result.Data).IsSent);
        Assert.Equal(2, bodies.Count);
        Assert.Equal(bodies[0], bodies[1]);
        Assert.DoesNotContain("for_source_only", bodies[0]);
        Assert.DoesNotContain("pin", bodies[0]);
    }

    [Theory]
    [InlineData("GET", 3)]
    [InlineData("POST", 1)]
    [InlineData("PATCH", 1)]
    public async Task OnlyRetriesSafeReadsAfterServerErrors(string method, int expected)
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("bad gateway", HttpStatusCode.BadGateway)); }));
        var ex = await Assert.ThrowsAsync<TwitchApiException>(() => Create(http).SendAsync(new(method), "users"));
        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
        Assert.Equal(expected, calls);
    }

    [Fact]
    public async Task DoesNotWaitBeyondRetryBudget()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) =>
        {
            calls++;
            var response = TestHttpHandler.Json("{\"error\":\"Too Many Requests\",\"message\":\"limited\"}", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
            return Task.FromResult(response);
        }));
        var ex = await Assert.ThrowsAsync<TwitchApiException>(() => Create(http).SendAsync(HttpMethod.Get, "users"));
        Assert.Equal("limited", ex.Message);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RefreshesOnceAndReplaysWithNewToken()
    {
        var tokens = new StubTokens();
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            calls++;
            Assert.Equal(calls == 1 ? "old" : "new", request.Headers.Authorization!.Parameter);
            return Task.FromResult(TestHttpHandler.Json("{}", calls == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.NoContent));
        }));
        await Create(http, tokens).SendAsync(HttpMethod.Delete, "eventsub/subscriptions");
        Assert.Equal(1, tokens.Refreshes);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task StaticTokenDoesNotCauseUnauthorizedRetryLoop()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json("{}", HttpStatusCode.Unauthorized)); }));
        await Assert.ThrowsAsync<TwitchApiException>(() => Create(http).SendAsync(HttpMethod.Get, "users"));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("//example.com")]
    [InlineData("../oauth2")]
    [InlineData("users?token=x")]
    [InlineData("%2e%2e/users")]
    // Uri trims leading whitespace and control characters, which once turned these into network-path references.
    [InlineData(" //evil.example/steal")]
    [InlineData("\t//evil.example/steal")]
    [InlineData("\n//evil.example/steal")]
    [InlineData("\r//evil.example/steal")]
    [InlineData("users/")]
    [InlineData("chat//settings")]
    [InlineData("users ")]
    [InlineData("users\u0000")]
    public async Task RejectsNonEndpointPathsBeforeSendingCredentials(string path)
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("Must not send")));
        await Assert.ThrowsAsync<ArgumentException>(() => Create(http).SendAsync(HttpMethod.Get, path));
    }

    [Fact]
    public async Task CancellationStopsRateLimitWait()
    {
        using var cancel = new CancellationTokenSource();
        using var http = new HttpClient(new TestHttpHandler((_, _) =>
        {
            cancel.Cancel();
            return Task.FromResult(TestHttpHandler.Json("{}", HttpStatusCode.TooManyRequests));
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(http).SendAsync(HttpMethod.Get, "users", cancellationToken: cancel.Token));
    }

    private sealed class StubTokens : IAccessTokenProvider
    {
        public int Refreshes { get; private set; }
        public ValueTask<AccessToken> GetTokenAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new AccessToken("old"));
        public ValueTask<AccessToken> RefreshTokenAsync(AccessToken rejectedToken, CancellationToken cancellationToken = default) { Refreshes++; return ValueTask.FromResult(new AccessToken("new")); }
    }
}
