# TwitchDock.NET

**Ein unabhängiges .NET-SDK für die Twitch-API, EventSub und Chat.**

[English](README.md) · **Deutsch** · [Tutorial](docs/tutorial.de.md) · [Dokumentation](docs/README.md) · [NuGet](https://www.nuget.org/packages/TwitchDock.DependencyInjection)

[![NuGet](https://img.shields.io/nuget/vpre/TwitchDock.DependencyInjection?label=NuGet)](https://www.nuget.org/packages/TwitchDock.DependencyInjection)
[![Build and test](https://github.com/dermixer1305/TwitchDock.NET/actions/workflows/ci.yml/badge.svg)](https://github.com/dermixer1305/TwitchDock.NET/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Entwickle Chatbots, Stream-Alerts und Kanalwerkzeuge in C# – mit typisierten Anfragen, typisierten Ereignissen und asynchronen Methoden. TwitchDock läuft auf **.NET 8 und .NET 10** und unterstützt Trimming und Native AOT.

Das Projekt wurde eigenständig neu entwickelt. Es ist weder ein offizielles Twitch-Produkt noch ein Fork oder Nachfolger von TwitchLib. Es besteht keine Verbindung zu Twitch und keine Unterstützung durch Twitch.

## Installation

```sh
dotnet add package TwitchDock.DependencyInjection --prerelease
```

Dieses eine Paket bringt alle Module mit. Außerdem brauchst du eine **Client-ID**: Registriere eine Anwendung in der [Twitch-Entwicklerkonsole](https://dev.twitch.tv/console/apps) und kopiere ihre Client-ID. Das [deutsche Tutorial](docs/tutorial.de.md) zeigt die Registrierung Schritt für Schritt.

## Dein erster Chatbot

Eine vollständige `Program.cs` für eine Konsolen-App. Sie meldet dich mit deinem Twitch-Konto an, verbindet sich mit deinem eigenen Chat und beantwortet Befehle:

```csharp
using TwitchDock.Authentication;
using TwitchDock.Core;
using TwitchDock.EventSub;
using TwitchDock.Helix;
using TwitchDock.Helix.Models;

const string ClientId = "deine-client-id"; // aus dev.twitch.tv/console/apps – kein Geheimnis

using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
var oauth = new TwitchOAuthClient(http);

// 1. Anmelden: Twitch zeigt einen Code, den du im Browser bestätigst. Kein Client-Secret nötig.
string[] scopes = [TwitchScopes.UserReadChat, TwitchScopes.UserWriteChat];
var device = await oauth.StartDeviceAuthorizationAsync(ClientId, scopes);
Console.WriteLine($"Öffne {device.VerificationUri} und bestätige den Code {device.UserCode}");
var grant = await oauth.WaitForDeviceAuthorizationAsync(ClientId, device, scopes);

// 2. Token einmal prüfen: Das SDK erfährt, wer angemeldet ist und welche Rechte erteilt wurden.
var me = await oauth.ValidateAsync(grant.AccessToken);
var botId = me.UserId!;
var channelId = botId; // dein eigener Kanal; für einen anderen Chat dessen Broadcaster-ID eintragen
var tokens = new StaticAccessTokenProvider(me.ToAccessToken(grant.AccessToken));
var helix = new HelixClient(new TwitchHttpClient(http, tokens, new TwitchHttpOptions { ClientId = ClientId }));

// 3. Befehle: Jede Chatnachricht kommt als typisiertes Ereignis an.
var router = new EventSubEventRouter()
    .On(EventSubEvents.ChannelChatMessageV1, async (chat, _, ct) =>
    {
        Console.WriteLine($"{chat.ChatterUserName}: {chat.Message.Text}");
        var reply = chat.Message.Text.Trim().ToLowerInvariant() switch
        {
            "!ping" => "pong",
            "!hallo" => $"Hallo @{chat.ChatterUserName}!",
            "!würfel" => $"Du hast eine {Random.Shared.Next(1, 7)} gewürfelt",
            _ => null,
        };
        if (reply is null) return;

        await helix.SendChatMessageAsync(new SendChatMessageRequest
        {
            BroadcasterId = channelId, SenderId = botId, Message = reply, ReplyParentMessageId = chat.MessageId,
        }, ct);
    });

// 4. Verbinden: EventSub über WebSocket, Wiederverbindungen übernimmt der Client.
var socket = new EventSubWebSocketClient();
await socket.RunAsync(
    async (session, resubscribe, ct) =>
    {
        if (!resubscribe) return; // Twitch hat die Sitzung verschoben, das Abo bleibt bestehen
        await helix.SubscribeWebSocketAsync(EventSubSubscriptions.ChannelChatMessageV1(channelId, botId), session.Id, ct);
        Console.WriteLine("Bot ist online – schreib !ping in deinen Chat.");
    },
    router.DispatchAsync);
```

Starte ihn mit `dotnet run`, öffne den angezeigten Link, bestätige den Code und schreib `!ping` in deinen Kanal-Chat. Ein Konto reicht: Der Bot darf in seinem eigenen Kanal antworten.

Twitch verlangt, dass länger laufende Apps Benutzer-Tokens stündlich prüfen. Das [Chatbot-Beispiel](samples/TwitchDock.ChatBot/Program.cs) ergänzt das mit `TokenValidationLoop` und behandelt außerdem Strg+C und Nachrichten, die Twitch verwirft. Token-Erneuerung erklärt [Authentifizierung](docs/authentication.md).

## Auf Follows, Subs, Raids und mehr reagieren

Fordere die Rechte an, die die Ereignisse brauchen, melde dich wie oben an und leite jedes Ereignis an einen eigenen, typisierten Handler:

```csharp
string[] scopes =
[
    TwitchScopes.ModeratorReadFollowers, TwitchScopes.ChannelReadSubscriptions,
    TwitchScopes.ChannelReadRedemptions, TwitchScopes.BitsRead,
];
// ... anmelden und `helix` genau wie beim Chatbot erstellen ...

var router = new EventSubEventRouter()
    .On(EventSubEvents.ChannelFollowV2, (follow, _, _) => Log($"{follow.UserName} folgt jetzt"))
    .On(EventSubEvents.ChannelSubscribeV1, (sub, _, _) => Log($"{sub.UserName} hat abonniert (Stufe {sub.Tier})"))
    .On(EventSubEvents.ChannelCheerV1, (cheer, _, _) => Log($"{cheer.UserName ?? "Anonym"} hat {cheer.Bits} Bits gecheert"))
    .On(EventSubEvents.ChannelRaidV1, (raid, _, _) => Log($"Raid von {raid.FromBroadcasterUserName} mit {raid.Viewers} Zuschauern"))
    .On(EventSubEvents.ChannelPointsCustomRewardRedemptionAddV1, (redeem, _, _) => Log($"{redeem.UserName} hat {redeem.Reward.Title} eingelöst"))
    .On(EventSubEvents.StreamOnlineV1, (online, _, _) => Log($"{online.BroadcasterUserName} ist live!"));

await new EventSubWebSocketClient().RunAsync(
    async (session, resubscribe, ct) =>
    {
        if (!resubscribe) return;
        EventSubSubscriptionSpec[] subscriptions =
        [
            EventSubSubscriptions.ChannelFollowV2(channelId, moderatorUserId: channelId),
            EventSubSubscriptions.ChannelSubscribeV1(channelId),
            EventSubSubscriptions.ChannelCheerV1(channelId),
            EventSubSubscriptions.ChannelRaidV1(toBroadcasterUserId: channelId),
            EventSubSubscriptions.ChannelPointsCustomRewardRedemptionAddV1(channelId),
            EventSubSubscriptions.StreamOnlineV1(channelId),
        ];
        foreach (var subscription in subscriptions)
            await helix.SubscribeWebSocketAsync(subscription, session.Id, ct);
    },
    router.DispatchAsync);

static Task Log(string text)
{
    Console.WriteLine(text);
    return Task.CompletedTask;
}
```

Alle 83 EventSub-Typen haben eine Fabrikmethode auf `EventSubSubscriptions` und ein typisiertes Ereignis auf `EventSubEvents`. Das SDK prüft die Rechte des Tokens vor dem Abonnieren und wirft `TwitchAuthorizationException`, wenn eines fehlt. Webhooks und Conduits werden ebenfalls unterstützt, siehe [EventSub](docs/eventsub.md).

## Die Twitch-API abfragen

Server-Werkzeuge, die nur öffentliche Daten lesen, kommen mit einem App-Token statt einer Benutzeranmeldung aus:

```csharp
using TwitchDock.Authentication;
using TwitchDock.Core;
using TwitchDock.Helix;

var clientId = Environment.GetEnvironmentVariable("TWITCH_CLIENT_ID")!;
var clientSecret = Environment.GetEnvironmentVariable("TWITCH_CLIENT_SECRET")!; // Geheimnisse nicht in den Quellcode schreiben

using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
var oauth = new TwitchOAuthClient(http);
// App-Token: wird mit Client-ID und Secret geholt und bei Ablauf automatisch erneuert.
using var tokens = new RefreshingTokenProvider((_, ct) => oauth.GetAppTokenAsync(clientId, clientSecret, ct));
var helix = new HelixClient(new TwitchHttpClient(http, tokens, new TwitchHttpOptions { ClientId = clientId }));

var users = await helix.GetUsersAsync(new() { Logins = ["twitchdev"] });
Console.WriteLine($"{users.Data[0].DisplayName}: {users.Data[0].Description}");

var streams = await helix.GetStreamsAsync(new() { Languages = ["de"], First = 5 });
foreach (var stream in streams.Data)
    Console.WriteLine($"{stream.UserName} spielt {stream.GameName} vor {stream.ViewerCount} Zuschauern");
```

`HelixClient` deckt alle 149 Helix-Endpunkte ab: Kanäle, Moderation, Umfragen, Vorhersagen, Kanalpunkte, Clips, Streampläne und mehr. Ratenbegrenzung, Wiederholungen und Seitennavigation (`Enumerate*Async`) sind eingebaut. Siehe [Dokumentationsindex](docs/README.md).

## ASP.NET Core und Dependency Injection

```csharp
using TwitchDock.Authentication;
using TwitchDock.Core;
using TwitchDock.DependencyInjection;
using TwitchDock.Helix;

var builder = WebApplication.CreateBuilder(args);
var clientId = builder.Configuration["Twitch:ClientId"]!;
var clientSecret = builder.Configuration["Twitch:ClientSecret"]!; // User Secrets oder Umgebungsvariablen

builder.Services.AddTwitchDock(new TwitchHttpOptions { ClientId = clientId }, sp =>
{
    var oauth = sp.GetRequiredService<TwitchOAuthClient>();
    return new RefreshingTokenProvider((_, ct) => oauth.GetAppTokenAsync(clientId, clientSecret, ct));
});
// Prüft das Token beim Start und stündlich, wie Twitch es für länger laufende Apps verlangt.
builder.Services.AddTwitchTokenValidation(new TwitchTokenValidationOptions { ExpectedClientId = clientId });

var app = builder.Build();

app.MapGet("/live/{login}", async (string login, HelixClient helix, CancellationToken ct) =>
{
    var streams = await helix.GetStreamsAsync(new() { UserLogins = [login] }, ct);
    return streams.Data is [var stream]
        ? Results.Ok(new { live = true, stream.Title, stream.GameName, stream.ViewerCount })
        : Results.Ok(new { live = false });
});

app.Run();
```

`AddTwitchDock` registriert `HelixClient`, `TwitchChatClient`, `EventSubWebSocketClient` und `TwitchOAuthClient`. Einen fertigen EventSub-Webhook-Empfänger zeigt das [Webhook-Beispiel](samples/TwitchDock.WebhookHost/Program.cs).

## Welches Paket brauche ich?

**Unsicher? Installiere `TwitchDock.DependencyInjection`** – darin ist alles enthalten. Für kleinere Apps kannst du einzelne Pakete wählen; jedes bringt seine Abhängigkeiten automatisch mit.

| Ich möchte … | Installieren | Bringt außerdem mit |
| --- | --- | --- |
| Einen Chatbot bauen oder auf Follows, Subs, Raids reagieren (Beispiele oben) | `TwitchDock.EventSub` + `TwitchDock.Authentication` | Helix, Core |
| Daten lesen oder ändern: Benutzer, Streams, Clips, Umfragen, Bans | `TwitchDock.Helix` + `TwitchDock.Authentication` | Core |
| Nur Benutzer anmelden oder Tokens verwalten („Mit Twitch anmelden“) | `TwitchDock.Authentication` | Core |
| IRC-Chat oder den vereinfachten Chat-Client nutzen | `TwitchDock.Chat` | EventSub, Helix, Core |
| ASP.NET Core, einen Worker Service oder `IServiceCollection` nutzen | `TwitchDock.DependencyInjection` | alles |
| Eine eigene Schicht auf dem HTTP-Transport bauen | `TwitchDock.Core` | – |

### TwitchDock.Core

HTTP-Transport, Ratenbegrenzung, begrenzte Wiederholungen, Seitennavigation, Token-Abstraktionen und Rechteprüfung. Alle anderen Pakete bauen darauf auf, deshalb installierst du es selten allein. Du begegnest ihm über seine Typen: Token-Provider, `TwitchScopes` und die zwei Ausnahmen:

```csharp
try
{
    await helix.SendChatMessageAsync(request, ct);
}
catch (TwitchAuthorizationException ex) // lokal vor dem Senden geprüft: Twitch hat nichts erhalten
{
    Console.WriteLine($"Dem Token fehlt: {string.Join(", ", ex.MissingScopes)}");
}
catch (TwitchApiException ex) // Twitch hat mit einem Fehler geantwortet
{
    Console.WriteLine($"{ex.StatusCode}: {ex.Message} (Trace {ex.RequestId})");
}
```

### TwitchDock.Authentication

OAuth-Abläufe, Gerätecode-Anmeldung, OpenID Connect, Token-Erneuerung, -Prüfung und -Widerruf. Du brauchst es, sobald sich jemand anmelden muss oder deine App ein App-Token braucht. Ein Bot, der tagelang läuft, hält sein Benutzer-Token so am Leben:

```csharp
var userTokens = new RefreshingTokenProvider(
    (refreshToken, ct) => oauth.RefreshAsync(clientId, refreshToken!, clientSecret, ct), // Secret nur bei vertraulichen Apps
    initialToken: grant,                                    // z. B. das Ergebnis der Gerätecode-Anmeldung
    persist: (rotated, ct) => SaveEncryptedAsync(rotated, ct)); // Twitch tauscht Refresh-Tokens aus: das neue speichern
```

Alle Abläufe (Authorization Code, Implicit, Gerätecode, Client Credentials, OIDC) stehen in [Authentifizierung](docs/authentication.md).

### TwitchDock.Helix

Typisierte REST-API-Gruppen: Benutzer, Streams, Kanäle, Moderation, Chat, Umfragen, Kanalpunkte und mehr. Du brauchst es für alles, was du auf Twitch *tust* – im Gegensatz zu Ereignissen, die du *empfängst*:

```csharp
await helix.Channels.ModifyChannelInformationAsync(new() { BroadcasterId = channelId, Title = "Ranked grind!", GameId = "509658" });
await helix.Polls.CreatePollAsync(new()
{
    BroadcasterId = channelId, Title = "Nächstes Spiel?", Duration = 120,
    Choices = [new() { Title = "Minecraft" }, new() { Title = "Elden Ring" }],
});
await helix.Moderation.BanUserAsync(new() { BroadcasterId = channelId, ModeratorId = botId, Data = new() { UserId = spammerId, Duration = 600, Reason = "Spam" } });
await helix.Chat.SendShoutoutAsync(channelId, raiderId, moderatorId: botId);
```

Diese Aufrufe brauchen die Rechte `channel:manage:broadcast`, `channel:manage:polls`, `moderator:manage:banned_users` und `moderator:manage:shoutouts`. Alle Gruppen (`helix.Users`, `helix.Clips`, `helix.ChannelPoints`, `helix.Schedule` …) stehen im [Dokumentationsindex](docs/README.md).

### TwitchDock.EventSub

Typisierte Abos und Ereignisse, WebSocket-Client, Webhook-Prüfung und -Routing, Conduits und Batching. Du brauchst es, um benachrichtigt zu werden: Chatnachrichten, Follows, Subs, Raids, Kanalpunkte, Stream-Start. Die Beispiele [Chatbot](#dein-erster-chatbot) und [Ereignisse](#auf-follows-subs-raids-und-mehr-reagieren) oben nutzen es. Für Server gibt es einen [Webhook-Empfänger](samples/TwitchDock.WebhookHost/Program.cs); große Bots verteilen die Last über [Conduits](docs/helix-conduits.md).

### TwitchDock.Chat

Chat über EventSub und Helix sowie IRC mit Wiederverbindung und Ratenbegrenzung. Nutze `TwitchChatClient` als Abkürzung für den EventSub-Chatbot, oder `TwitchIrcClient`, um einen IRC-Bot zu übernehmen oder viele Kanäle über eine Verbindung zu lesen:

```csharp
var irc = new TwitchIrcClient(userTokens, new TwitchIrcOptions { Login = "mybot" }); // Rechte chat:read, chat:edit
await irc.JoinAsync("somechannel");
var router = new IrcMessageRouter()
    .OnChatMessage(async (chat, ct) =>
    {
        if (chat.Text == "!ping") await irc.SendMessageAsync(chat.Channel, "pong", chat.MessageId, ct);
    });
await irc.RunAsync(router.DispatchAsync);
```

Twitch empfiehlt EventSub für neue Bots; [IRC oder EventSub?](docs/chat-irc.md) vergleicht beide.

### TwitchDock.DependencyInjection

`AddTwitchDock`, Token-Prüfung als Hosted Service und Registrierung; bringt alle Module mit. Du brauchst es in ASP.NET Core, Worker Services oder jeder App mit `IServiceCollection`: Ein Aufruf registriert `HelixClient`, `TwitchChatClient`, `EventSubWebSocketClient` und `TwitchOAuthClient`, `AddTwitchTokenValidation` ergänzt die stündliche Prüfung, die Twitch verlangt, und `AddTwitchIrc` registriert den IRC-Client. Siehe das [ASP.NET-Core-Beispiel](#aspnet-core-und-dependency-injection) oben.

Jede Operation akzeptiert ein `CancellationToken`. Die JSON-Verarbeitung ist quellgeneriert. Zur Laufzeit hängt das SDK nur von den Microsoft.Extensions-Paketen für Logging und Hosting ab.

## Stand der Veröffentlichung

**1.0.0-rc.1 ist eine Vorabversion.** Alle Helix-Endpunkte (149) und EventSub-Typen (83) der offiziellen Dokumentation vom 09.10.2026 haben typisierte Modelle, Rechteprüfungen, Tests und Dokumentation ([Abdeckungsbericht](docs/coverage.md)). In der CI laufen 878 Unit- und Vertragstests pro Zielplattform, Integrationstests mit der Twitch CLI und Native-AOT-Prüfungen. Anmeldung, API-Abfragen und Chat wurden live gegen Twitch getestet. IRC, öffentliche Webhooks, Wiederverbindungen und Token-Erneuerung brauchen noch umfassendere Live-Tests ([Testbericht](docs/live-verification.md)). Öffentliche APIs können sich bis 1.0.0 noch ändern.

## Weitere Dokumentation

- [Tutorial auf Deutsch](docs/tutorial.de.md) und [English tutorial](docs/tutorial.md): App registrieren, erster API-Aufruf, Chatbot, häufige Fehler
- [C#-Schnellstart](docs/quickstart.md), [Anmeldung und Tokens](docs/authentication.md), [EventSub](docs/eventsub.md), [IRC](docs/chat-irc.md)
- [Ausführbare Beispiele](docs/samples.md): API-Abfrage, Chatbot und Webhook-Empfänger
- [Dokumentationsindex](docs/README.md), [Roadmap](docs/roadmap.md) und [Changelog](CHANGELOG.md)

Die ausführliche API-Referenz ist auf Englisch verfügbar. Projektübersicht und Einsteigertutorial gibt es auf Deutsch und Englisch.

## Mitmachen und Sicherheit

Melde Fehler und Verbesserungsvorschläge über [GitHub Issues](https://github.com/dermixer1305/TwitchDock.NET/issues). Bitte füge ein kleines reproduzierbares Beispiel hinzu und entferne Zugangsdaten. Wie man aus dem Quellcode baut und testet, steht in [CONTRIBUTING.md](CONTRIBUTING.md).

Sicherheitslücken bitte **privat** über [GitHub melden](https://github.com/dermixer1305/TwitchDock.NET/security/advisories/new). Hinweise zum sicheren Betrieb stehen in [SECURITY.md](SECURITY.md).

## Lizenz

[MIT-Lizenz](LICENSE). Twitch ist eine Marke von Twitch Interactive, Inc. Dieses Projekt ist unabhängig und wird nicht von Twitch unterstützt. Die Nutzungsbedingungen und Berechtigungsvorgaben der Twitch-API gelten weiterhin.
