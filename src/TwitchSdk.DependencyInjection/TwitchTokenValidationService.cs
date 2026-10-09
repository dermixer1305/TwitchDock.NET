using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TwitchSdk.Authentication;
using TwitchSdk.Core;

namespace TwitchSdk.DependencyInjection;

public sealed class TwitchTokenValidationOptions
{
    /// <summary>The client ID the validated token must belong to.</summary>
    public required string ExpectedClientId { get; init; }
    /// <summary>Consecutive network or server failures tolerated before the service fails, which stops the host by default.</summary>
    public int MaxConsecutiveTransientFailures { get; init; } = 5;
    public TimeSpan TransientRetryDelay { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Called after each successful validation, for example to record the remaining lifetime.</summary>
    public Func<TokenValidation, CancellationToken, Task>? OnValidated { get; init; }
}

/// <summary>
/// Runs <see cref="TokenValidationLoop"/> for the registered authorization: at startup and hourly, as Twitch requires for user tokens.
/// A token that is invalid and cannot be refreshed ends the service with an exception, so the host stops instead of serving with a dead credential.
/// </summary>
public sealed class TwitchTokenValidationService(TwitchOAuthClient oauth, IAccessTokenProvider tokens, TwitchTokenValidationOptions options,
    TimeProvider? timeProvider = null, ILogger<TwitchTokenValidationService>? logger = null) : BackgroundService
{
    private readonly TwitchOAuthClient _oauth = oauth ?? throw new ArgumentNullException(nameof(oauth));
    private readonly IAccessTokenProvider _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    private readonly TwitchTokenValidationOptions _options = Validate(options);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ILogger _logger = logger ?? NullLogger<TwitchTokenValidationService>.Instance;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Validate in the background so a slow or failing network call never blocks host startup.
        await Task.Yield();
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TokenValidationLoop.RunAsync(_oauth, _tokens, _options.ExpectedClientId, async (result, ct) =>
                {
                    failures = 0;
                    if (_options.OnValidated is { } onValidated) await onValidated(result, ct).ConfigureAwait(false);
                }, _time, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) when (IsTransient(ex, stoppingToken) && ++failures <= _options.MaxConsecutiveTransientFailures)
            {
                _logger.LogWarning(ex, "Twitch token validation failed transiently ({Failures}/{Maximum}); retrying.", failures, _options.MaxConsecutiveTransientFailures);
                await Task.Delay(_options.TransientRetryDelay, _time, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransient(Exception exception, CancellationToken stoppingToken) => exception switch
    {
        OperationCanceledException => !stoppingToken.IsCancellationRequested,
        // RefreshingTokenProvider bounds a hung token endpoint with a TimeoutException.
        TimeoutException => true,
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => true,
        HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError } => true,
        _ => false,
    };

    internal static TwitchTokenValidationOptions Validate(TwitchTokenValidationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ExpectedClientId);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxConsecutiveTransientFailures);
        if (options.TransientRetryDelay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options), "The retry delay must be positive.");
        return options;
    }
}

public static class TwitchTokenValidationServiceCollectionExtensions
{
    /// <summary>Registers <see cref="TwitchTokenValidationService"/> for the authorization registered with AddTwitchSdk.</summary>
    public static IServiceCollection AddTwitchTokenValidation(this IServiceCollection services, TwitchTokenValidationOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        TwitchTokenValidationService.Validate(options);
        services.AddSingleton(options);
        services.AddHostedService(sp => new TwitchTokenValidationService(sp.GetRequiredService<TwitchOAuthClient>(), sp.GetRequiredService<IAccessTokenProvider>(),
            options, sp.GetService<TimeProvider>(), sp.GetService<ILogger<TwitchTokenValidationService>>()));
        return services;
    }
}
