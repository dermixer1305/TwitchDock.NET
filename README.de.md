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

## Pakete

| Paket | Inhalt |
| --- | --- |
| `TwitchDock.DependencyInjection` | `AddTwitchDock`, Token-Prüfung als Hosted Service; bringt alle folgenden Module mit |
| `TwitchDock.Core` | HTTP-Anfragen, Ratenbegrenzung, Wiederholungen, Seitennavigation, Tokens und Rechteprüfung |
| `TwitchDock.Authentication` | OAuth, Gerätecode, OpenID Connect, Token-Erneuerung, -Prüfung und -Widerruf |
| `TwitchDock.Helix` | Twitch REST API: Benutzer, Kanäle, Streams, Chat, Moderation, Umfragen, Kanalpunkte und mehr |
| `TwitchDock.EventSub` | Typisierte Abos und Ereignisse, WebSocket-Client, Webhooks, Conduits und Batching |
| `TwitchDock.Chat` | Chat über EventSub und Helix sowie IRC mit Wiederverbindung und Ratenbegrenzung |

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
