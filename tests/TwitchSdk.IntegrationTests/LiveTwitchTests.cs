using System.Text;
using System.Threading.Channels;
using TwitchSdk.Authentication;
using TwitchSdk.Core;
using TwitchSdk.EventSub;
using TwitchSdk.Helix;

namespace TwitchSdk.IntegrationTests;

/// <summary>
/// Read-only checks against the real Twitch services. Opt-in with TWITCHSDK_LIVE=1 because they need network access;
/// the credentialed checks additionally need TWITCH_CLIENT_ID and TWITCH_CLIENT_SECRET (an app of your own).
/// </summary>
public sealed class LiveTwitchTests
{
    private const string TwitchDevUserId = "141981764";

    [LiveTwitchFact]
    public async Task EventSubWebSocketReceivesTheRealWelcome()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var sessions = Channel.CreateUnbounded<EventSubSession>();
        var run = new EventSubWebSocketClient().RunAsync((session, _, _) => { sessions.Writer.TryWrite(session); return Task.CompletedTask; },
            (_, _) => Task.CompletedTask, stop.Token);
        var welcome = await sessions.Reader.ReadAsync(stop.Token);
        Assert.False(string.IsNullOrEmpty(welcome.Id));
        Assert.Equal("connected", welcome.Status);
        Assert.InRange(welcome.KeepaliveTimeoutSeconds ?? 0, 10, 600);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [LiveTwitchFact]
    public async Task PublicICalendarIsReadWithoutAcquiringAToken()
    {
        using var http = new HttpClient();
        var helix = new HelixClient(new TwitchHttpClient(http, new UnusableTokenProvider(), new TwitchHttpOptions { ClientId = "unused" }));
        try
        {
            var calendar = await helix.Schedule.GetChannelICalendarAsync(TwitchDevUserId);
            Assert.StartsWith("BEGIN:VCALENDAR", calendar, StringComparison.Ordinal);
        }
        catch (TwitchApiException ex)
        {
            // A channel without a schedule may answer with a structured error; the request still went out unauthenticated.
            Assert.InRange((int)ex.StatusCode!, 400, 499);
        }
    }

    [LiveTwitchFact]
    public async Task OpenIdSigningKeysAreFetchedAndParsed()
    {
        using var http = new HttpClient();
        var oauth = new TwitchOAuthClient(http, signingKeyCache: new OpenIdSigningKeyCache());
        static string Segment(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var token = Segment("""{"alg":"RS256","typ":"JWT","kid":"1"}""") + "." + Segment("""{"iss":"https://id.twitch.tv/oauth2","sub":"1","aud":"client","exp":4102444800,"iat":1700000000}""") + "." + Segment("signature");
        var error = await Assert.ThrowsAsync<TwitchIdTokenException>(() => oauth.ValidateIdTokenWithoutNonceAsync(token, "client"));
        // Either the real key was found and the forged signature rejected, or no key had this kid. Both prove the JWKS was fetched and parsed.
        Assert.DoesNotContain("could not be retrieved", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [LiveTwitchFact]
    public async Task InvalidCredentialsMapToStructuredOAuthErrors()
    {
        using var http = new HttpClient();
        var oauth = new TwitchOAuthClient(http);
        var clientError = await Assert.ThrowsAsync<TwitchApiException>(() => oauth.GetAppTokenAsync("twitchsdk0invalid0client0id00", "twitchsdk-invalid-secret"));
        Assert.InRange((int)clientError.StatusCode!, 400, 403);
        Assert.DoesNotContain("twitchsdk-invalid-secret", clientError.Message, StringComparison.Ordinal);
        var tokenError = await Assert.ThrowsAsync<TwitchApiException>(() => oauth.ValidateAsync("twitchsdk0invalid0token000000"));
        Assert.Equal(401, (int)tokenError.StatusCode!);
    }

    [LiveTwitchCredentialsFact]
    public async Task AppTokenReadsRealHelixData()
    {
        var clientId = Environment.GetEnvironmentVariable("TWITCH_CLIENT_ID")!;
        var secret = Environment.GetEnvironmentVariable("TWITCH_CLIENT_SECRET")!;
        using var http = new HttpClient();
        var oauth = new TwitchOAuthClient(http);
        var app = await oauth.GetAppTokenAsync(clientId, secret);
        try
        {
            var validation = await oauth.ValidateAsync(app.AccessToken);
            Assert.Equal(clientId, validation.ClientId);
            using var tokens = new RefreshingTokenProvider((_, ct) => oauth.GetAppTokenAsync(clientId, secret, ct), app);
            var helix = new HelixClient(new TwitchHttpClient(http, tokens, new TwitchHttpOptions { ClientId = clientId }));
            var twitchDev = Assert.Single((await helix.GetUsersAsync(new() { Logins = ["twitchdev"] })).Data);
            Assert.Equal(TwitchDevUserId, twitchDev.Id);
            Assert.NotEmpty((await helix.Games.GetTopGamesAsync()).Data);
            Assert.NotEmpty((await helix.Search.SearchCategoriesAsync(new() { Query = "just chatting" })).Data);
            Assert.NotEmpty((await helix.GetStreamsAsync(new() { First = 5 })).Data);
            Assert.Single((await helix.GetChannelInformationAsync([TwitchDevUserId])).Data);
            Assert.NotEmpty((await helix.Chat.GetGlobalEmotesAsync()).Data);
            Assert.NotEmpty((await helix.Chat.GetGlobalChatBadgesAsync()).Data);
            Assert.NotEmpty((await helix.Bits.GetCheermotesAsync()).Data);
            Assert.NotEmpty((await helix.ContentClassification.GetContentClassificationLabelsAsync()).Data);
            await helix.Videos.GetVideosAsync(new() { UserId = TwitchDevUserId });
            await helix.Clips.GetClipsAsync(new() { BroadcasterId = TwitchDevUserId });
            await helix.Teams.GetChannelTeamsAsync(TwitchDevUserId);
            await helix.GetEventSubSubscriptionsAsync();
            await helix.Conduits.GetConduitsAsync();
        }
        finally { await oauth.RevokeAsync(clientId, app.AccessToken); }
    }

    private sealed class UnusableTokenProvider : IAccessTokenProvider
    {
        public ValueTask<AccessToken> GetTokenAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("A public endpoint acquired a token.");
        public ValueTask<AccessToken> RefreshTokenAsync(AccessToken rejectedToken, CancellationToken cancellationToken = default) => GetTokenAsync(cancellationToken);
    }
}

/// <summary>Runs only with TWITCHSDK_LIVE=1 (real network calls to Twitch).</summary>
internal sealed class LiveTwitchFactAttribute : FactAttribute
{
    public LiveTwitchFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TWITCHSDK_LIVE") != "1") Skip = "Set TWITCHSDK_LIVE=1 to run checks against the real Twitch services.";
    }
}

/// <summary>Runs only with TWITCHSDK_LIVE=1 plus TWITCH_CLIENT_ID and TWITCH_CLIENT_SECRET.</summary>
internal sealed class LiveTwitchCredentialsFactAttribute : FactAttribute
{
    public LiveTwitchCredentialsFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TWITCHSDK_LIVE") != "1") Skip = "Set TWITCHSDK_LIVE=1 to run checks against the real Twitch services.";
        else if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TWITCH_CLIENT_ID")) || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TWITCH_CLIENT_SECRET")))
            Skip = "Set TWITCH_CLIENT_ID and TWITCH_CLIENT_SECRET (your own Twitch app) for credentialed live checks.";
    }
}
