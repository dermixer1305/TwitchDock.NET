using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TwitchDock.Chat.Irc;
using TwitchDock.Core;

namespace TwitchDock.DependencyInjection;

public static class TwitchIrcServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="TwitchIrcClient"/> that authenticates with the token provider registered by AddTwitchDock.
    /// The token needs chat:read (and chat:edit to send). The host starts it with <see cref="TwitchIrcClient.RunAsync"/>.
    /// </summary>
    public static IServiceCollection AddTwitchIrc(this IServiceCollection services, TwitchIrcOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        services.AddSingleton(options);
        services.AddSingleton(sp => new TwitchIrcClient(sp.GetRequiredService<IAccessTokenProvider>(), options,
            timeProvider: sp.GetService<TimeProvider>(), logger: sp.GetService<ILogger<TwitchIrcClient>>()));
        return services;
    }
}
