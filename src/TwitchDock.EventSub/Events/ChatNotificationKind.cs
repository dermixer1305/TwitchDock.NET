namespace TwitchDock.EventSub.Events;

/// <summary>
/// The known <c>notice_type</c> values of channel.chat.notification, read through <see cref="ChannelChatNotificationEvent.Kind"/>.
/// <c>shared_chat_*</c> types map to the same kind with <see cref="ChannelChatNotificationEvent.IsSharedChat"/> set. A value Twitch
/// adds later maps to <see cref="Unknown"/>; <see cref="ChannelChatNotificationEvent.NoticeType"/> still holds its text.
/// </summary>
public enum ChatNotificationKind
{
    /// <summary>A notice type this SDK version does not know, or Twitch's own <c>unknown</c>.</summary>
    Unknown = 0,
    /// <summary><c>sub</c>: a new subscription; details in <see cref="ChannelChatNotificationEvent.Sub"/>.</summary>
    Sub,
    /// <summary><c>resub</c>: a renewed subscription; details in <see cref="ChannelChatNotificationEvent.Resub"/>.</summary>
    Resub,
    /// <summary><c>sub_gift</c>: a subscription gifted to one recipient; details in <see cref="ChannelChatNotificationEvent.SubGift"/>.</summary>
    SubGift,
    /// <summary><c>community_sub_gift</c>: gifted subscriptions to the community; details in <see cref="ChannelChatNotificationEvent.CommunitySubGift"/>.</summary>
    CommunitySubGift,
    /// <summary><c>gift_paid_upgrade</c>: a gifted subscription was continued as a paid one.</summary>
    GiftPaidUpgrade,
    /// <summary><c>prime_paid_upgrade</c>: a Prime subscription was upgraded to a paid one.</summary>
    PrimePaidUpgrade,
    /// <summary><c>raid</c>: another broadcaster raided the channel; details in <see cref="ChannelChatNotificationEvent.Raid"/>.</summary>
    Raid,
    /// <summary><c>unraid</c>: a raid was cancelled.</summary>
    Unraid,
    /// <summary><c>pay_it_forward</c>: a viewer paid a gifted subscription forward.</summary>
    PayItForward,
    /// <summary><c>announcement</c>: a moderator announcement; details in <see cref="ChannelChatNotificationEvent.Announcement"/>.</summary>
    Announcement,
    /// <summary><c>bits_badge_tier</c>: a viewer earned a new Bits badge tier.</summary>
    BitsBadgeTier,
    /// <summary><c>charity_donation</c>: a donation to the channel's charity campaign.</summary>
    CharityDonation,
    /// <summary><c>watch_streak</c>: a viewer reached a watch streak milestone.</summary>
    WatchStreak,
    /// <summary><c>modiversary</c>: a moderator's anniversary in the channel.</summary>
    Modiversary,
    /// <summary><c>gifted_drops_summary</c>: a summary of gifted drops.</summary>
    GiftedDropsSummary,
}

internal static class ChatNotificationKinds
{
    internal const string SharedChatPrefix = "shared_chat_";

    internal static ChatNotificationKind Parse(string? noticeType)
    {
        var name = noticeType is not null && noticeType.StartsWith(SharedChatPrefix, StringComparison.Ordinal) ? noticeType[SharedChatPrefix.Length..] : noticeType;
        return name switch
        {
            "sub" => ChatNotificationKind.Sub,
            "resub" => ChatNotificationKind.Resub,
            "sub_gift" => ChatNotificationKind.SubGift,
            "community_sub_gift" => ChatNotificationKind.CommunitySubGift,
            "gift_paid_upgrade" => ChatNotificationKind.GiftPaidUpgrade,
            "prime_paid_upgrade" => ChatNotificationKind.PrimePaidUpgrade,
            "raid" => ChatNotificationKind.Raid,
            "unraid" => ChatNotificationKind.Unraid,
            "pay_it_forward" => ChatNotificationKind.PayItForward,
            "announcement" => ChatNotificationKind.Announcement,
            "bits_badge_tier" => ChatNotificationKind.BitsBadgeTier,
            "charity_donation" => ChatNotificationKind.CharityDonation,
            "watch_streak" => ChatNotificationKind.WatchStreak,
            "modiversary" => ChatNotificationKind.Modiversary,
            "gifted_drops_summary" => ChatNotificationKind.GiftedDropsSummary,
            _ => ChatNotificationKind.Unknown,
        };
    }
}
