# Tutorial: dein erster Twitch-Bot

[English](tutorial.md) · **Deutsch** · [Projektübersicht](../README.de.md)

In etwa 15 Minuten registrierst du eine Twitch-Anwendung, legst ein eigenes C#-Projekt an, startest einen Chatbot mit Befehlen, ergänzt Alerts für Follows, Subs und Raids und liest Daten aus der Twitch-API. Alles kommt von [nuget.org](https://www.nuget.org/packages/TwitchDock.DependencyInjection); du musst dieses Repository weder herunterladen noch selbst bauen. Verwende für Experimente einen Testkanal.

## 1. Was du brauchst

- Das [.NET SDK](https://dotnet.microsoft.com/download) 8 oder 10. Prüfen kannst du das mit `dotnet --version`.
- Ein Twitch-Konto mit bestätigter E-Mail-Adresse und Zwei-Faktor-Anmeldung (Voraussetzung für die Entwicklerkonsole).
- Einen beliebigen Editor: Visual Studio, Rider, VS Code oder einen einfachen Texteditor.

## 2. Eigene Twitch-Anwendung registrieren

1. Öffne die [Twitch-Entwicklerkonsole](https://dev.twitch.tv/console/apps) und melde dich an.
2. Wähle **Anwendung registrieren**. Verwende einen eigenen, eindeutigen Namen, etwa `MeinKanal Bot` mit einem persönlichen Zusatz.
3. Trage `http://localhost:3000` unter **OAuth Redirect URLs** ein und klicke auf **Hinzufügen**. Wähle eine Kategorie, etwa **Chat Bot**.
4. Wähle den Client-Typ **Vertraulich**. Das Secret brauchst du nur für das API-Beispiel in Schritt 6. Es bleibt auf deinem eigenen Rechner oder Server und gehört nie in eine ausgelieferte Anwendung.
5. Erstelle die Anwendung, öffne **Verwalten** und kopiere die **Client-ID**.

Die Client-ID ist kein Geheimnis. Der Bot meldet sich per Gerätecode an und braucht sonst nichts. Die Weiterleitungsadresse wird dabei nicht verwendet; sie ist für eine spätere Anmeldung per Authorization Code gedacht.

Quellen: [Twitch-App registrieren](https://dev.twitch.tv/docs/authentication/register-app/), [OAuth-Verfahren](https://dev.twitch.tv/docs/authentication/getting-tokens-oauth/).

## 3. Projekt anlegen

```sh
dotnet new console -n MyTwitchBot
cd MyTwitchBot
dotnet add package TwitchDock.DependencyInjection --prerelease
```

`--prerelease` ist nötig, solange 1.0.0 eine Vorabversion ist. Das Paket bringt alle TwitchDock-Module mit; [Welches Paket brauche ich?](../README.de.md#welches-paket-brauche-ich) erklärt, wie du später einzelne Pakete auswählst.

## 4. Dein erster Chatbot

Ersetze den Inhalt von `Program.cs` durch Folgendes und trage deine Client-ID in die Zeile mit `ClientId` ein:

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

Starte ihn:

```sh
dotnet run
```

1. Öffne den Twitch-Link aus dem Terminal und bestätige den angezeigten Code.
2. Melde dich mit dem Konto an, das der Bot nutzen soll, und erlaube **Chatnachrichten lesen und senden**.
3. Warte auf **Bot ist online** und öffne den Kanal-Chat dieses Kontos.
4. Schreibe **`!ping`**, **`!hallo`** oder **`!würfel`**. Der Bot zeigt jede Nachricht an und antwortet auf die Befehle. Ein Konto reicht; ein eigenes Bot-Konto ist optional.
5. Mit **Strg+C** beendest du ihn.

**Was hier passiert:** Die Gerätecode-Anmeldung liefert ein Benutzer-Token. `ValidateAsync` teilt dem SDK mit, zu welchem Benutzer es gehört und welche Rechte es hat. Ein falsches Token scheitert so mit einer klaren Fehlermeldung, bevor etwas gesendet wird. `EventSubWebSocketClient` hält eine WebSocket-Verbindung zu Twitch offen und verbindet sich bei Abbrüchen selbst neu. Bei jeder neuen Sitzung abonniert der Bot `channel.chat.message`, und der Router reicht jede Nachricht als typisiertes `ChannelChatMessageEvent` an deinen Handler weiter. Antworten gehen über die Helix-API raus.

Tokens bleiben im Arbeitsspeicher und werden nie ausgegeben oder gespeichert. Du meldest dich deshalb bei jedem Start neu an. Den Zugriff der App kannst du jederzeit in deinen [Twitch-Verbindungen](https://www.twitch.tv/settings/connections) entfernen.

## 5. Alerts für Follows, Subs und Raids

Ereignisse funktionieren genau wie Chatnachrichten: Recht anfordern, Handler ergänzen, abonnieren. Ersetze `Program.cs` noch einmal; die mit **Neu** markierten Zeilen sind die Änderungen:

```csharp
using TwitchDock.Authentication;
using TwitchDock.Core;
using TwitchDock.EventSub;
using TwitchDock.Helix;
using TwitchDock.Helix.Models;

const string ClientId = "deine-client-id"; // aus dev.twitch.tv/console/apps – kein Geheimnis

using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
var oauth = new TwitchOAuthClient(http);

// Neu: Der Bot muss auch Follower und Abonnenten sehen dürfen.
string[] scopes =
[
    TwitchScopes.UserReadChat, TwitchScopes.UserWriteChat,
    TwitchScopes.ModeratorReadFollowers, TwitchScopes.ChannelReadSubscriptions,
];
var device = await oauth.StartDeviceAuthorizationAsync(ClientId, scopes);
Console.WriteLine($"Öffne {device.VerificationUri} und bestätige den Code {device.UserCode}");
var grant = await oauth.WaitForDeviceAuthorizationAsync(ClientId, device, scopes);

var me = await oauth.ValidateAsync(grant.AccessToken);
var botId = me.UserId!;
var channelId = botId;
var tokens = new StaticAccessTokenProvider(me.ToAccessToken(grant.AccessToken));
var helix = new HelixClient(new TwitchHttpClient(http, tokens, new TwitchHttpOptions { ClientId = ClientId }));

// Neu: sendet eine Chatnachricht in den Kanal; genutzt von Befehlen und Alerts.
Task Say(string message, CancellationToken ct, string? replyTo = null) =>
    helix.SendChatMessageAsync(new SendChatMessageRequest
    {
        BroadcasterId = channelId, SenderId = botId, Message = message, ReplyParentMessageId = replyTo,
    }, ct);

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
        if (reply is not null) await Say(reply, ct, replyTo: chat.MessageId);
    })
    // Neu: Alerts im Chat.
    .On(EventSubEvents.ChannelFollowV2, (follow, _, ct) => Say($"Danke für den Follow, {follow.UserName}!", ct))
    .On(EventSubEvents.ChannelSubscribeV1, (sub, _, ct) => Say($"Willkommen im Team, {sub.UserName}!", ct))
    .On(EventSubEvents.ChannelRaidV1, (raid, _, ct) => Say($"{raid.FromBroadcasterUserName} raidet mit {raid.Viewers} Zuschauern!", ct));

var socket = new EventSubWebSocketClient();
await socket.RunAsync(
    async (session, resubscribe, ct) =>
    {
        if (!resubscribe) return;
        EventSubSubscriptionSpec[] subscriptions =
        [
            EventSubSubscriptions.ChannelChatMessageV1(channelId, botId),
            EventSubSubscriptions.ChannelFollowV2(channelId, moderatorUserId: botId), // Neu
            EventSubSubscriptions.ChannelSubscribeV1(channelId),                    // Neu
            EventSubSubscriptions.ChannelRaidV1(toBroadcasterUserId: channelId),    // Neu
        ];
        foreach (var subscription in subscriptions)
            await helix.SubscribeWebSocketAsync(subscription, session.Id, ct);
        Console.WriteLine("Bot ist online – schreib !ping in deinen Chat.");
    },
    router.DispatchAsync);
```

Starte erneut mit `dotnet run` und erlaube die zusätzlichen Rechte. Folge deinem Kanal mit einem zweiten Konto, um den Alert zu sehen.

- Follows brauchen `moderator:read:followers`. Der Bot liest seinen eigenen Kanal und gilt dort als sein eigener Moderator.
- Abos brauchen `channel:read:subscriptions` und gibt es nur bei Affiliate- oder Partner-Kanälen.
- Raids brauchen kein Recht.
- Fehlt ein Recht, wirft `SubscribeWebSocketAsync` eine `TwitchAuthorizationException` mit dessen Namen, bevor etwas gesendet wird.

Alle 83 EventSub-Typen funktionieren so: Kanalpunkte, Cheers, Umfragen, Hype Trains, Stream-Start und -Ende und mehr. Den Namen findest du auf `EventSubEvents` / `EventSubSubscriptions`, die nötigen Rechte unter [EventSub](eventsub.md).

## 6. Daten aus der Twitch-API lesen

Werkzeuge, die nur öffentliche Daten lesen (Profile, laufende Streams, Clips), kommen mit einem **App-Token** statt einer Benutzeranmeldung aus. Dafür brauchst du das Client-Secret: In der Entwicklerkonsole unter **Verwalten → Neues Geheimnis**. Ein neues Secret macht das vorherige ungültig. Lege ein zweites Projekt an:

```sh
dotnet new console -n MyTwitchApi
cd MyTwitchApi
dotnet add package TwitchDock.DependencyInjection --prerelease
```

`Program.cs`:

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

Gib die Zugangsdaten an Eingabeaufforderungen ein, statt sie in eine Datei oder deinen Befehlsverlauf zu schreiben. In **PowerShell**:

```powershell
$env:TWITCH_CLIENT_ID = Read-Host 'Client-ID'
$secretInput = Read-Host 'Client-Secret' -AsSecureString
$env:TWITCH_CLIENT_SECRET = [System.Net.NetworkCredential]::new('', $secretInput).Password
dotnet run
Remove-Item Env:TWITCH_CLIENT_SECRET
```

Mit **Bash** unter Linux oder macOS:

```bash
read -r -p 'Client-ID: ' TWITCH_CLIENT_ID
read -r -s -p 'Client-Secret: ' TWITCH_CLIENT_SECRET
export TWITCH_CLIENT_ID TWITCH_CLIENT_SECRET
dotnet run
unset TWITCH_CLIENT_SECRET
```

Erwartet: das Profil von `twitchdev` und fünf laufende deutschsprachige Streams. Alle Helix-Gruppen hängen an `helix`: `helix.Channels`, `helix.Moderation`, `helix.Polls`, `helix.Clips`, `helix.ChannelPoints` und mehr; siehe [Dokumentationsindex](README.md).

## 7. Den Bot stundenlang laufen lassen

Twitch verlangt, dass länger laufende Apps Benutzer-Tokens stündlich prüfen, und ein Bot sollte bei Strg+C sauber beenden. Ersetze im Bot aus Schritt 5 alles ab `var socket = new EventSubWebSocketClient();` bis zum Ende durch:

```csharp
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); }; // Neu: Strg+C beendet den Bot sauber

var socket = new EventSubWebSocketClient();
var chatLoop = socket.RunAsync(
    async (session, resubscribe, ct) =>
    {
        if (!resubscribe) return;
        EventSubSubscriptionSpec[] subscriptions =
        [
            EventSubSubscriptions.ChannelChatMessageV1(channelId, botId),
            EventSubSubscriptions.ChannelFollowV2(channelId, moderatorUserId: botId),
            EventSubSubscriptions.ChannelSubscribeV1(channelId),
            EventSubSubscriptions.ChannelRaidV1(toBroadcasterUserId: channelId),
        ];
        foreach (var subscription in subscriptions)
            await helix.SubscribeWebSocketAsync(subscription, session.Id, ct);
        Console.WriteLine("Bot ist online – schreib !ping in deinen Chat.");
    },
    router.DispatchAsync, stop.Token);

// Neu: prüft das Token jetzt und dann stündlich; die Aufgabe schlägt fehl, wenn Twitch es nicht mehr akzeptiert.
var validationLoop = TokenValidationLoop.RunAsync(oauth, tokens, ClientId, (_, _) => Task.CompletedTask, cancellationToken: stop.Token);

await Task.WhenAny(chatLoop, validationLoop); // was zuerst endet, beendet den Bot
await stop.CancelAsync();
try { await Task.WhenAll(chatLoop, validationLoop); }
catch (OperationCanceledException) { Console.WriteLine("Bot beendet."); }
```

Ein Benutzer-Token läuft nach einigen Stunden ab. Damit der Bot ohne neue Anmeldung weiterläuft, nutzt du einen `RefreshingTokenProvider` mit dem Refresh-Token und speicherst erneuerte Tokens sicher; siehe [Token-Provider und Erneuerung](authentication.md#token-providers-and-refresh). Das vollständige [Chatbot-Beispiel](../samples/TwitchDock.ChatBot/Program.cs) meldet außerdem Nachrichten, die Twitch verwirft.

## Häufige Fehler

| Problem | Prüfen |
| --- | --- |
| `Invalid client name` bei der Registrierung | Eigenen, eindeutigen App-Namen wählen. |
| `dotnet add package` findet keine Version | `--prerelease` ergänzen oder `--version 1.0.0-rc.1` angeben. |
| Client-ID fehlt | Client-ID in `Program.cs` eintragen (Bot) bzw. die Variable im selben Terminal setzen, in dem `dotnet run` läuft (API-Beispiel). |
| `401` oder ungültiges Token | Client-ID und Secret prüfen oder neu anmelden. App- und Benutzer-Tokens sind nicht austauschbar. |
| `TwitchAuthorizationException` | Dem Token fehlt ein Recht. In `scopes` ergänzen, neu starten und das neue Recht erlauben. |
| Gerätecode abgelaufen | Programm neu starten und den neuen Link und Code verwenden. |
| Keine Antwort im Chat | Auf **Bot ist online** warten, im Kanal des angemeldeten Kontos schreiben und genau `!ping` senden. |
| Kein Follow-Alert | Mit einem **anderen** Konto folgen; dir selbst kannst du nicht folgen. |
| Fehler beim Zielframework | .NET SDK 8 oder 10 verwenden; die Pakete unterstützen `net8.0` und `net10.0`. |

## Nächste Schritte

- [Welches Paket brauche ich?](../README.de.md#welches-paket-brauche-ich) mit einem Beispiel für jedes Paket.
- [Authentifizierung](authentication.md): Token-Erneuerung, stündliche Prüfung, Authorization Code und OpenID Connect.
- [EventSub](eventsub.md): alle Ereignistypen, Webhooks, Conduits und Wiederverbindungen.
- [Ausführbare Beispiele](samples.md), darunter ein ASP.NET-Core-Webhook-Empfänger, und die [API-Referenzen](README.md).

Speichere Zugangsdaten nicht im Quellcode oder in Logs. Dieses Tutorial ist eine Einführung; produktive Anwendungen brauchen außerdem sichere Token-Speicherung, Fehlerbehandlung und die Maßnahmen aus [SECURITY.md](../SECURITY.md).
