using Microsoft.Extensions.DependencyInjection;
using TwitchDock.Authentication;
using TwitchDock.Chat;
using TwitchDock.Core;
using TwitchDock.DependencyInjection;
using TwitchDock.EventSub;
using TwitchDock.Helix;

namespace TwitchDock.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void ResolvesModulesWithSharedTransportAndIndependentSocketClients()
    {
        var creations = 0;
        var services = new ServiceCollection();
        services.AddTwitchDock(new() { ClientId = "test" }, _ => { creations++; return new StaticAccessTokenProvider(new("token")); });
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.Same(provider.GetRequiredService<HelixClient>(), provider.GetRequiredService<HelixClient>());
        Assert.Same(provider.GetRequiredService<TwitchHttpClient>(), provider.GetRequiredService<TwitchHttpClient>());
        Assert.NotSame(provider.GetRequiredService<EventSubWebSocketClient>(), provider.GetRequiredService<EventSubWebSocketClient>());
        Assert.NotNull(provider.GetRequiredService<TwitchChatClient>());
        Assert.NotNull(provider.GetRequiredService<TwitchOAuthClient>());
        Assert.Equal(1, creations);
    }

    [Fact]
    public void InvalidOptionsFailAtRegistration()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection().AddTwitchDock(new() { ClientId = "test", MaxRetryDelay = TimeSpan.Zero },
            _ => new StaticAccessTokenProvider(new("token"))));
        Assert.Equal(nameof(TwitchHttpOptions.MaxRetryDelay), error.ParamName);
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddTwitchDock(new() { ClientId = " " }, _ => new StaticAccessTokenProvider(new("token"))));
    }

    [Fact]
    public async Task OAuthClientsCreatedPerUseShareTheRegisteredSigningKeyCache()
    {
        using var fx = new OpenIdFixture();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(fx.Time);
        services.AddSingleton(fx.Cache);
        services.AddTwitchDock(new() { ClientId = "client" }, _ => new StaticAccessTokenProvider(new("token")));
        services.AddHttpClient<TwitchOAuthClient>().ConfigurePrimaryHttpMessageHandler(fx.CreateHandler);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var token = fx.Token();
        for (var i = 0; i < 3; i++) await provider.GetRequiredService<TwitchOAuthClient>().ValidateIdTokenAsync(token, "client", "n1");
        Assert.Equal(1, fx.Fetches);
    }

    [Fact]
    public void RegistersOneIrcClientUsingTheSharedTokenProvider()
    {
        var services = new ServiceCollection();
        services.AddTwitchDock(new() { ClientId = "test" }, _ => new StaticAccessTokenProvider(new("token")));
        services.AddTwitchIrc(new() { Login = "bot" });
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.Same(provider.GetRequiredService<TwitchDock.Chat.Irc.TwitchIrcClient>(), provider.GetRequiredService<TwitchDock.Chat.Irc.TwitchIrcClient>());
    }
}
