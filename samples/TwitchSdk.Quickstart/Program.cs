using Microsoft.Extensions.DependencyInjection;
using TwitchSdk.Authentication;
using TwitchSdk.Core;
using TwitchSdk.DependencyInjection;
using TwitchSdk.Helix;

var clientId = Environment.GetEnvironmentVariable("TWITCH_CLIENT_ID")
    ?? throw new InvalidOperationException("Set TWITCH_CLIENT_ID.");
var clientSecret = Environment.GetEnvironmentVariable("TWITCH_CLIENT_SECRET")
    ?? throw new InvalidOperationException("Set TWITCH_CLIENT_SECRET.");
var services = new ServiceCollection();
services.AddTwitchSdk(new TwitchHttpOptions { ClientId = clientId }, sp =>
{
    var oauth = sp.GetRequiredService<TwitchOAuthClient>();
    return new RefreshingTokenProvider((_, ct) => oauth.GetAppTokenAsync(clientId, clientSecret, ct));
});
using var provider = services.BuildServiceProvider();
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
var tokenProvider = provider.GetRequiredService<IAccessTokenProvider>();
var oauthClient = provider.GetRequiredService<TwitchOAuthClient>();
// This short-lived sample validates before its only operation; a long-lived host must also run hourly validation.
var token = await tokenProvider.GetTokenAsync(stop.Token);
var validation = await oauthClient.ValidateAsync(token.Value, stop.Token);
if (validation.ClientId != clientId) throw new InvalidOperationException("Token client ID mismatch.");
var helix = provider.GetRequiredService<HelixClient>();
var users = await helix.GetUsersAsync(new() { Logins = [args.FirstOrDefault() ?? "twitchdev"] }, stop.Token);
foreach (var user in users.Data) Console.WriteLine($"{user.Id}: {user.DisplayName}");
