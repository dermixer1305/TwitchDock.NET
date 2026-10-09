using TwitchSdk.Chat.Irc;

namespace TwitchSdk.Tests;

public sealed class IrcTests
{
    private const string FullPrivmsg = "@badge-info=subscriber/16;badges=broadcaster/1,subscriber/12,glhf-pledge/1;bits=100;color=#1E90FF;display-name=Ronni;"
        + "emotes=25:0-4,12-16/1902:6-10;first-msg=1;flags=;id=b34ccfc7-4977-403a-8a94-33c6bac34fb8;mod=1;returning-chatter=0;room-id=1337;subscriber=1;"
        + "tmi-sent-ts=1507246572675;turbo=1;user-id=1337;user-type=global_mod :ronni!ronni@ronni.tmi.twitch.tv PRIVMSG #ronni :Kappa Keepo Kappa";

    [Fact]
    public void ParsesTagsPrefixCommandAndTrailingParameter()
    {
        var message = IrcMessage.Parse(FullPrivmsg + "\r\n");
        Assert.Equal("PRIVMSG", message.Command);
        Assert.Equal("ronni!ronni@ronni.tmi.twitch.tv", message.Prefix);
        Assert.Equal(("ronni", "ronni", "ronni.tmi.twitch.tv"), (message.Nick, message.User, message.Host));
        Assert.Equal(new[] { "#ronni", "Kappa Keepo Kappa" }, message.Parameters);
        Assert.True(message.HasTrailingParameter);
        Assert.Equal("#1E90FF", message.Tags["color"]);
        Assert.Equal("", message.Tags["flags"]);
        Assert.Null(message.GetTag("missing"));
        Assert.Null(message.GetParameter(2));
    }

