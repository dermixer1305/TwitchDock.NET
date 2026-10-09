using TwitchDock.EventSub.Events;

namespace TwitchDock.EventSub;

public static partial class EventSubEvents
{
    public static EventSubEventDefinition<ChannelUpdateEvent> ChannelUpdateV2 { get; } = new("channel.update", "2", EventSubEventsJsonContext.Default.ChannelUpdateEvent);
    public static EventSubEventDefinition<ChannelFollowEvent> ChannelFollowV2 { get; } = new("channel.follow", "2", EventSubEventsJsonContext.Default.ChannelFollowEvent);
    public static EventSubEventDefinition<ChannelAdBreakBeginEvent> ChannelAdBreakBeginV1 { get; } = new("channel.ad_break.begin", "1", EventSubEventsJsonContext.Default.ChannelAdBreakBeginEvent);
    public static EventSubEventDefinition<ChannelRaidEvent> ChannelRaidV1 { get; } = new("channel.raid", "1", EventSubEventsJsonContext.Default.ChannelRaidEvent);
    public static EventSubEventDefinition<ChannelBanEvent> ChannelBanV1 { get; } = new("channel.ban", "1", EventSubEventsJsonContext.Default.ChannelBanEvent);
    public static EventSubEventDefinition<ChannelUnbanEvent> ChannelUnbanV1 { get; } = new("channel.unban", "1", EventSubEventsJsonContext.Default.ChannelUnbanEvent);
    public static EventSubEventDefinition<ChannelUnbanRequestCreateEvent> ChannelUnbanRequestCreateV1 { get; } = new("channel.unban_request.create", "1", EventSubEventsJsonContext.Default.ChannelUnbanRequestCreateEvent);
    public static EventSubEventDefinition<ChannelUnbanRequestResolveEvent> ChannelUnbanRequestResolveV1 { get; } = new("channel.unban_request.resolve", "1", EventSubEventsJsonContext.Default.ChannelUnbanRequestResolveEvent);
    public static EventSubEventDefinition<ChannelModerateEvent> ChannelModerateV1 { get; } = new("channel.moderate", "1", EventSubEventsJsonContext.Default.ChannelModerateEvent);
    public static EventSubEventDefinition<ChannelModerateEventV2> ChannelModerateV2 { get; } = new("channel.moderate", "2", EventSubEventsJsonContext.Default.ChannelModerateEventV2);
    public static EventSubEventDefinition<ChannelModeratorAddEvent> ChannelModeratorAddV1 { get; } = new("channel.moderator.add", "1", EventSubEventsJsonContext.Default.ChannelModeratorAddEvent);
    public static EventSubEventDefinition<ChannelModeratorRemoveEvent> ChannelModeratorRemoveV1 { get; } = new("channel.moderator.remove", "1", EventSubEventsJsonContext.Default.ChannelModeratorRemoveEvent);
    public static EventSubEventDefinition<ChannelVipAddEvent> ChannelVipAddV1 { get; } = new("channel.vip.add", "1", EventSubEventsJsonContext.Default.ChannelVipAddEvent);
    public static EventSubEventDefinition<ChannelVipRemoveEvent> ChannelVipRemoveV1 { get; } = new("channel.vip.remove", "1", EventSubEventsJsonContext.Default.ChannelVipRemoveEvent);
    public static EventSubEventDefinition<ChannelShieldModeBeginEvent> ChannelShieldModeBeginV1 { get; } = new("channel.shield_mode.begin", "1", EventSubEventsJsonContext.Default.ChannelShieldModeBeginEvent);
    public static EventSubEventDefinition<ChannelShieldModeEndEvent> ChannelShieldModeEndV1 { get; } = new("channel.shield_mode.end", "1", EventSubEventsJsonContext.Default.ChannelShieldModeEndEvent);
    public static EventSubEventDefinition<ChannelShoutoutCreateEvent> ChannelShoutoutCreateV1 { get; } = new("channel.shoutout.create", "1", EventSubEventsJsonContext.Default.ChannelShoutoutCreateEvent);
    public static EventSubEventDefinition<ChannelShoutoutReceiveEvent> ChannelShoutoutReceiveV1 { get; } = new("channel.shoutout.receive", "1", EventSubEventsJsonContext.Default.ChannelShoutoutReceiveEvent);
}
