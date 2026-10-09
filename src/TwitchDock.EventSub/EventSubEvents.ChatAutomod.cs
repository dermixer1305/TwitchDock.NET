using TwitchDock.EventSub.Events;

namespace TwitchDock.EventSub;

public static partial class EventSubEvents
{
    public static EventSubEventDefinition<AutomodMessageHoldEvent> AutomodMessageHoldV1 { get; } = new("automod.message.hold", "1", EventSubEventsJsonContext.Default.AutomodMessageHoldEvent);
    public static EventSubEventDefinition<AutomodMessageHoldEventV2> AutomodMessageHoldV2 { get; } = new("automod.message.hold", "2", EventSubEventsJsonContext.Default.AutomodMessageHoldEventV2);
    public static EventSubEventDefinition<AutomodMessageUpdateEvent> AutomodMessageUpdateV1 { get; } = new("automod.message.update", "1", EventSubEventsJsonContext.Default.AutomodMessageUpdateEvent);
    public static EventSubEventDefinition<AutomodMessageUpdateEventV2> AutomodMessageUpdateV2 { get; } = new("automod.message.update", "2", EventSubEventsJsonContext.Default.AutomodMessageUpdateEventV2);
    public static EventSubEventDefinition<AutomodSettingsUpdateEvent> AutomodSettingsUpdateV1 { get; } = new("automod.settings.update", "1", EventSubEventsJsonContext.Default.AutomodSettingsUpdateEvent);
    public static EventSubEventDefinition<AutomodTermsUpdateEvent> AutomodTermsUpdateV1 { get; } = new("automod.terms.update", "1", EventSubEventsJsonContext.Default.AutomodTermsUpdateEvent);
    public static EventSubEventDefinition<ChannelChatClearEvent> ChannelChatClearV1 { get; } = new("channel.chat.clear", "1", EventSubEventsJsonContext.Default.ChannelChatClearEvent);
    public static EventSubEventDefinition<ChannelChatClearUserMessagesEvent> ChannelChatClearUserMessagesV1 { get; } = new("channel.chat.clear_user_messages", "1", EventSubEventsJsonContext.Default.ChannelChatClearUserMessagesEvent);
    public static EventSubEventDefinition<ChannelChatMessageEvent> ChannelChatMessageV1 { get; } = new("channel.chat.message", "1", EventSubEventsJsonContext.Default.ChannelChatMessageEvent);
    public static EventSubEventDefinition<ChannelChatMessageDeleteEvent> ChannelChatMessageDeleteV1 { get; } = new("channel.chat.message_delete", "1", EventSubEventsJsonContext.Default.ChannelChatMessageDeleteEvent);
    public static EventSubEventDefinition<ChannelChatNotificationEvent> ChannelChatNotificationV1 { get; } = new("channel.chat.notification", "1", EventSubEventsJsonContext.Default.ChannelChatNotificationEvent);
    public static EventSubEventDefinition<ChannelChatSettingsUpdateEvent> ChannelChatSettingsUpdateV1 { get; } = new("channel.chat_settings.update", "1", EventSubEventsJsonContext.Default.ChannelChatSettingsUpdateEvent);
    public static EventSubEventDefinition<ChannelChatUserMessageHoldEvent> ChannelChatUserMessageHoldV1 { get; } = new("channel.chat.user_message_hold", "1", EventSubEventsJsonContext.Default.ChannelChatUserMessageHoldEvent);
    public static EventSubEventDefinition<ChannelChatUserMessageUpdateEvent> ChannelChatUserMessageUpdateV1 { get; } = new("channel.chat.user_message_update", "1", EventSubEventsJsonContext.Default.ChannelChatUserMessageUpdateEvent);
    public static EventSubEventDefinition<ChannelSharedChatBeginEvent> ChannelSharedChatBeginV1 { get; } = new("channel.shared_chat.begin", "1", EventSubEventsJsonContext.Default.ChannelSharedChatBeginEvent);
    public static EventSubEventDefinition<ChannelSharedChatUpdateEvent> ChannelSharedChatUpdateV1 { get; } = new("channel.shared_chat.update", "1", EventSubEventsJsonContext.Default.ChannelSharedChatUpdateEvent);
    public static EventSubEventDefinition<ChannelSharedChatEndEvent> ChannelSharedChatEndV1 { get; } = new("channel.shared_chat.end", "1", EventSubEventsJsonContext.Default.ChannelSharedChatEndEvent);
    public static EventSubEventDefinition<ChannelSuspiciousUserMessageEvent> ChannelSuspiciousUserMessageV1 { get; } = new("channel.suspicious_user.message", "1", EventSubEventsJsonContext.Default.ChannelSuspiciousUserMessageEvent);
    public static EventSubEventDefinition<ChannelSuspiciousUserUpdateEvent> ChannelSuspiciousUserUpdateV1 { get; } = new("channel.suspicious_user.update", "1", EventSubEventsJsonContext.Default.ChannelSuspiciousUserUpdateEvent);
    public static EventSubEventDefinition<ChannelWarningAcknowledgeEvent> ChannelWarningAcknowledgeV1 { get; } = new("channel.warning.acknowledge", "1", EventSubEventsJsonContext.Default.ChannelWarningAcknowledgeEvent);
    public static EventSubEventDefinition<ChannelWarningSendEvent> ChannelWarningSendV1 { get; } = new("channel.warning.send", "1", EventSubEventsJsonContext.Default.ChannelWarningSendEvent);
}