    [Theory]
    [InlineData(@"a\:b", "a;b")]
    [InlineData(@"a\sb", "a b")]
    [InlineData(@"a\\b", @"a\b")]
    [InlineData(@"a\rb", "a\rb")]
    [InlineData(@"a\nb", "a\nb")]
    [InlineData(@"ab\", "ab")]
    [InlineData(@"a\xb", "axb")]
    [InlineData(@"\\s", @"\s")]
    [InlineData(@"\:\s\\", @"; \")]
    public void UnescapesTagValues(string raw, string expected)
    {
        Assert.Equal(expected, IrcMessage.Parse($"@k={raw} :tmi.twitch.tv NOTICE * :x").Tags["k"]);
        Assert.Equal(expected, IrcMessage.UnescapeTagValue(raw));
    }

    [Fact]
    public void EmptyAndMissingTagValuesAreEmptyAndLaterDuplicatesWin()
    {
        var message = IrcMessage.Parse("@a=;b;c=1;d=2;d=3; PING :tmi.twitch.tv");
        Assert.Equal("", message.Tags["a"]);
        Assert.Equal("", message.Tags["b"]);
        Assert.Equal("1", message.Tags["c"]);
        Assert.Equal("3", message.Tags["d"]);
        Assert.Null(message.Prefix);
    }

    [Fact]
    public void ParsesLinesWithoutPrefixOrTagsAndEdgeCaseParameters()
    {
        var ping = IrcMessage.Parse("PING :tmi.twitch.tv");
        Assert.Equal(("PING", "tmi.twitch.tv"), (ping.Command, ping.Parameters[0]));
        Assert.Null(ping.Prefix);
        Assert.Empty(ping.Tags);

        var empty = IrcMessage.Parse(":nick!user@host PRIVMSG #chan :");
        Assert.Equal(new[] { "#chan", "" }, empty.Parameters);
        Assert.True(empty.HasTrailingParameter);

        var colons = IrcMessage.Parse("PRIVMSG #chan :: hi :x");
        Assert.Equal(": hi :x", colons.Parameters[1]);

        var spaced = IrcMessage.Parse("cmd   a  b   c");
        Assert.Equal("CMD", spaced.Command);
        Assert.Equal(new[] { "a", "b", "c" }, spaced.Parameters);
        Assert.False(spaced.HasTrailingParameter);

        var numeric = IrcMessage.Parse(":tmi.twitch.tv 001 bot :Welcome, GLHF!");
        Assert.Equal("001", numeric.Command);
        Assert.Equal("tmi.twitch.tv", numeric.Host);
        Assert.Null(numeric.Nick);

        Assert.Equal("RECONNECT", IrcMessage.Parse(":tmi.twitch.tv RECONNECT").Command);
    }

    [Theory]
    [InlineData("nick!user@host", "nick", "user", "host")]
    [InlineData("nick@host", "nick", null, "host")]
    [InlineData("nick!user", "nick", "user", null)]
    [InlineData("justnick", "justnick", null, null)]
    [InlineData("tmi.twitch.tv", null, null, "tmi.twitch.tv")]
    public void SplitsPrefix(string prefix, string? nick, string? user, string? host)
    {
        var message = IrcMessage.Parse($":{prefix} PING");
        Assert.Equal((nick, user, host), (message.Nick, message.User, message.Host));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r\n")]
    [InlineData("@")]
    [InlineData("@a=b")]
    [InlineData("@a=b ")]
    [InlineData(":prefix")]
    [InlineData(":prefix ")]
    [InlineData(": PING")]
    [InlineData("@a=b :prefix")]
    [InlineData(" PING")]
    [InlineData("PR1VMSG #a :b")]
    [InlineData("12 x")]
    [InlineData("1234 x")]
    [InlineData("PRIVMSG #a :b\rc")]
    [InlineData("PRIVMSG #a :b\nJOIN #c")]
    [InlineData("PRIVMSG #a :b\0c")]
    [InlineData("@=x PING")]
    [InlineData("@k;=v PING")]
    [InlineData("@ké=v PING")]
    [InlineData("@a b=c PING")]
    public void RejectsMalformedLines(string line)
    {
        Assert.False(IrcMessage.TryParse(line, out var message));
        Assert.Null(message);
        Assert.Throws<FormatException>(() => IrcMessage.Parse(line));
    }

    [Fact]
    public void RejectsNullAndOverlongLines()
    {
        Assert.False(IrcMessage.TryParse(null, out _));
        Assert.False(IrcMessage.TryParse("PRIVMSG #a :" + new string('x', IrcMessage.MaxLineLength), out _));
        Assert.True(IrcMessage.TryParse("PRIVMSG #a :" + new string('x', IrcMessage.MaxLineLength - 12), out _));
    }

    [Fact]
    public void SerializesOutgoingTagsWithEscapingAndRoundTrips()
    {
        var reply = new IrcMessage("PRIVMSG", ["#chan", "hi"], [new("reply-parent-msg-id", "b34ccfc7")], lastParameterIsTrailing: true);
        Assert.Equal("@reply-parent-msg-id=b34ccfc7 PRIVMSG #chan :hi", reply.Serialize());

        var nonce = new IrcMessage("privmsg", ["#chan", "hello world"], [new("client-nonce", "a b;c\\d\r\n"), new("+example.com/flag", "")]);
        var line = nonce.Serialize();
        Assert.Contains(@"client-nonce=a\sb\:c\\d\r\n", line, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', line);
        Assert.DoesNotContain('\n', line);
        var parsed = IrcMessage.Parse(line);
        Assert.Equal("a b;c\\d\r\n", parsed.Tags["client-nonce"]);
        Assert.Equal("", parsed.Tags["+example.com/flag"]);
        Assert.Equal(new[] { "#chan", "hello world" }, parsed.Parameters);
        Assert.Equal(line, parsed.Serialize());

        Assert.Equal("JOIN #chan", new IrcMessage("JOIN", ["#chan"]).Serialize());
        Assert.Equal("PONG :tmi.twitch.tv", new IrcMessage("PONG", ["tmi.twitch.tv"], lastParameterIsTrailing: true).Serialize());
        Assert.Equal(@"a\sb\:c\\", IrcMessage.EscapeTagValue(@"a b;c\"));
    }

    [Fact]
    public void RejectsLineInjectionAndInvalidOutgoingValues()
    {
        Assert.Throws<ArgumentException>(() => new IrcMessage("PRIVMSG", ["#chan", "hi\r\nJOIN #evil"]));
        Assert.Throws<ArgumentException>(() => new IrcMessage("PRIVMSG", ["#chan", "hi\nPART #chan"]));
        Assert.Throws<ArgumentException>(() => new IrcMessage("PRIVMSG", ["#chan", "nul\0"]));
        Assert.Throws<ArgumentException>(() => new IrcMessage("PRIVMSG", ["#chan extra", "hi"]));
        Assert.Throws<ArgumentException>(() => new IrcMessage("PRIVMSG", ["", "hi"]));
        Assert.Throws<ArgumentException>(() => new IrcMessage("PRIVMSG", [":chan", "hi"]));
        Assert.Throws<ArgumentException>(() => new IrcMessage("PRIV MSG"));
        Assert.Throws<ArgumentException>(() => new IrcMessage("PRIVMSG\r\nQUIT"));
        Assert.Throws<ArgumentException>(() => new IrcMessage("PING", tags: [new("bad key", "v")]));
        Assert.Throws<ArgumentException>(() => new IrcMessage("PING", tags: [new("k", "v\0")]));
        Assert.Throws<ArgumentException>(() => new IrcMessage("PING", prefix: "a b"));
        Assert.Throws<ArgumentException>(() => new IrcMessage("PRIVMSG", ["#chan", new string('x', IrcMessage.MaxLineLength)]));
    }

    [Fact]
    public void ToStringRedactsPassButSerializeDoesNot()
    {
        var pass = new IrcMessage("PASS", ["oauth:secret-token"]);
        Assert.DoesNotContain("secret-token", pass.ToString(), StringComparison.Ordinal);
        Assert.Equal("PASS ***", pass.ToString());
        Assert.Equal("PASS oauth:secret-token", pass.Serialize());
        Assert.DoesNotContain("secret", IrcMessage.Parse("PASS oauth:secret").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ChatMessageViewReadsUserBadgesEmotesBitsAndFlags()
    {
        Assert.True(IrcChatMessage.TryCreate(IrcMessage.Parse(FullPrivmsg), out var chat));
        Assert.Equal("ronni", chat.Channel);
        Assert.Equal("Kappa Keepo Kappa", chat.Text);
        Assert.False(chat.IsAction);
        Assert.Equal("b34ccfc7-4977-403a-8a94-33c6bac34fb8", chat.MessageId);
        Assert.Equal(("1337", "ronni", "Ronni", "#1E90FF"), (chat.UserId, chat.UserLogin, chat.DisplayName, chat.Color));
        Assert.Equal(new[] { new IrcBadge("broadcaster", "1"), new IrcBadge("subscriber", "12"), new IrcBadge("glhf-pledge", "1") }, chat.Badges);
        Assert.Equal("16", chat.BadgeInfo["subscriber"]);
        Assert.Equal(new[] { new IrcEmote("25", 0, 4), new IrcEmote("25", 12, 16), new IrcEmote("1902", 6, 10) }, chat.Emotes);
        Assert.Equal(new[] { "Kappa", "Kappa", "Keepo" }, chat.Emotes.Select(e => e.GetText(chat.Text)));
        Assert.Equal(100, chat.Bits);
        Assert.True(chat.IsFirstMessage);
        Assert.False(chat.IsReturningChatter);
        Assert.True(chat.IsModerator);
        Assert.True(chat.IsSubscriber);
        Assert.True(chat.IsTurbo);
        Assert.True(chat.IsBroadcaster);
        Assert.False(chat.IsVip);
        Assert.Equal("global_mod", chat.UserType);
        Assert.Equal("1337", chat.RoomId);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1507246572675), chat.SentAt);
        Assert.Null(chat.Reply);
        Assert.Null(chat.Source);
        Assert.Null(chat.MsgId);
    }

    [Fact]
    public void EmotePositionsAreCodePointsSoEmojiDoNotShiftThem()
    {
        var message = IrcMessage.Parse("@emotes=25:2-6,10-14 :a!a@a.tmi.twitch.tv PRIVMSG #c :\U0001F600 Kappa \U0001F600 Kappa");
        Assert.True(IrcChatMessage.TryCreate(message, out var chat));
        Assert.Equal(new[] { "Kappa", "Kappa" }, chat.Emotes.Select(e => e.GetText(chat.Text)));
        Assert.Null(new IrcEmote("25", 40, 44).GetText(chat.Text));
    }

    [Fact]
    public void ActionRepliesVipAndMalformedTagsAreReadLeniently()
    {
        var message = IrcMessage.Parse(@"@bits=abc;tmi-sent-ts=999999999999999999;emotes=bad,1:x-2,3:5-1;badges=,/x,vip/1;vip=;msg-id=highlighted-message;"
            + @"reply-parent-msg-id=parent-1;reply-parent-user-id=42;reply-parent-user-login=viewer;reply-parent-display-name=Viewer;"
            + @"reply-parent-msg-body=hello\sthere\:);reply-thread-parent-msg-id=thread-1;reply-thread-parent-user-login=starter;client-nonce=n1 "
            + ":bot!bot@bot.tmi.twitch.tv PRIVMSG #chan :\u0001ACTION waves\u0001");
        Assert.True(IrcChatMessage.TryCreate(message, out var chat));
        Assert.True(chat.IsAction);
        Assert.Equal("waves", chat.Text);
        Assert.Null(chat.Bits);
        Assert.Null(chat.SentAt);
        Assert.Empty(chat.Emotes);
        Assert.Equal(new[] { new IrcBadge("vip", "1") }, chat.Badges);
        Assert.True(chat.IsVip);
        Assert.Equal("highlighted-message", chat.MsgId);
        Assert.Equal("n1", chat.ClientNonce);
        var reply = Assert.IsType<IrcReply>(chat.Reply);
        Assert.Equal(("parent-1", "42", "viewer", "Viewer", "hello there;)"),
            (reply.ParentMessageId, reply.ParentUserId, reply.ParentUserLogin, reply.ParentDisplayName, reply.ParentMessageBody));
        Assert.Equal(("thread-1", "starter"), (reply.ThreadParentMessageId, reply.ThreadParentUserLogin));
    }

    [Fact]
    public void ChatMessageReadsSharedChatSourceTags()
    {
        var message = IrcMessage.Parse("@id=local;room-id=1;source-badge-info=subscriber/5;source-badges=subscriber/3,moderator/1;source-id=source-msg;"
            + "source-room-id=999;source-only=0 :a!a@a.tmi.twitch.tv PRIVMSG #chan :hi");
        Assert.True(IrcChatMessage.TryCreate(message, out var chat));
        var source = Assert.IsType<IrcSharedChatSource>(chat.Source);
        Assert.Equal(("999", "source-msg"), (source.RoomId, source.MessageId));
        Assert.Equal(new[] { new IrcBadge("subscriber", "3"), new IrcBadge("moderator", "1") }, source.Badges);
        Assert.Equal("5", source.BadgeInfo["subscriber"]);
        Assert.False(source.IsSourceOnly);
        Assert.Null(source.MsgId);
    }

    [Fact]
    public void ViewsRejectOtherCommandsAndMissingChannels()
    {
        Assert.False(IrcChatMessage.TryCreate(IrcMessage.Parse("PRIVMSG bot :direct"), out _));
        Assert.False(IrcChatMessage.TryCreate(IrcMessage.Parse("PRIVMSG #chan"), out _));
        Assert.False(IrcChatMessage.TryCreate(IrcMessage.Parse("NOTICE #chan :x"), out _));
        Assert.False(IrcUserNotice.TryCreate(IrcMessage.Parse("USERNOTICE *"), out _));
        Assert.False(IrcClearChat.TryCreate(IrcMessage.Parse("CLEARCHAT"), out _));
        Assert.False(IrcClearMessage.TryCreate(IrcMessage.Parse("PRIVMSG #chan :x"), out _));
        Assert.False(IrcRoomState.TryCreate(IrcMessage.Parse("USERSTATE #chan"), out _));
        Assert.False(IrcUserState.TryCreate(IrcMessage.Parse("ROOMSTATE #chan"), out _));
        Assert.False(IrcGlobalUserState.TryCreate(IrcMessage.Parse("USERSTATE #chan"), out _));
        Assert.False(IrcNotice.TryCreate(IrcMessage.Parse("NOTICE"), out _));
        Assert.False(IrcWhisper.TryCreate(IrcMessage.Parse("WHISPER foo"), out _));
    }

    [Fact]
    public void UserNoticeExposesMsgIdParametersAndSystemMessage()
    {
        var message = IrcMessage.Parse(@"@badge-info=subscriber/5;badges=subscriber/3;color=#0000FF;display-name=Viewer;emotes=;id=notice-1;login=viewer;mod=0;"
            + @"msg-id=resub;msg-param-cumulative-months=5;msg-param-should-share-streak=0;msg-param-sub-plan-name=Channel\sSubscription\s(channel);"
            + @"msg-param-sub-plan=1000;msg-param-was-gifted=false;room-id=12345;subscriber=1;"
            + @"system-msg=Viewer\ssubscribed\sat\sTier\s1.\sThey've\ssubscribed\sfor\s5\smonths!;tmi-sent-ts=1507246572675;user-id=67890;user-type= "
            + ":tmi.twitch.tv USERNOTICE #channel :Great stream -- keep it up!");
        Assert.True(IrcUserNotice.TryCreate(message, out var notice));
        Assert.Equal(("channel", "resub", "viewer", "67890"), (notice.Channel, notice.MsgId, notice.Login, notice.UserId));
        Assert.Equal("Great stream -- keep it up!", notice.Text);
        Assert.Equal("Viewer subscribed at Tier 1. They've subscribed for 5 months!", notice.SystemMessage);
        Assert.Equal("Channel Subscription (channel)", notice.Parameters["sub-plan-name"]);
        Assert.Equal("false", notice.Parameters["was-gifted"]);
        Assert.Equal(5, notice.Parameters.Count);
        Assert.True(notice.TryGetInt32Parameter("cumulative-months", out var months));
        Assert.Equal(5, months);
        Assert.False(notice.TryGetInt32Parameter("was-gifted", out _));
        Assert.Equal("5", notice.BadgeInfo["subscriber"]);
        Assert.Null(notice.UserType);
        Assert.True(notice.IsSubscriber);

        Assert.True(IrcUserNotice.TryCreate(IrcMessage.Parse("@msg-id=raid;msg-param-viewerCount=15;source-room-id=7;source-msg-id=raid :tmi.twitch.tv USERNOTICE #channel"), out var raid));
        Assert.Null(raid.Text);
        Assert.Equal("15", raid.Parameters["viewerCount"]);
        Assert.Equal(("7", "raid"), (raid.Source!.RoomId, raid.Source.MsgId));
    }

    [Fact]
    public void ClearChatDistinguishesTimeoutBanAndClear()
    {
        Assert.True(IrcClearChat.TryCreate(IrcMessage.Parse("@ban-duration=350;room-id=12345;target-user-id=67890;tmi-sent-ts=1642719320727 :tmi.twitch.tv CLEARCHAT #dallas :ronni"), out var timeout));
        Assert.Equal(("dallas", "ronni", "67890", "12345"), (timeout.Channel, timeout.TargetLogin, timeout.TargetUserId, timeout.RoomId));
        Assert.Equal(TimeSpan.FromSeconds(350), timeout.BanDuration);
        Assert.False(timeout.IsPermanentBan);
        Assert.False(timeout.IsChatCleared);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1642719320727), timeout.SentAt);

        Assert.True(IrcClearChat.TryCreate(IrcMessage.Parse("@room-id=12345;target-user-id=67890 :tmi.twitch.tv CLEARCHAT #dallas :ronni"), out var ban));
        Assert.True(ban.IsPermanentBan);
        Assert.Null(ban.BanDuration);

        Assert.True(IrcClearChat.TryCreate(IrcMessage.Parse("@room-id=12345 :tmi.twitch.tv CLEARCHAT #dallas"), out var clear));
        Assert.True(clear.IsChatCleared);
        Assert.False(clear.IsPermanentBan);
        Assert.Null(clear.TargetLogin);
    }

    [Fact]
    public void ClearMessageReadsTargetAndText()
    {
        Assert.True(IrcClearMessage.TryCreate(IrcMessage.Parse("@login=foo;room-id=;target-msg-id=94e6c7ff-bf98-4faa-af5d-7ad633a158a9;tmi-sent-ts=1642720582342 :tmi.twitch.tv CLEARMSG #bar :what a great day"), out var clear));
        Assert.Equal(("bar", "foo", "94e6c7ff-bf98-4faa-af5d-7ad633a158a9", "what a great day"), (clear.Channel, clear.Login, clear.TargetMessageId, clear.Text));
        Assert.Null(clear.RoomId);
        Assert.NotNull(clear.SentAt);
    }

    [Fact]
    public void RoomStateDistinguishesFullAndPartialUpdates()
    {
        Assert.True(IrcRoomState.TryCreate(IrcMessage.Parse("@emote-only=0;followers-only=-1;r9k=0;room-id=12345678;slow=0;subs-only=0 :tmi.twitch.tv ROOMSTATE #bar"), out var full));
        Assert.Equal(("bar", "12345678"), (full.Channel, full.RoomId));
        Assert.Equal((false, false, false, false), (full.IsEmoteOnly, full.IsFollowersOnly, full.IsUniqueChat, full.IsSubscribersOnly));
        Assert.Null(full.FollowersOnlyDuration);
        Assert.Equal(TimeSpan.Zero, full.SlowModeDelay);

        Assert.True(IrcRoomState.TryCreate(IrcMessage.Parse("@followers-only=10;room-id=12345678;slow=30 :tmi.twitch.tv ROOMSTATE #bar"), out var partial));
        Assert.True(partial.IsFollowersOnly);
        Assert.Equal(TimeSpan.FromMinutes(10), partial.FollowersOnlyDuration);
        Assert.Equal(TimeSpan.FromSeconds(30), partial.SlowModeDelay);
        Assert.Null(partial.IsEmoteOnly);
        Assert.Null(partial.IsUniqueChat);
        Assert.Null(partial.IsSubscribersOnly);
    }

    [Fact]
    public void UserStateAndGlobalUserStateReadEmoteSetsAndRoles()
    {
        Assert.True(IrcUserState.TryCreate(IrcMessage.Parse("@badge-info=;badges=staff/1,broadcaster/1;color=#0D4200;display-name=ronni;emote-sets=0,33,50,237;id=sent-1;mod=1;subscriber=1;turbo=1;user-type=staff :tmi.twitch.tv USERSTATE #dallas"), out var state));
        Assert.Equal(("dallas", "sent-1", "ronni", "staff"), (state.Channel, state.MessageId, state.DisplayName, state.UserType));
        Assert.Equal(new[] { "0", "33", "50", "237" }, state.EmoteSets);
        Assert.True(state.IsModerator && state.IsSubscriber && state.IsTurbo && state.IsBroadcaster);
        Assert.Empty(state.BadgeInfo);

        Assert.True(IrcGlobalUserState.TryCreate(IrcMessage.Parse("@badge-info=subscriber/8;badges=subscriber/6;color=#0D4200;display-name=dallas;emote-sets=0,33,50;turbo=0;user-id=12345678;user-type=admin :tmi.twitch.tv GLOBALUSERSTATE"), out var global));
        Assert.Equal(("12345678", "dallas", "admin", "#0D4200"), (global.UserId, global.DisplayName, global.UserType, global.Color));
        Assert.Equal(3, global.EmoteSets.Count);
        Assert.Equal("8", global.BadgeInfo["subscriber"]);
        Assert.False(global.IsTurbo);
    }

    [Fact]
    public void NoticeAndWhisperViews()
    {
        Assert.True(IrcNotice.TryCreate(IrcMessage.Parse("@msg-id=delete_message_success;target-user-id=7 :tmi.twitch.tv NOTICE #bar :The message from foo is now deleted."), out var notice));
        Assert.Equal(("bar", "delete_message_success", "The message from foo is now deleted.", "7"), (notice.Channel, notice.MsgId, notice.Text, notice.TargetUserId));
        Assert.True(IrcNotice.TryCreate(IrcMessage.Parse(":tmi.twitch.tv NOTICE * :Login authentication failed"), out var login));
        Assert.Null(login.Channel);
        Assert.Null(login.MsgId);
        Assert.Equal("Login authentication failed", login.Text);

        Assert.True(IrcWhisper.TryCreate(IrcMessage.Parse("@badges=staff/1;color=#8A2BE2;display-name=PetsgomOO;emotes=;message-id=306;thread-id=12345678_87654321;turbo=0;user-id=87654321;user-type=staff :petsgomoo!petsgomoo@petsgomoo.tmi.twitch.tv WHISPER foo :hello there"), out var whisper));
        Assert.Equal(("petsgomoo", "foo", "hello there", "306", "12345678_87654321", "87654321"),
            (whisper.FromLogin, whisper.ToLogin, whisper.Text, whisper.MessageId, whisper.ThreadId, whisper.UserId));
        Assert.Equal("PetsgomOO", whisper.DisplayName);
        Assert.Single(whisper.Badges);
    }

    [Fact]
    public async Task RouterDispatchesTypedViewsCommandsAndFallback()
    {
        var seen = new List<string>();
        var router = new IrcMessageRouter()
            .OnChatMessage((chat, _) => { seen.Add("chat:" + chat.Text); return Task.CompletedTask; })
            .OnUserNotice((notice, _) => { seen.Add("notice:" + notice.MsgId); return Task.CompletedTask; })
            .OnCommand("join", (message, _) => { seen.Add("join:" + message.Nick); return Task.CompletedTask; })
            .OnUnhandled((message, _) => { seen.Add("other:" + message.Command); return Task.CompletedTask; });
        // The documented pattern passes the router straight to TwitchIrcClient.RunAsync.
        Func<IrcMessage, CancellationToken, Task> callback = router.DispatchAsync;
        await callback(IrcMessage.Parse(":a!a@a.tmi.twitch.tv PRIVMSG #c :hi"), CancellationToken.None);
        seen.Clear();
        Assert.True(await router.DispatchAsync(IrcMessage.Parse(":a!a@a.tmi.twitch.tv PRIVMSG #c :hi")));
        Assert.True(await router.DispatchAsync(IrcMessage.Parse("@msg-id=sub :tmi.twitch.tv USERNOTICE #c")));
        Assert.True(await router.DispatchAsync(IrcMessage.Parse(":viewer!viewer@viewer.tmi.twitch.tv JOIN #c")));
        Assert.True(await router.DispatchAsync(IrcMessage.Parse("PRIVMSG bot :not a channel")));
        Assert.True(await router.DispatchAsync(IrcMessage.Parse(":tmi.twitch.tv 001 bot :Welcome")));
        Assert.Equal(new[] { "chat:hi", "notice:sub", "join:viewer", "other:PRIVMSG", "other:001" }, seen);

        var empty = new IrcMessageRouter().OnClearChat((_, _) => Task.CompletedTask);
        Assert.False(await empty.DispatchAsync(IrcMessage.Parse("PRIVMSG #c :hi")));
        Assert.Throws<InvalidOperationException>(() => empty.OnClearChat((_, _) => Task.CompletedTask));
        Assert.Throws<InvalidOperationException>(() => empty.OnCommand("CLEARCHAT", (_, _) => Task.CompletedTask));
    }

    [Theory]
    [InlineData("#SomeChannel", "somechannel")]
    [InlineData("some_channel_123", "some_channel_123")]
    public void NormalizesChannelNames(string input, string expected) => Assert.Equal(expected, TwitchIrcClient.NormalizeChannelName(input));

    [Theory]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("##chan")]
    [InlineData("chan nel")]
    [InlineData("chan\r\nJOIN #x")]
    [InlineData("chän")]
    [InlineData("abcdefghijklmnopqrstuvwxyz")]
    public void RejectsInvalidChannelNames(string input) => Assert.Throws<ArgumentException>(() => TwitchIrcClient.NormalizeChannelName(input));
}
