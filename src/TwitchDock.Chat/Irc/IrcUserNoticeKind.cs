namespace TwitchDock.Chat.Irc;

/// <summary>
/// The known <c>msg-id</c> values of a USERNOTICE, read through <see cref="IrcUserNotice.Kind"/>.
/// A value Twitch adds later maps to <see cref="Unknown"/>; <see cref="IrcUserNotice.MsgId"/> still holds its text.
/// </summary>
public enum IrcUserNoticeKind
{
    /// <summary>A <c>msg-id</c> this SDK version does not know, or none.</summary>
    Unknown = 0,
    /// <summary><c>sub</c>: a new subscription.</summary>
    Sub,
    /// <summary><c>resub</c>: a renewed subscription, usually with a message in <see cref="IrcUserNotice.Text"/>.</summary>
    Resub,
    /// <summary><c>subgift</c>: a subscription gifted to one recipient.</summary>
    SubGift,
    /// <summary><c>submysterygift</c>: gifted subscriptions to random viewers.</summary>
    SubMysteryGift,
    /// <summary><c>giftpaidupgrade</c>: a gifted subscription was continued as a paid one.</summary>
    GiftPaidUpgrade,
    /// <summary><c>rewardgift</c>: a reward gift, for example from a promotion.</summary>
    RewardGift,
    /// <summary><c>anongiftpaidupgrade</c>: an anonymously gifted subscription was continued as a paid one.</summary>
    AnonGiftPaidUpgrade,
    /// <summary><c>raid</c>: another broadcaster raided the channel.</summary>
    Raid,
    /// <summary><c>unraid</c>: a raid was cancelled.</summary>
    Unraid,
    /// <summary><c>bitsbadgetier</c>: a user earned a new Bits badge tier.</summary>
    BitsBadgeTier,
    /// <summary><c>sharedchatnotice</c>: a notice from another channel in a shared chat session.</summary>
    SharedChatNotice,
    /// <summary><c>announcement</c>: a moderator announcement.</summary>
    Announcement,
    /// <summary><c>modiversary</c>: a moderator's anniversary in the channel.</summary>
    Modiversary,
    /// <summary><c>viewermilestone</c>: a viewer milestone such as a watch streak.</summary>
    ViewerMilestone,
}

/// <summary>
/// Names of the documented <c>msg-param-*</c> tags of a USERNOTICE, without the <c>msg-param-</c> prefix,
/// for <see cref="IrcUserNotice.Parameters"/> and <see cref="IrcUserNotice.TryGetInt32Parameter"/>.
/// </summary>
public static class IrcUserNoticeParameters
{
    /// <summary><c>cumulative-months</c> (sub, resub): total months subscribed.</summary>
    public const string CumulativeMonths = "cumulative-months";
    /// <summary><c>streak-months</c> (sub, resub): consecutive months subscribed; 0 unless <see cref="ShouldShareStreak"/> is 1.</summary>
    public const string StreakMonths = "streak-months";
    /// <summary><c>should-share-streak</c> (sub, resub): 1 when the user shares the streak.</summary>
    public const string ShouldShareStreak = "should-share-streak";
    /// <summary><c>sub-plan</c> (sub, resub, subgift): <c>Prime</c>, <c>1000</c>, <c>2000</c> or <c>3000</c>.</summary>
    public const string SubPlan = "sub-plan";
    /// <summary><c>sub-plan-name</c> (sub, resub, subgift): the plan's display name.</summary>
    public const string SubPlanName = "sub-plan-name";
    /// <summary><c>months</c> (subgift: months the recipient has subscribed; modiversary: months as moderator).</summary>
    public const string Months = "months";
    /// <summary><c>gift-months</c> (subgift): months gifted in one multi-month gift.</summary>
    public const string GiftMonths = "gift-months";
    /// <summary><c>recipient-id</c> (subgift): the recipient's user ID.</summary>
    public const string RecipientId = "recipient-id";
    /// <summary><c>recipient-user-name</c> (subgift): the recipient's login.</summary>
    public const string RecipientUserName = "recipient-user-name";
    /// <summary><c>recipient-display-name</c> (subgift): the recipient's display name.</summary>
    public const string RecipientDisplayName = "recipient-display-name";
    /// <summary><c>sender-login</c> (giftpaidupgrade): login of the user who gifted the subscription.</summary>
    public const string SenderLogin = "sender-login";
    /// <summary><c>sender-name</c> (giftpaidupgrade): display name of the user who gifted the subscription.</summary>
    public const string SenderName = "sender-name";
    /// <summary><c>promo-name</c> (giftpaidupgrade, anongiftpaidupgrade): the running subscription promotion.</summary>
    public const string PromoName = "promo-name";
    /// <summary><c>promo-gift-total</c> (giftpaidupgrade, anongiftpaidupgrade): gifts given during <see cref="PromoName"/>.</summary>
    public const string PromoGiftTotal = "promo-gift-total";
    /// <summary><c>login</c> (raid): the raiding broadcaster's login.</summary>
    public const string Login = "login";
    /// <summary><c>displayName</c> (raid): the raiding broadcaster's display name.</summary>
    public const string DisplayName = "displayName";
    /// <summary><c>viewerCount</c> (raid): number of raiding viewers.</summary>
    public const string ViewerCount = "viewerCount";
    /// <summary><c>threshold</c> (bitsbadgetier): the Bits badge tier earned, for example 1000.</summary>
    public const string Threshold = "threshold";
    /// <summary><c>category</c> (viewermilestone): the milestone category, for example <c>watch-streak</c>.</summary>
    public const string Category = "category";
    /// <summary><c>id</c> (viewermilestone): the milestone event ID, not the chat message ID.</summary>
    public const string Id = "id";
    /// <summary><c>value</c> (viewermilestone): for a watch streak, the number of consecutive streams watched.</summary>
    public const string Value = "value";
}
