using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TwitchSdk.Authentication;
using TwitchSdk.Chat;
using TwitchSdk.Core;
using TwitchSdk.DependencyInjection;
using TwitchSdk.EventSub;
using TwitchSdk.Helix;
using TwitchSdk.Helix.Models;

if (JsonSerializer.IsReflectionEnabledByDefault) throw new InvalidOperationException("Reflection must be disabled for this smoke test.");
using var http = new HttpClient(new FakeHandler());
var oauth = new TwitchOAuthClient(http);
var token = await oauth.GetAppTokenAsync("fake-client", "fake-secret");
var helix = new HelixClient(new TwitchHttpClient(http, new StaticAccessTokenProvider(new(token.AccessToken)), new() { ClientId = "fake-client" }));
var page = await helix.GetUsersAsync(new() { Ids = ["1"] });
if (page.Data.Single().DisplayName != "Tester") throw new InvalidOperationException("User contract failed.");
var games = await helix.Games.GetGamesAsync(new() { Ids = ["1"] });
if (games.Data.Single().Name != "Example Game") throw new InvalidOperationException("Games contract failed.");
await helix.Channels.ModifyChannelInformationAsync(new() { BroadcasterId = "1", IsBrandedContent = false });
var followers = await helix.Channels.GetChannelFollowersAsync(new() { BroadcasterId = "1" });
if (followers.Total != 42 || followers.Data.Count != 0) throw new InvalidOperationException("Follower total contract failed.");
var markers = await helix.Streams.GetStreamMarkersAsync(new() { VideoId = "video1" });
if (markers.Data.Single().Videos.Single().Markers.Single().PositionSeconds != 60) throw new InvalidOperationException("Marker grouping contract failed.");
var subscriberPage = await helix.Subscriptions.GetBroadcasterSubscriptionsAsync(new() { BroadcasterId = "1", UserIds = ["2"] });
if (subscriberPage.Points is not null || subscriberPage.Total is not null) throw new InvalidOperationException("Nullable subscription totals failed.");
var transactions = await helix.Bits.GetExtensionTransactionsAsync(new() { ExtensionId = "fake-client" });
if (!transactions.Data.Single().ProductData.InDevelopment) throw new InvalidOperationException("Extension product casing contract failed.");
var reward = await helix.ChannelPoints.CreateCustomRewardAsync("1", new() { Title = "Reward", Cost = 1, IsEnabled = false });
if (reward.Data.Single().DefaultImage.Url1x != "https://example.org/1") throw new InvalidOperationException("Reward image contract failed.");
await helix.ChannelPoints.UpdateRedemptionStatusAsync(new() { BroadcasterId = "1", RewardId = "r1", Ids = ["d1"], Status = "FULFILLED" });
var poll = await helix.Polls.CreatePollAsync(new() { BroadcasterId = "1", Title = "Poll", Choices = [new() { Title = "A" }, new() { Title = "B" }], Duration = 15 });
if (poll.Data.Single().EndedAt is not null) throw new InvalidOperationException("Active poll contract failed.");
var prediction = await helix.Predictions.CreatePredictionAsync(new() { BroadcasterId = "1", Title = "Prediction", Outcomes = [new() { Title = "A" }, new() { Title = "B" }], PredictionWindow = 30 });
if (prediction.Data.Single().WinningOutcomeId is not null) throw new InvalidOperationException("Active prediction contract failed.");
var userExtensions = await helix.Users.UpdateUserExtensionsAsync(new()
{
    Data = new() { Component = new Dictionary<string, UserComponentExtensionActivation> { ["1"] = new() { Active = false } } }
});
if (userExtensions.Data.Component["1"].Active || userExtensions.Data.Component["1"].X is not null) throw new InvalidOperationException("Inactive extension slot contract failed.");
await helix.Whispers.SendWhisperAsync(new() { FromUserId = "1", ToUserId = "2", Message = "Test" });
var calendar = await helix.Schedule.GetChannelICalendarAsync("1");
if (!calendar.StartsWith("BEGIN:VCALENDAR", StringComparison.Ordinal)) throw new InvalidOperationException("Calendar text contract failed.");
await helix.Schedule.CreateChannelStreamScheduleSegmentAsync("1", new() { StartTime = DateTimeOffset.Parse("2026-10-10T12:00:00Z"), Timezone = "Europe/Berlin", Duration = 60 });
var conduit = await helix.Conduits.CreateConduitAsync(new() { ShardCount = 2 });
if (conduit.Data.Single().ShardCount != 2) throw new InvalidOperationException("Conduit contract failed.");
var shards = await helix.Conduits.UpdateConduitShardsAsync(new() { ConduitId = "c1", Shards = [new() { Id = "0", Transport = new() { Method = "websocket", SessionId = "session1" } }] });
if (shards.Errors.Single().Code != "invalid_parameter") throw new InvalidOperationException("Conduit shard error contract failed.");
var subscriptions = await helix.CreateEventSubSubscriptionAsync(new()
{
    Type = "stream.online", Version = "1", Condition = new Dictionary<string, string> { ["broadcaster_user_id"] = "1" },
    Transport = new() { Method = "websocket", SessionId = "session1" }
});
if (subscriptions.Data.Single().Transport.SessionId != "session1") throw new InvalidOperationException("Subscription contract failed.");
var chatNotification = EventSubMessage.Parse("""
    {"metadata":{"message_id":"n","message_type":"notification","message_timestamp":"2026-10-09T12:00:00Z","subscription_type":"channel.chat.message","subscription_version":"1"},
     "payload":{"subscription":{"id":"s","status":"enabled","type":"channel.chat.message","version":"1","condition":{"broadcaster_user_id":"1","user_id":"2"},"transport":{"method":"websocket","session_id":"session1"}},
     "event":{"broadcaster_user_id":"1","broadcaster_user_login":"channel","broadcaster_user_name":"Channel",
     "chatter_user_id":"2","chatter_user_login":"tester","chatter_user_name":"Tester","message_id":"m","message":{"text":"hello","fragments":[]},
     "message_type":"text","color":"","badges":[]}}}
    """u8);
