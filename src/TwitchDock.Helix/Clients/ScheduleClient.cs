using System.Text;
using System.Text.Json;
using TwitchDock.Core;
using TwitchDock.Helix.Models;

namespace TwitchDock.Helix.Clients;

public sealed class ScheduleClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public Task<ChannelStreamScheduleResponse> GetChannelStreamScheduleAsync(GetChannelStreamScheduleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        if (request.First is < 1 or > 25) throw new ArgumentOutOfRangeException(nameof(request), "Schedule page size must be between 1 and 25.");
        return _transport.SendAsync(HttpMethod.Get, "schedule", HelixJsonContext.Default.ChannelStreamScheduleResponse,
            new HelixQuery().AddValue("broadcaster_id", request.BroadcasterId).AddValues("id", request.Ids)
                .AddValue("start_time", request.StartTime).AddPage(request.First, request.After), cancellationToken: cancellationToken);
    }

    /// <summary>Returns calendar text unchanged. This public endpoint does not acquire or attach SDK credentials.</summary>
    public Task<string> GetChannelICalendarAsync(string broadcasterId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        return _transport.SendTextAsync(HttpMethod.Get, "schedule/icalendar", new HelixQuery().AddValue("broadcaster_id", broadcasterId),
            authenticated: false, cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<ScheduleSegment> EnumerateChannelStreamScheduleAsync(GetChannelStreamScheduleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Ids);
        var snapshot = request with { Ids = request.Ids.ToArray() };
        return HelixPagination.EnumerateAsync(async (cursor, ct) =>
        {
            var page = await GetChannelStreamScheduleAsync(snapshot with { After = cursor ?? snapshot.After }, ct).ConfigureAwait(false);
            return new HelixPage<ScheduleSegment> { Data = page.Data.Segments, Pagination = page.Pagination };
        }, cancellationToken);
    }

    public Task UpdateChannelStreamScheduleAsync(UpdateChannelStreamScheduleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BroadcasterId);
        if (request.IsVacationEnabled == true && (!request.VacationStartTime.HasValue || !request.VacationEndTime.HasValue || string.IsNullOrWhiteSpace(request.Timezone)))
            throw new ArgumentException("Enabling vacation requires its start, end and IANA timezone.", nameof(request));
        if (request.VacationStartTime.HasValue && request.VacationEndTime <= request.VacationStartTime) throw new ArgumentException("Vacation end must be later than its start.", nameof(request));
        if (request.Timezone is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.Timezone);
        return _transport.SendAsync(HttpMethod.Patch, "schedule/settings",
            new HelixQuery().AddValue("broadcaster_id", request.BroadcasterId).AddValue("is_vacation_enabled", request.IsVacationEnabled)
                .AddValue("vacation_start_time", request.VacationStartTime).AddValue("vacation_end_time", request.VacationEndTime).AddValue("timezone", request.Timezone),
            authorization: Manage(request.BroadcasterId), cancellationToken: cancellationToken);
    }

    public Task<ChannelStreamScheduleResponse> CreateChannelStreamScheduleSegmentAsync(string broadcasterId, CreateScheduleSegmentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Timezone);
        ValidateSegment(request.Duration, request.Title);
        return _transport.SendAsync(HttpMethod.Post, "schedule/segment", HelixJsonContext.Default.ChannelStreamScheduleResponse,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId),
            JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.CreateScheduleSegmentRequest),
            authorization: Manage(broadcasterId), cancellationToken: cancellationToken);
    }

    public Task<ChannelStreamScheduleResponse> UpdateChannelStreamScheduleSegmentAsync(string broadcasterId, string segmentId, UpdateScheduleSegmentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(segmentId);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Timezone is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.Timezone);
        ValidateSegment(request.Duration, request.Title);
        return _transport.SendAsync(HttpMethod.Patch, "schedule/segment", HelixJsonContext.Default.ChannelStreamScheduleResponse,
            new HelixQuery().AddValue("broadcaster_id", broadcasterId).AddValue("id", segmentId),
            JsonSerializer.SerializeToUtf8Bytes(request, HelixJsonContext.Default.UpdateScheduleSegmentRequest),
            authorization: Manage(broadcasterId), cancellationToken: cancellationToken);
    }

    public Task DeleteChannelStreamScheduleSegmentAsync(string broadcasterId, string segmentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(broadcasterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(segmentId);
        return _transport.SendAsync(HttpMethod.Delete, "schedule/segment", new HelixQuery().AddValue("broadcaster_id", broadcasterId).AddValue("id", segmentId),
            authorization: Manage(broadcasterId), cancellationToken: cancellationToken);
    }

    private static TwitchAuthorizationRequirement Manage(string broadcasterId) => new([TwitchScopes.ChannelManageSchedule], requiredUserId: broadcasterId);
    private static void ValidateSegment(int? duration, string? title)
    {
        if (duration is < 30 or > 1380) throw new ArgumentOutOfRangeException(nameof(duration), "Segment duration must be between 30 and 1380 minutes.");
        if (title?.EnumerateRunes().Count() > 140) throw new ArgumentException("Segment title may contain at most 140 Unicode code points.", nameof(title));
    }
}
