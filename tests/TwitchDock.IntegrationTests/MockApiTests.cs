using System.Globalization;
using System.Text.Json;
using TwitchDock.Core;
using TwitchDock.Helix;

namespace TwitchDock.IntegrationTests;

public sealed class MockApiTests
{
    // The CLI 1.1 mock rejects scopes added after it (user:read:emotes, channel:read:ads, moderator:read:chat_settings).
    private static readonly string[] Scopes =
    [
        "user:read:email", "user:read:follows", "user:read:blocked_users", "user:read:subscriptions", "user:read:broadcast",
        "channel:read:subscriptions", "channel:read:polls", "channel:read:predictions", "channel:read:redemptions", "channel:read:editors", "channel:read:goals",
        "channel:read:hype_train", "channel:read:charity", "channel:read:vips", "channel:read:stream_key", "channel:manage:broadcast",
        "moderation:read", "moderator:read:chatters", "moderator:read:followers", "moderator:read:blocked_terms",
        "moderator:read:shield_mode", "moderator:read:automod_settings", "moderator:manage:banned_users", "bits:read", "analytics:read:extensions",
        "analytics:read:games", "clips:edit",
    ];

    [TwitchCliFact]
    public async Task SdkModelsReadMockApiResponses()
    {
        using var cliLock = await TwitchCli.LockAsync();
        var port = TwitchCli.FreePort();
        using var server = TwitchCli.StartServer("mock-api", "start", "--port", port.ToString(CultureInfo.InvariantCulture));
        using var raw = new HttpClient { BaseAddress = new($"http://localhost:{port}/"), Timeout = TimeSpan.FromSeconds(30) };
        using var clients = await WaitForJsonAsync(raw, "units/clients", server);
        var client = clients.RootElement.GetProperty("data")[0];
        var (clientId, secret) = (client.GetProperty("ID").GetString()!, client.GetProperty("Secret").GetString()!);
        using var users = JsonDocument.Parse(await raw.GetStringAsync("units/users"));
        var broadcaster = users.RootElement.GetProperty("data").EnumerateArray().First(u => u.GetProperty("broadcaster_type").GetString() == "partner").GetProperty("id").GetString()!;
        var scope = Uri.EscapeDataString(string.Join(' ', Scopes));
        using var tokenResponse = await raw.PostAsync($"auth/authorize?client_id={clientId}&client_secret={secret}&grant_type=user_token&user_id={broadcaster}&scope={scope}", null);
        tokenResponse.EnsureSuccessStatusCode();
        using var token = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());

        using var http = new HttpClient();
        var helix = new HelixClient(new TwitchHttpClient(http,
            new StaticAccessTokenProvider(new AccessToken(token.RootElement.GetProperty("access_token").GetString()!, scopes: Scopes, kind: TwitchTokenKind.User, userId: broadcaster, clientId: clientId)),
            new TwitchHttpOptions { ClientId = clientId, BaseAddress = new($"http://localhost:{port}/mock/") }));

        var calls = new (string Name, Func<Task<int>> Call)[]
        {
            ("users", async () => (await helix.GetUsersAsync(new() { Ids = [broadcaster] })).Data.Count),
            ("streams", async () => (await helix.GetStreamsAsync()).Data.Count),
            ("channels", async () => (await helix.GetChannelInformationAsync([broadcaster])).Data.Count),
            ("top games", async () => (await helix.Games.GetTopGamesAsync()).Data.Count),
            ("search categories", async () => (await helix.Search.SearchCategoriesAsync(new() { Query = "a" })).Data.Count),
            ("search channels", async () => (await helix.Search.SearchChannelsAsync(new() { Query = "a" })).Data.Count),
            ("subscriptions", async () => (await helix.Subscriptions.GetBroadcasterSubscriptionsAsync(new() { BroadcasterId = broadcaster })).Data.Count),
            ("polls", async () => (await helix.Polls.GetPollsAsync(new() { BroadcasterId = broadcaster })).Data.Count),
            ("predictions", async () => (await helix.Predictions.GetPredictionsAsync(new() { BroadcasterId = broadcaster })).Data.Count),
            ("custom rewards", async () => (await helix.ChannelPoints.GetCustomRewardsAsync(new() { BroadcasterId = broadcaster })).Data.Count),
            ("moderators", async () => (await helix.Moderation.GetModeratorsAsync(new() { BroadcasterId = broadcaster })).Data.Count),
            ("banned users", async () => (await helix.Moderation.GetBannedUsersAsync(new() { BroadcasterId = broadcaster })).Data.Count),
            ("vips", async () => (await helix.Moderation.GetVipsAsync(new() { BroadcasterId = broadcaster })).Data.Count),
            ("chat settings", async () => (await helix.Chat.GetChatSettingsAsync(broadcaster)).Data.Count),
            ("channel emotes", async () => (await helix.Chat.GetChannelEmotesAsync(broadcaster)).Data.Count),
            ("global emotes", async () => (await helix.Chat.GetGlobalEmotesAsync()).Data.Count),
            ("channel badges", async () => (await helix.Chat.GetChannelChatBadgesAsync(broadcaster)).Data.Count),
            ("global badges", async () => (await helix.Chat.GetGlobalChatBadgesAsync()).Data.Count),
            ("schedule", async () => (await helix.Schedule.GetChannelStreamScheduleAsync(new() { BroadcasterId = broadcaster })).Data.Segments?.Count ?? 0),
            ("videos", async () => (await helix.Videos.GetVideosAsync(new() { UserId = broadcaster })).Data.Count),
            ("channel teams", async () => (await helix.Teams.GetChannelTeamsAsync(broadcaster)).Data.Count),
            ("editors", async () => (await helix.Channels.GetChannelEditorsAsync(broadcaster)).Data.Count),
            ("followers", async () => (await helix.Channels.GetChannelFollowersAsync(new() { BroadcasterId = broadcaster })).Data.Count),
            ("followed channels", async () => (await helix.Channels.GetFollowedChannelsAsync(new() { UserId = broadcaster })).Data.Count),
            ("cheermotes", async () => (await helix.Bits.GetCheermotesAsync(broadcaster)).Data.Count),
            ("bits leaderboard", async () => (await helix.Bits.GetBitsLeaderboardAsync()).Data.Count),
            ("creator goals", async () => (await helix.Goals.GetCreatorGoalsAsync(broadcaster)).Data.Count),
            ("charity campaign", async () => (await helix.Charity.GetCharityCampaignAsync(broadcaster)).Data.Count),
            ("stream markers", async () => (await helix.Streams.GetStreamMarkersAsync(new() { UserId = broadcaster })).Data.Count),
        };
        // Not called: clips (the CLI 1.1 mock sends non-RFC 3339 created_at values) and EventSub subscriptions (not served by the mock API).

        var failures = new List<string>();
        var nonEmpty = 0;
        foreach (var (name, call) in calls)
        {
            try { if (await call() > 0) nonEmpty++; }
            catch (Exception ex) when (ex is JsonException or TwitchApiException or TwitchAuthorizationException or ArgumentException)
            {
                failures.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.True(nonEmpty >= 15, $"Only {nonEmpty} endpoints returned data.");
    }

    private static async Task<JsonDocument> WaitForJsonAsync(HttpClient http, string path, CliServer server)
    {
        // The first start generates the mock database, which can take a minute.
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        while (true)
        {
            try { return JsonDocument.Parse(await http.GetStringAsync(path, deadline.Token)); }
            catch (HttpRequestException) { await Task.Delay(500, deadline.Token); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { throw new TimeoutException("The mock API did not start: " + server.Output); }
        }
    }
}
