using Microsoft.Extensions.DependencyInjection;
using TwitchSdk.Authentication;
using TwitchSdk.Chat;
using TwitchSdk.Core;
using TwitchSdk.DependencyInjection;
using TwitchSdk.EventSub;
using TwitchSdk.Helix;

namespace TwitchSdk.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void ResolvesModulesWithSharedTransportAndIndependentSocketClients()
    {
        var creations = 0;
        var services = new ServiceCollection();
        services.AddTwitchSdk(new() { ClientId = "test" }, _ => { creations++; return new StaticAccessTokenProvider(new("token")); });
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.Same(provider.GetRequiredService<HelixClient>(), provider.GetRequiredService<HelixClient>());
        Assert.Same(provider.GetRequiredService<TwitchHttpClient>(), provider.GetRequiredService<TwitchHttpClient>());
        Assert.NotSame(provider.GetRequiredService<EventSubWebSocketClient>(), provider.GetRequiredService<EventSubWebSocketClient>());
        Assert.NotNull(provider.GetRequiredService<TwitchChatClient>());
        Assert.NotNull(provider.GetRequiredService<TwitchOAuthClient>());
        Assert.Equal(1, creations);
    }
}
