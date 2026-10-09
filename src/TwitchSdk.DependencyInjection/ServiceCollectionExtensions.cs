using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TwitchSdk.Authentication;
using TwitchSdk.Chat;
using TwitchSdk.Core;
using TwitchSdk.EventSub;
using TwitchSdk.Helix;

namespace TwitchSdk.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers clients for one authorization. A token provider factory creates an owned singleton.</summary>
    /// <exception cref="ArgumentException">The options are invalid (see <see cref="TwitchHttpOptions.EnsureValid"/>); checked at registration.</exception>
    public static IServiceCollection AddTwitchSdk(this IServiceCollection services, TwitchHttpOptions options,
        Func<IServiceProvider, IAccessTokenProvider> tokenProviderFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokenProviderFactory);
        // Fail at startup rather than when the first client is resolved.
        options.EnsureValid();
        services.AddSingleton(options);
        services.AddSingleton(tokenProviderFactory);
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient("TwitchSdk.Helix").ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });
        services.AddHttpClient<TwitchOAuthClient>().ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });
        services.AddSingleton(sp => new TwitchHttpClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("TwitchSdk.Helix"),
            sp.GetRequiredService<IAccessTokenProvider>(), options, sp.GetRequiredService<TimeProvider>(), sp.GetService<ILogger<TwitchHttpClient>>()));
        services.AddSingleton<HelixClient>();
        services.AddSingleton<TwitchChatClient>();
        services.AddTransient(sp => new EventSubWebSocketClient(timeProvider: sp.GetRequiredService<TimeProvider>()));
        return services;
    }
}