if (!TwitchChatClient.TryReadMessage(chatNotification, out var chat) || chat.Message.Text != "hello") throw new InvalidOperationException("Chat contract failed.");
var message = EventSubMessage.Parse("""
    {"metadata":{"message_id":"m","message_type":"session_keepalive","message_timestamp":"2026-10-09T12:00:00Z"},"payload":{}}
    """u8);
if (message.Metadata.MessageType != "session_keepalive") throw new InvalidOperationException("EventSub contract failed.");
var services = new ServiceCollection();
services.AddTwitchSdk(new() { ClientId = "fake-client" }, _ => new StaticAccessTokenProvider(new("fake-token")));
using var provider = services.BuildServiceProvider();
_ = provider.GetRequiredService<TwitchChatClient>();
Console.WriteLine("Packed SDK smoke test passed with JSON reflection disabled.");

internal sealed class FakeHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Patch && request.RequestUri!.AbsolutePath == "/helix/channels")
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        if (request.RequestUri!.AbsolutePath == "/helix/whispers")
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        if (request.RequestUri.AbsolutePath == "/helix/schedule/icalendar")
        {
            if (request.Headers.Authorization is not null || request.Headers.Contains("Client-Id")) throw new InvalidOperationException("Public calendar acquired SDK credentials.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n", Encoding.UTF8, "text/calendar") });
        }
        var json = request.RequestUri!.AbsolutePath switch
        {
            "/oauth2/token" => """{"access_token":"fake-token","expires_in":3600,"token_type":"bearer"}""",
            "/helix/games" => """{"data":[{"id":"1","name":"Example Game","box_art_url":"https://example.org/art","igdb_id":""}]}""",
            "/helix/channels/followers" => """{"data":[],"total":42,"pagination":{}}""",
            "/helix/users/extensions" => """{"data":{"panel":{},"overlay":{},"component":{"1":{"active":false}}}}""",
            "/helix/schedule/segment" => """{"data":{"segments":[],"broadcaster_id":"1","broadcaster_name":"Channel","broadcaster_login":"channel","vacation":null}}""",
            "/helix/eventsub/conduits" => """{"data":[{"id":"c1","shard_count":2}]}""",
            "/helix/eventsub/conduits/shards" => """{"data":[],"errors":[{"id":"0","message":"Session disconnected","code":"invalid_parameter"}]}""",
            "/helix/channel_points/custom_rewards" => """{"data":[{"broadcaster_id":"1","broadcaster_login":"channel","broadcaster_name":"Channel","id":"r1","title":"Reward","background_color":"#9147FF","default_image":{"url_1x":"https://example.org/1","url_2x":"https://example.org/2","url_4x":"https://example.org/4"}}]}""",
            "/helix/channel_points/custom_rewards/redemptions" => """{"data":[]}""",
            "/helix/polls" => """{"data":[{"id":"p1","broadcaster_id":"1","broadcaster_login":"channel","broadcaster_name":"Channel","title":"Poll","status":"ACTIVE","ended_at":null}]}""",
            "/helix/predictions" => """{"data":[{"id":"p1","broadcaster_id":"1","broadcaster_login":"channel","broadcaster_name":"Channel","title":"Prediction","status":"ACTIVE","winning_outcome_id":null}]}""",
            "/helix/streams/markers" => """{"data":[{"user_id":"1","user_name":"Creator","user_login":"creator","videos":[{"video_id":"video1","markers":[{"id":"m1","position_seconds":60,"url":"https://example.org/marker"}]}]}]}""",
            "/helix/subscriptions" => """{"data":[],"points":null,"total":null}""",
            "/helix/extensions/transactions" => """{"data":[{"id":"t1","broadcaster_id":"1","broadcaster_login":"channel","broadcaster_name":"Channel","user_id":"2","user_login":"buyer","user_name":"Buyer","product_type":"BITS_IN_EXTENSION","product_data":{"sku":"sku1","domain":"twitch.ext.fake-client","cost":{"amount":1,"type":"bits"},"inDevelopment":true,"displayName":"Test"}}]}""",
            "/helix/eventsub/subscriptions" => """{"data":[{"id":"sub1","status":"enabled","type":"stream.online","version":"1","condition":{"broadcaster_user_id":"1"},"transport":{"method":"websocket","session_id":"session1"}}]}""",
            _ => """{"data":[{"id":"1","login":"tester","display_name":"Tester"}]}"""
        };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
