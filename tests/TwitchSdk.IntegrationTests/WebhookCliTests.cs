using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using TwitchSdk.EventSub;

namespace TwitchSdk.IntegrationTests;

public sealed class WebhookCliTests
{
    private const string Secret = "integration-secret-0123";

    /// <summary>Types the Twitch CLI 1.1 can trigger (twitch event trigger --help).</summary>
    private static readonly HashSet<string> CliTypes =
    [
        "channel.ad_break.begin", "channel.ban", "channel.channel_points_custom_reward.add", "channel.channel_points_custom_reward.remove",
        "channel.channel_points_custom_reward.update", "channel.channel_points_custom_reward_redemption.add", "channel.channel_points_custom_reward_redemption.update",
        "channel.charity_campaign.donate", "channel.charity_campaign.progress", "channel.charity_campaign.start", "channel.charity_campaign.stop", "channel.cheer",
        "channel.follow", "channel.goal.begin", "channel.goal.end", "channel.goal.progress", "channel.hype_train.begin", "channel.hype_train.end",
        "channel.hype_train.progress", "channel.moderator.add", "channel.moderator.remove", "channel.poll.begin", "channel.poll.end", "channel.poll.progress",
        "channel.prediction.begin", "channel.prediction.end", "channel.prediction.lock", "channel.prediction.progress", "channel.raid", "channel.shield_mode.begin",
        "channel.shield_mode.end", "channel.shoutout.create", "channel.shoutout.receive", "channel.subscribe", "channel.subscription.end", "channel.subscription.gift",
        "channel.subscription.message", "channel.unban", "channel.unban_request.create", "channel.unban_request.resolve", "channel.update", "drop.entitlement.grant",
        "extension.bits_transaction.create", "stream.offline", "stream.online", "user.authorization.grant", "user.authorization.revoke", "user.update",
    ];

    [TwitchCliFact]
    public async Task CliSignedNotificationsVerifyAndDeserializeIntoTypedEvents()
    {
        using var cliLock = await TwitchCli.LockAsync();
        var port = TwitchCli.FreePort();
        var outcomes = new ConcurrentDictionary<string, string>();
        var verifier = new EventSubWebhookVerifier(Secret);
        using var listener = Listen(port, (request, body) =>
        {
            var payload = verifier.VerifyAndParse(request.Headers["Twitch-Eventsub-Message-Id"]!, request.Headers["Twitch-Eventsub-Message-Timestamp"]!,
                request.Headers["Twitch-Eventsub-Message-Signature"]!, body);
            var key = $"{payload.Subscription!.Type}@{payload.Subscription.Version}";
            try
            {
                if (!EventSubEvents.TryGetDefinition(payload.Subscription.Type, payload.Subscription.Version, out var definition)) outcomes[key] = "unregistered";
                else
                {
                    definition.Deserialize(payload.Event.ValueKind != JsonValueKind.Undefined ? payload.Event : payload.Events);
                    outcomes[key] = "ok";
                }
            }
            catch (JsonException ex) { outcomes[key] = "deserialization failed: " + ex.Message; }
            return Task.FromResult<(int, string?)>((204, null));
        });

        var unsupported = new List<string>();
        foreach (var definition in EventSubEvents.All.Where(d => CliTypes.Contains(d.Type)).OrderBy(d => d.ToString(), StringComparer.Ordinal))
        {
            var (exitCode, output) = await TwitchCli.RunAsync(TimeSpan.FromSeconds(60), "event", "trigger", definition.Type, "-v", definition.Version,
                "-F", $"http://localhost:{port}/eventsub", "-s", Secret);
            if (!outcomes.ContainsKey($"{definition.Type}@{definition.Version}")) unsupported.Add($"{definition} (exit {exitCode}): {output.Trim().Split('\n')[0]}");
        }

        var failures = outcomes.Where(o => o.Value != "ok").Select(o => $"{o.Key}: {o.Value}").ToList();
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        // The CLI lags behind some versions; everything it can generate must deserialize, and it must cover most types.
        Assert.True(outcomes.Count >= 40, $"Only {outcomes.Count} types were delivered. Not generated: {string.Join("; ", unsupported)}");
    }

    [TwitchCliFact]
    public async Task CliCallbackVerificationReceivesTheChallenge()
    {
        using var cliLock = await TwitchCli.LockAsync();
        var port = TwitchCli.FreePort();
        var handler = new EventSubWebhookHandler(new EventSubWebhookVerifier(Secret), new EventSubEventRouter());
        using var listener = Listen(port, async (request, body) =>
        {
            var response = await handler.HandleAsync(EventSubWebhookRequest.FromHeaders(name => request.Headers[name], body));
            return (response.StatusCode, response.Body);
        });
        // CLI 1.1.24 can panic in its own update check after finishing, so assert on its verdict lines instead of the exit code.
        var (_, output) = await TwitchCli.RunAsync(TimeSpan.FromSeconds(60), "event", "verify-subscription", "stream.online",
            "-F", $"http://localhost:{port}/eventsub", "-s", Secret);
        Assert.True(output.Contains("Valid response", StringComparison.Ordinal) && output.Contains("Valid status code", StringComparison.Ordinal), output);
    }

    private static HttpListener Listen(int port, Func<HttpListenerRequest, byte[], Task<(int Status, string? Body)>> handle)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }
                using var buffer = new MemoryStream();
                await context.Request.InputStream.CopyToAsync(buffer);
                int status;
                string? body;
                try { (status, body) = await handle(context.Request, buffer.ToArray()); }
                catch (Exception ex) { (status, body) = (500, ex.Message); }
                context.Response.StatusCode = status;
                if (body is not null)
                {
                    var bytes = Encoding.UTF8.GetBytes(body);
                    context.Response.ContentType = "text/plain";
                    await context.Response.OutputStream.WriteAsync(bytes);
                }
                context.Response.Close();
            }
        });
        return listener;
    }
}
