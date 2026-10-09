using System.Net;
using System.Net.Http.Headers;
using System.Text;
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
        Assert.Equal(TimeSpan.FromHours(1), ex.RetryAfter);
    }

    [Fact]
    public async Task RateLimitErrorsCarryTheAdvertisedDelayOnlyFor429()
    {
        var time = new ManualTimeProvider();
        async Task<TwitchApiException> FailAsync(Action<HttpResponseMessage> configure, HttpStatusCode status = HttpStatusCode.TooManyRequests)
        {
            using var http = new HttpClient(new TestHttpHandler((_, _) =>
            {
                var response = TestHttpHandler.Json("{}", status);
                configure(response);
                return Task.FromResult(response);
            }));
            var transport = new TwitchHttpClient(http, new StaticAccessTokenProvider(new("token")), new() { ClientId = "client", MaxRateLimitRetries = 0, MaxTransientRetries = 0 }, time);
            return await Assert.ThrowsAsync<TwitchApiException>(() => transport.SendAsync(HttpMethod.Get, "users"));
        }
        // Retries exhausted: the later of Retry-After and Ratelimit-Reset is reported.
        var reset = await FailAsync(r =>
        {
            r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(5));
            r.Headers.Add("Ratelimit-Reset", time.GetUtcNow().AddSeconds(30).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        });
        Assert.Equal(TimeSpan.FromSeconds(30), reset.RetryAfter);
        Assert.Equal(TimeSpan.Zero, (await FailAsync(r => r.Headers.Add("Ratelimit-Reset", "0"))).RetryAfter);
        Assert.Null((await FailAsync(_ => { })).RetryAfter);
        Assert.Null((await FailAsync(r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(5)), HttpStatusCode.ServiceUnavailable)).RetryAfter);
    }

    [Theory]
    [InlineData(nameof(TwitchHttpOptions.ClientId))]
    [InlineData(nameof(TwitchHttpOptions.BaseAddress))]
    [InlineData(nameof(TwitchHttpOptions.MaxRateLimitRetries))]
    [InlineData(nameof(TwitchHttpOptions.MaxTransientRetries))]
    [InlineData(nameof(TwitchHttpOptions.MaxRetryDelay))]
    [InlineData(nameof(TwitchHttpOptions.FallbackRetryDelay))]
    [InlineData(nameof(TwitchHttpOptions.MaxResponseContentBytes))]
    public void OptionsValidationNamesTheInvalidProperty(string property)
    {
        TwitchHttpOptions options = property switch
        {
            nameof(TwitchHttpOptions.ClientId) => new() { ClientId = " " },
            nameof(TwitchHttpOptions.BaseAddress) => new() { ClientId = "client", BaseAddress = new("http://example.org/helix/") },
            nameof(TwitchHttpOptions.MaxRateLimitRetries) => new() { ClientId = "client", MaxRateLimitRetries = -1 },
            nameof(TwitchHttpOptions.MaxTransientRetries) => new() { ClientId = "client", MaxTransientRetries = -1 },
            nameof(TwitchHttpOptions.MaxRetryDelay) => new() { ClientId = "client", MaxRetryDelay = TimeSpan.Zero },
            nameof(TwitchHttpOptions.FallbackRetryDelay) => new() { ClientId = "client", FallbackRetryDelay = TimeSpan.FromMinutes(5) },
            _ => new() { ClientId = "client", MaxResponseContentBytes = 0 },
        };
        Assert.Equal(property, Assert.ThrowsAny<ArgumentException>(options.EnsureValid).ParamName);
        using var http = new HttpClient();
        Assert.Equal(property, Assert.ThrowsAny<ArgumentException>(() => new TwitchHttpClient(http, new StaticAccessTokenProvider(new("token")), options)).ParamName);
        new TwitchHttpOptions { ClientId = "client" }.EnsureValid();
    }

    [Theory]
    // Uri normalizes the host name "loopback" to localhost, so that request stays on this machine.
    [InlineData("http://loopback/helix/", true)]
    [InlineData("http://localhost.example.org/helix/", false)]
    [InlineData("http://127.0.0.1.example.org/helix/", false)]
    [InlineData("http://example.org/helix/", false)]
    [InlineData("http://127.0.0.1:8080/mock/", true)]
    [InlineData("http://[::1]:8080/mock/", true)]
    [InlineData("http://localhost:8080/mock/", true)]
    [InlineData("https://api.twitch.tv/helix/", true)]
    public void PlainHttpBaseAddressMustBeALoopbackAddressOrLocalhost(string baseAddress, bool valid)
    {
        var options = new TwitchHttpOptions { ClientId = "client", BaseAddress = new(baseAddress) };
        if (valid) options.EnsureValid();
        else Assert.Equal(nameof(TwitchHttpOptions.BaseAddress), Assert.Throws<ArgumentException>(options.EnsureValid).ParamName);
    }

    [Fact]
    public async Task ResponsesBeyondTheConfiguredSizeAreRejectedWhetherDeclaredOrStreamed()
    {
        var json = Encoding.UTF8.GetBytes("""{"data":[{"id":"1","login":"a","display_name":"A"},{"id":"2","login":"b","display_name":"B"}]}""");
        HttpResponseMessage Declared() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(json) };
        HttpResponseMessage Streamed() => new(HttpStatusCode.OK) { Content = new UnknownLengthContent(json) };
        foreach (var respond in new Func<HttpResponseMessage>[] { Declared, Streamed })
        {
            using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(respond())));
            TwitchHttpClient Transport(long limit) => new(http, new StaticAccessTokenProvider(new("token")), new() { ClientId = "client", MaxResponseContentBytes = limit });
            await Assert.ThrowsAsync<InvalidDataException>(() => new HelixClient(Transport(json.Length - 1)).GetUsersAsync());
            await Assert.ThrowsAsync<InvalidDataException>(() => Transport(json.Length - 1).SendTextAsync(HttpMethod.Get, "schedule/icalendar", authenticated: false));
            Assert.Equal(2, (await new HelixClient(Transport(json.Length)).GetUsersAsync()).Data.Count);
            Assert.Equal(json.Length, Encoding.UTF8.GetByteCount(await Transport(json.Length).SendTextAsync(HttpMethod.Get, "schedule/icalendar", authenticated: false)));
        }
        Assert.Equal(TwitchHttpOptions.DefaultMaxResponseContentBytes, new TwitchHttpOptions { ClientId = "client" }.MaxResponseContentBytes);
    }

    [Fact]
    public async Task OversizedErrorBodiesKeepTheStatusCode()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new UnknownLengthContent(Encoding.UTF8.GetBytes("{\"message\":\"" + new string('x', 256) + "\"}")),
        })));
        var transport = new TwitchHttpClient(http, new StaticAccessTokenProvider(new("token")), new() { ClientId = "client", MaxResponseContentBytes = 64 });
        var error = await Assert.ThrowsAsync<TwitchApiException>(() => transport.SendAsync(HttpMethod.Get, "users"));
        Assert.Equal((HttpStatusCode.BadRequest, "Twitch API returned HTTP 400."), (error.StatusCode!.Value, error.Message));
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
