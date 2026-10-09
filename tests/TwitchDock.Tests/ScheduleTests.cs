using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TwitchDock.Core;
using TwitchDock.Helix;
using TwitchDock.Helix.Clients;
using TwitchDock.Helix.Models;

namespace TwitchDock.Tests;

public sealed class ScheduleTests
{
    private static ScheduleClient Client(HttpClient http, AccessToken? token = null) => new HelixClient(new(http,
        new StaticAccessTokenProvider(token ?? new("token")), new() { ClientId = "client", MaxTransientRetries = 0, MaxRateLimitRetries = 0 })).Schedule;
    private static string Fixture(string id) => ContractAssertions.Fixture("helix-schedule.json", id);
    private static readonly DateTimeOffset Start = new(2026, 10, 10, 14, 0, 0, TimeSpan.FromHours(2));

    public static IEnumerable<object[]> Contracts()
    {
        yield return ["get-channel-stream-schedule", HelixJsonContext.Default.ChannelStreamScheduleResponse];
        yield return ["create-channel-stream-schedule-segment", HelixJsonContext.Default.ChannelStreamScheduleResponse];
        yield return ["update-channel-stream-schedule-segment", HelixJsonContext.Default.ChannelStreamScheduleResponse];
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void ResponseContractsPreserveEveryDocumentedField(string id, JsonTypeInfo type)
        => ContractAssertions.Verify("helix-schedule.json", id, type);

    [Fact]
    public async Task ScheduleReadEncodesAllSupportedFiltersAndPreservesNullableFields()
    {
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/schedule", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1&id=s1&id=s2&start_time=2026-10-10T12%3A00%3A00.0000000%2B00%3A00&first=25&after=a%2Bb", request.RequestUri.Query);
            return Task.FromResult(TestHttpHandler.Json(Fixture("get-channel-stream-schedule")));
        }));
        var page = await Client(http, new("app", kind: TwitchTokenKind.App)).GetChannelStreamScheduleAsync(new() { BroadcasterId = "1", Ids = ["s1", "s2"], StartTime = Start, First = 25, After = "a+b" });
        Assert.Equal(2, page.Data.Segments.Count);
        Assert.Equal(page.Data.Segments[0].EndTime, page.Data.Segments[0].CanceledUntil);
        Assert.Null(page.Data.Segments[1].Category);
        Assert.Null(page.Data.Segments[1].CanceledUntil);
        Assert.NotNull(page.Data.Vacation);
    }

    [Fact]
    public async Task EnumerationFlattensSegmentsAndSnapshotsFilters()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal("?broadcaster_id=1&id=s1&first=5&after=" + (++step == 1 ? "initial" : "next"), request.RequestUri!.Query);
            return Task.FromResult(TestHttpHandler.Json(step == 1 ? Fixture("get-channel-stream-schedule") : "{\"data\":{\"segments\":[],\"broadcaster_id\":\"1\",\"broadcaster_name\":\"Example\",\"broadcaster_login\":\"example\",\"vacation\":null},\"pagination\":{}}"));
        }));
        var ids = new List<string> { "s1" };
        var results = Client(http).EnumerateChannelStreamScheduleAsync(new() { BroadcasterId = "1", Ids = ids, First = 5, After = "initial" });
        ids.Clear();
        var count = 0;
        await foreach (var _ in results) count++;
        Assert.Equal(2, count);
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task CalendarIsReadWithoutTokensAndResponseContentIsDisposed()
    {
        const string calendar = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nSUMMARY:Grüße\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var content = new TrackingCalendarContent(calendar);
        var provider = new UnavailableTokenProvider();
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/helix/schedule/icalendar", request.RequestUri!.AbsolutePath);
            Assert.Equal("?broadcaster_id=1", request.RequestUri.Query);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Client-Id"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }));
        var client = new HelixClient(new(http, provider, new() { ClientId = "client" }));
        Assert.Equal(calendar, await client.Schedule.GetChannelICalendarAsync("1"));
        Assert.Equal(0, provider.Calls);
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task PublicCalendarDoesNotRefreshOn401AndStillMapsErrors()
    {
        var provider = new UnavailableTokenProvider();
        using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json("{\"error\":\"Unauthorized\",\"message\":\"upstream rejection\"}", HttpStatusCode.Unauthorized))));
        var client = new HelixClient(new(http, provider, new() { ClientId = "client" }));
        var error = await Assert.ThrowsAsync<TwitchApiException>(() => client.Schedule.GetChannelICalendarAsync("1"));
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task PublicTextRetainsGetRetriesAndCancellation()
    {
        var step = 0;
        var provider = new UnavailableTokenProvider();
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(++step == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : new(HttpStatusCode.OK) { Content = new StringContent("BEGIN:VCALENDAR\r\nEND:VCALENDAR", Encoding.UTF8, "text/calendar") });
        }));
        var client = new HelixClient(new(http, provider, new() { ClientId = "client", FallbackRetryDelay = TimeSpan.FromMilliseconds(1) }));
        Assert.StartsWith("BEGIN:VCALENDAR", await client.Schedule.GetChannelICalendarAsync("1"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Schedule.GetChannelICalendarAsync("1", cts.Token));
        Assert.Equal(2, step);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task VacationSettingsUseQueryOnlyAndPreserveExplicitDisable()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("/helix/schedule/settings", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Content);
            Assert.Equal(++step == 1 ? "?broadcaster_id=1&is_vacation_enabled=true&vacation_start_time=2026-10-10T12%3A00%3A00.0000000%2B00%3A00&vacation_end_time=2026-10-11T12%3A00%3A00.0000000%2B00%3A00&timezone=Europe%2FBerlin" : "?broadcaster_id=1&is_vacation_enabled=false", request.RequestUri.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }));
        var client = Client(http);
        await client.UpdateChannelStreamScheduleAsync(new() { BroadcasterId = "1", IsVacationEnabled = true, VacationStartTime = Start, VacationEndTime = Start.AddDays(1), Timezone = "Europe/Berlin" });
        await client.UpdateChannelStreamScheduleAsync(new() { BroadcasterId = "1", IsVacationEnabled = false });
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task SegmentCreateAndPatchUseStringDurationsAndOnlySupportedBodyFields()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal(++step == 1 ? HttpMethod.Post : HttpMethod.Patch, request.Method);
            Assert.Equal("/helix/schedule/segment", request.RequestUri!.AbsolutePath);
            Assert.Equal(step == 1 ? "?broadcaster_id=1" : "?broadcaster_id=1&id=s1", request.RequestUri.Query);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var body = json.RootElement;
            Assert.Equal(6, body.EnumerateObject().Count());
            Assert.Equal(Start, body.GetProperty("start_time").GetDateTimeOffset());
            Assert.Equal("Europe/Berlin", body.GetProperty("timezone").GetString());
            Assert.Equal("60", body.GetProperty("duration").GetString());
            Assert.Equal("category1", body.GetProperty("category_id").GetString());
            Assert.Equal("Stream", body.GetProperty("title").GetString());
            Assert.False(body.GetProperty(step == 1 ? "is_recurring" : "is_canceled").GetBoolean());
            Assert.False(body.TryGetProperty("broadcaster_id", out _));
            return TestHttpHandler.Json(Fixture(step == 1 ? "create-channel-stream-schedule-segment" : "update-channel-stream-schedule-segment"));
        }));
        var client = Client(http);
        await client.CreateChannelStreamScheduleSegmentAsync("1", new() { StartTime = Start, Timezone = "Europe/Berlin", Duration = 60, IsRecurring = false, CategoryId = "category1", Title = "Stream" });
        await client.UpdateChannelStreamScheduleSegmentAsync("1", "s1", new() { StartTime = Start, Timezone = "Europe/Berlin", Duration = 60, IsCanceled = false, CategoryId = "category1", Title = "Stream" });
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task SingleFieldSegmentUpdateAndDeletionOmitUnrelatedValues()
    {
        var step = 0;
        using var http = new HttpClient(new TestHttpHandler(async (request, ct) =>
        {
            Assert.Equal("?broadcaster_id=1&id=s1", request.RequestUri!.Query);
            if (++step == 1)
            {
                Assert.Equal(HttpMethod.Patch, request.Method);
                Assert.Equal("{\"is_canceled\":true}", await request.Content!.ReadAsStringAsync(ct));
                return TestHttpHandler.Json(Fixture("update-channel-stream-schedule-segment"));
            }
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Null(request.Content);
            return new(HttpStatusCode.NoContent);
        }));
        await Client(http).UpdateChannelStreamScheduleSegmentAsync("1", "s1", new() { IsCanceled = true });
        await Client(http).DeleteChannelStreamScheduleSegmentAsync("1", "s1");
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task ScheduleMutationsCheckScopeKindAndOwner()
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => throw new InvalidOperationException("No request expected")));
        foreach (var token in new AccessToken[] { new("app", kind: TwitchTokenKind.App), new("user", scopes: [], kind: TwitchTokenKind.User), new("other", scopes: [TwitchScopes.ChannelManageSchedule], kind: TwitchTokenKind.User, userId: "other") })
            foreach (var operation in Mutations(Client(http, token))) await Assert.ThrowsAsync<TwitchAuthorizationException>(operation);
    }

    [Fact]
    public async Task ScheduleValidationCoversLimitsAndVacationDependencies()
    {
        var calls = 0;
        using var http = new HttpClient(new TestHttpHandler((_, _) => { calls++; return Task.FromResult(TestHttpHandler.Json(Fixture("create-channel-stream-schedule-segment"))); }));
        var client = Client(http);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetChannelStreamScheduleAsync(new() { BroadcasterId = "1", First = 26 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetChannelStreamScheduleAsync(new() { BroadcasterId = "1", Ids = Enumerable.Repeat("s1", 101).ToArray() }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateChannelStreamScheduleAsync(new() { BroadcasterId = "1", IsVacationEnabled = true }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateChannelStreamScheduleAsync(new() { BroadcasterId = "1", VacationStartTime = Start, VacationEndTime = Start }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.CreateChannelStreamScheduleSegmentAsync("1", new() { StartTime = Start, Timezone = "Europe/Berlin", Duration = 29 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.UpdateChannelStreamScheduleSegmentAsync("1", "s1", new() { Duration = 1381 }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateChannelStreamScheduleSegmentAsync("1", "s1", new() { Title = new('x', 141) }));
        await client.CreateChannelStreamScheduleSegmentAsync("1", new() { StartTime = Start, Timezone = "Europe/Berlin", Duration = 1380, Title = string.Concat(Enumerable.Repeat("😀", 140)) });
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task AllScheduleErrorsRetainStatus(HttpStatusCode status)
    {
        using var http = new HttpClient(new TestHttpHandler((_, _) => Task.FromResult(TestHttpHandler.Json("{\"error\":\"Schedule error\",\"message\":\"schedule unavailable\"}", status))));
        var client = Client(http);
        Func<Task>[] calls = [() => client.GetChannelStreamScheduleAsync(new() { BroadcasterId = "1" }), () => client.GetChannelICalendarAsync("1"), .. Mutations(client)];
        foreach (var call in calls) Assert.Equal(status, (await Assert.ThrowsAsync<TwitchApiException>(call)).StatusCode);
    }

    private static Func<Task>[] Mutations(ScheduleClient client) =>
    [
        () => client.UpdateChannelStreamScheduleAsync(new() { BroadcasterId = "1", IsVacationEnabled = false }),
        () => client.CreateChannelStreamScheduleSegmentAsync("1", new() { StartTime = Start, Timezone = "Europe/Berlin", Duration = 30 }),
        () => client.UpdateChannelStreamScheduleSegmentAsync("1", "s1", new() { IsCanceled = true }),
        () => client.DeleteChannelStreamScheduleSegmentAsync("1", "s1")
    ];

    private sealed class UnavailableTokenProvider : IAccessTokenProvider
    {
        public int Calls { get; private set; }
        public ValueTask<AccessToken> GetTokenAsync(CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException("No credential available"); }
        public ValueTask<AccessToken> RefreshTokenAsync(AccessToken rejectedToken, CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException("No credential available"); }
    }

    private sealed class TrackingCalendarContent(string text) : StringContent(text, Encoding.UTF8, "text/calendar")
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
