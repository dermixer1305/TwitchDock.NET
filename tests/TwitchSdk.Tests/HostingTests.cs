using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TwitchSdk.Authentication;
using TwitchSdk.Core;
using TwitchSdk.DependencyInjection;

namespace TwitchSdk.Tests;

public sealed class HostingTests
{
    private const string Valid = """{"client_id":"client","login":"user","scopes":["chat:read"],"user_id":"1","expires_in":3600}""";

    [Fact]
    public async Task ValidationLoopRefreshesOnceWhenTwitchRejectsTheToken()
    {
        var provider = new RotatingProvider();
        var validated = new List<string>();
        using var http = new HttpClient(new TestHttpHandler((request, _) => Task.FromResult(request.Headers.Authorization!.Parameter == "old"
            ? TestHttpHandler.Json("""{"status":401,"message":"invalid access token"}""", HttpStatusCode.Unauthorized)
            : TestHttpHandler.Json(Valid))));
        using var stop = new CancellationTokenSource();
        var loop = TokenValidationLoop.RunAsync(new TwitchOAuthClient(http), provider, "client", (result, _) =>
        {
            validated.Add(result.UserId!);
            stop.Cancel();
            return Task.CompletedTask;
        }, new ManualTimeProvider(), stop.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loop);
        Assert.Equal(["1"], validated);
        Assert.Equal(1, provider.Refreshes);
    }

    [Fact]
    public async Task ValidationLoopFailsWhenRefreshCannotReplaceTheToken()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json("""{"status":401,"message":"invalid access token"}""", HttpStatusCode.Unauthorized))));
        var error = await Assert.ThrowsAsync<TwitchApiException>(() => TokenValidationLoop.RunAsync(new TwitchOAuthClient(http),
            new StaticAccessTokenProvider(new("static")), "client", (_, _) => Task.CompletedTask, new ManualTimeProvider()));
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        var rotating = new RotatingProvider();
        await Assert.ThrowsAsync<TwitchApiException>(() => TokenValidationLoop.RunAsync(new TwitchOAuthClient(http), rotating, "client", (_, _) => Task.CompletedTask, new ManualTimeProvider()));
        Assert.Equal(1, rotating.Refreshes);
    }

    [Fact]
    public async Task HostedServiceRetriesTransientFailuresAndStopsOnInvalidTokens()
    {
        var time = new ManualTimeProvider();
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => Interlocked.Increment(ref calls) switch
        {
            1 => throw new HttpRequestException("network down"),
            2 => Task.FromResult(TestHttpHandler.Json("{}", HttpStatusCode.ServiceUnavailable)),
            _ => Task.FromResult(TestHttpHandler.Json("""{"status":401,"message":"invalid access token"}""", HttpStatusCode.Unauthorized)),
        }));
        using var service = new TwitchTokenValidationService(new TwitchOAuthClient(http), new StaticAccessTokenProvider(new("token")),
            new() { ExpectedClientId = "client", TransientRetryDelay = TimeSpan.FromSeconds(30) }, time);
        await service.StartAsync(CancellationToken.None);
        for (var expected = 1; expected <= 2; expected++)
        {
            await ManualTimeProvider.WaitUntilAsync(() => time.TimerCount == 1 && Volatile.Read(ref calls) == expected);
            time.Advance(TimeSpan.FromSeconds(30));
        }
        var error = await Assert.ThrowsAsync<TwitchApiException>(() => service.ExecuteTask!);
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task HostedServiceGivesUpAfterTheConfiguredTransientFailures()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new HttpRequestException("network down")));
        using var service = new TwitchTokenValidationService(new TwitchOAuthClient(http), new StaticAccessTokenProvider(new("token")),
            new() { ExpectedClientId = "client", MaxConsecutiveTransientFailures = 0 }, new ManualTimeProvider());
        // BackgroundService.StartAsync rethrows when ExecuteAsync already failed; otherwise the failure is on ExecuteTask.
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await service.StartAsync(CancellationToken.None);
            await service.ExecuteTask!;
        });
    }

    [Fact]
    public void RegistrationAddsTheHostedService()
    {
        var services = new ServiceCollection();
        services.AddTwitchSdk(new TwitchHttpOptions { ClientId = "client" }, _ => new StaticAccessTokenProvider(new("token")));
        services.AddTwitchTokenValidation(new() { ExpectedClientId = "client" });
        using var provider = services.BuildServiceProvider();
        Assert.IsType<TwitchTokenValidationService>(Assert.Single(provider.GetServices<IHostedService>()));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddTwitchTokenValidation(new() { ExpectedClientId = " " }));
    }

    private sealed class RotatingProvider : IAccessTokenProvider
    {
        private AccessToken _token = new("old");
        public int Refreshes;
        public ValueTask<AccessToken> GetTokenAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_token);
        public ValueTask<AccessToken> RefreshTokenAsync(AccessToken rejectedToken, CancellationToken cancellationToken = default)
        {
            Refreshes++;
            _token = new("new" + Refreshes);
            return ValueTask.FromResult(_token);
        }
    }
}
