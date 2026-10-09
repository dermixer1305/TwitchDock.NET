# Tutorial: erster API-Aufruf und Chatbot

[English](tutorial.md) · **Deutsch** · [Projektübersicht](../README.de.md)

Diese Anleitung verwendet die Vorabversion `1.0.0-rc.1`. Du registrierst eine Twitch-Anwendung, liest echte API-Daten, meldest dein Konto an und startest einen Chatbot. Verwende für Experimente einen Testkanal.

## 1. Projekt herunterladen

Installiere Git und das [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Zum Bauen dieses Projekts ist .NET 10 erforderlich. Die Bibliotheken können in .NET-8- und .NET-10-Anwendungen verwendet werden. Für Tests unter .NET 8 brauchst du zusätzlich dessen Runtime.

```sh
git clone https://github.com/dermixer1305/TwitchDock.NET.git
cd TwitchDock.NET
git checkout v1.0.0-rc.1
dotnet build TwitchDock.slnx -c Release
```

Führe die folgenden Befehle in diesem Projektordner aus, sofern nichts anderes angegeben ist. Du kannst die Beispiele mit Rider, Visual Studio oder VS Code bearbeiten.

## 2. Eigene Twitch-Anwendung registrieren

1. Öffne die [Twitch-Entwicklerkonsole](https://dev.twitch.tv/console/apps) und melde dich an. Dein Konto benötigt eine bestätigte E-Mail-Adresse und Zwei-Faktor-Anmeldung.
2. Wähle **Anwendung registrieren**. Verwende einen eigenen eindeutigen Namen, beispielsweise `MeinKanal Integrationstest` mit einem persönlichen Zusatz.
3. Trage `http://localhost:3000` unter **OAuth Redirect URLs** ein und klicke auf **Hinzufügen**. Wähle eine passende Kategorie, etwa **Chat Bot**.
4. Wähle für das folgende Beispiel mit App-Token den Client-Typ **Vertraulich**. Das Secret bleibt auf deinem eigenen Rechner/Server und gehört nicht in eine ausgelieferte Anwendung.
5. Erstelle die Anwendung und öffne **Verwalten**. Kopiere die **Client-ID** und erzeuge über **Neues Geheimnis** das **Client-Secret** für das API-Beispiel.

Das Chatbeispiel verwendet eine Geräteanmeldung und benötigt nur die Client-ID. Die Weiterleitungsadresse wird dabei nicht verwendet; sie steht für einen späteren Authorization-Code-Ablauf bereit. Wenn du ein neues Secret erzeugst, wird das vorherige ungültig.

Quellen: [Twitch-App registrieren](https://dev.twitch.tv/docs/authentication/register-app/), [OAuth-Verfahren](https://dev.twitch.tv/docs/authentication/getting-tokens-oauth/).

## 3. Echte Twitch-Daten abrufen

Gib in **PowerShell** die Werte an den Eingabeaufforderungen ein. Dadurch steht das Secret nicht als Klartext in einem gespeicherten Befehl:

```powershell
$env:TWITCH_CLIENT_ID = Read-Host 'Client-ID'
$secretInput = Read-Host 'Client-Secret' -AsSecureString
$env:TWITCH_CLIENT_SECRET = [System.Net.NetworkCredential]::new('', $secretInput).Password
dotnet run --project samples/TwitchDock.Quickstart -c Release -f net10.0 -- twitchdev
Remove-Item Env:TWITCH_CLIENT_SECRET
$secretInput = $null
```

Mit **Bash** unter Linux oder macOS:

```bash
read -r -p 'Client-ID: ' TWITCH_CLIENT_ID
read -r -s -p 'Client-Secret: ' TWITCH_CLIENT_SECRET
export TWITCH_CLIENT_ID TWITCH_CLIENT_SECRET
dotnet run --project samples/TwitchDock.Quickstart -c Release -f net10.0 -- twitchdev
unset TWITCH_CLIENT_SECRET
```

Das Programm sollte die Benutzer-ID und den Anzeigenamen von `twitchdev` ausgeben. Es holt und prüft einen App-Token und ruft anschließend Helix Get Users auf. Ersetze den Namen hinter `--`, um einen anderen Benutzer abzufragen. Tokens und Secrets werden nicht ausgegeben.

## 4. Konto freigeben und Chat testen

Lasse `TWITCH_CLIENT_ID` aus dem vorherigen Schritt gesetzt und starte:

```sh
dotnet run --project samples/TwitchDock.ChatBot -c Release -f net10.0
```

1. Öffne den Twitch-Link aus dem Terminal und trage bei Bedarf den angezeigten Code ein.
2. Melde dich mit dem gewünschten Konto an. Prüfe und bestätige die Berechtigungen **Chatnachrichten lesen und senden** (`user:read:chat`, `user:write:chat`).
3. Warte auf die Ausgabe **Connected**. Öffne den Twitch-Chat des angemeldeten Kontos.
4. Schreibe **`!ping`**. Das Beispiel zeigt die empfangene Nachricht an und antwortet mit **`pong`**. Ein zweites Bot-Konto ist für diesen Test nicht erforderlich.
5. Beende das Beispiel mit **Strg+C**. Die WebSocket-Verbindung wird geschlossen.

Das Beispiel wartet mit dem SDK auf deine Freigabe, prüft den Benutzertoken, richtet ein EventSub-WebSocket-Abonnement ein und sendet Antworten über Helix. Twitch kann eine HTTP-Anfrage erfolgreich beantworten und die Chatnachricht trotzdem verwerfen; deshalb prüft das Beispiel `IsSent` und zeigt gegebenenfalls den Ablehnungsgrund an.

Tokens bleiben im Arbeitsspeicher und werden nicht gespeichert. Das Lernbeispiel verwendet einen festen Token und erneuert ihn nicht automatisch. Nach Ablauf startest du es neu und meldest dich erneut an. Das Beenden widerruft nicht die App-Freigabe; diese kannst du in deinen [Twitch-Verbindungen](https://www.twitch.tv/settings/connections) entfernen. Für dauerhafte Bots benötigst du Token-Erneuerung und sichere Speicherung, siehe [Authentifizierung](authentication.md#token-providers-and-refresh).

### Vorhandenen Token oder anderen Kanal verwenden

| Umgebungsvariable | Bedeutung |
| --- | --- |
| `TWITCH_CLIENT_ID` | Pflicht: Client-ID deiner Anwendung |
| `TWITCH_ACCESS_TOKEN` | Optional: Benutzertoken ohne `oauth:` davor; sonst startet die Geräteanmeldung |
| `TWITCH_BOT_USER_ID` | Optional: muss zum Token gehören; standardmäßig dessen Benutzer-ID |
| `TWITCH_BROADCASTER_ID` | Optional: numerische ID des Zielkanals; standardmäßig dein eigener Kanal |

Entferne alte optionale Variablen, wenn du wieder dem Test mit nur einem Konto folgen möchtest. Für andere Kanäle benötigst du die passende Benutzerfreigabe und die von Twitch für die jeweilige Aktion geforderten Rechte. Ein App-Token ersetzt den Benutzertoken für dieses WebSocket-Chatbeispiel nicht.

## 5. In dein eigenes Projekt einbauen

Die Pakete liegen noch nicht auf nuget.org. Erzeuge im Repository-Ordner alle sechs Pakete und registriere den Ausgabeordner als lokale Quelle.

**PowerShell:**

```powershell
dotnet pack TwitchDock.slnx -c Release -o artifacts/packages
dotnet nuget add source "$((Get-Location).Path)/artifacts/packages" --name twitchdock-local
```

**Bash:**

```bash
dotnet pack TwitchDock.slnx -c Release -o artifacts/packages
dotnet nuget add source "$PWD/artifacts/packages" --name twitchdock-local
```

Alternativ lädst du alle sechs `.nupkg`-Dateien aus dem [GitHub-Release](https://github.com/dermixer1305/TwitchDock.NET/releases/tag/v1.0.0-rc.1) in einen Ordner und registrierst dessen absoluten Pfad. Alle Pakete haben dieselbe Version. Lasse nuget.org für die Microsoft-Abhängigkeiten aktiviert. Existiert der Quellenname bereits, verwende `dotnet nuget update source twitchdock-local --source <absoluter-ordner>`.

Erstelle eine eigene Anwendung:

```sh
dotnet new console -n MyFirstBot -o artifacts/MyFirstBot -f net10.0
dotnet add artifacts/MyFirstBot/MyFirstBot.csproj package TwitchDock.DependencyInjection --version 1.0.0-rc.1
```

Ersetze `artifacts/MyFirstBot/Program.cs` durch:

```csharp
using TwitchDock.Authentication;
using TwitchDock.Core;
using TwitchDock.Helix;

var clientId = Environment.GetEnvironmentVariable("TWITCH_CLIENT_ID")
    ?? throw new InvalidOperationException("Set TWITCH_CLIENT_ID.");
var clientSecret = Environment.GetEnvironmentVariable("TWITCH_CLIENT_SECRET")
    ?? throw new InvalidOperationException("Set TWITCH_CLIENT_SECRET.");

using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
var oauth = new TwitchOAuthClient(http);
using var tokens = new RefreshingTokenProvider((_, ct) => oauth.GetAppTokenAsync(clientId, clientSecret, ct));
var helix = new HelixClient(new TwitchHttpClient(http, tokens, new TwitchHttpOptions { ClientId = clientId }));
var users = await helix.GetUsersAsync(new() { Logins = ["twitchdev"] });
Console.WriteLine(users.Data[0].DisplayName);
```

Setze die Umgebungsvariablen über die Eingabeaufforderungen aus Schritt 3 erneut. Starte dann `dotnet run --project artifacts/MyFirstBot -f net10.0` und entferne anschließend die Secret-Variable. Weitere Beispiele für Dependency Injection, Seitennavigation und API-Aufrufe stehen im [Schnellstart](quickstart.md). Mit passendem SDK und Runtime kannst du die Pakete auch in einer .NET-8-Anwendung nutzen.

## Häufige Fehler

| Problem | Prüfen |
| --- | --- |
| `Invalid client name` bei der Registrierung | Eigenen eindeutigen App-Namen wählen. |
| Client-ID oder Secret fehlt | Variable in demselben Terminal setzen, in dem das Programm startet. |
| `401` oder ungültiger Token | App-Zugangsdaten prüfen bzw. Benutzerfreigabe wiederholen; App- und Benutzertokens sind nicht austauschbar. |
| Fehlende Berechtigung / falscher Benutzer | Passendes Konto anmelden, Rechte freigeben und alte optionale Chatvariablen entfernen. |
| Gerätecode abgelaufen | Chatbeispiel neu starten und den neuen Link/Code verwenden. |
| Keine Antwort auf `!ping` | Auf Connected warten, richtigen Kanal öffnen und genau `!ping` senden; Ablehnungsgründe im Terminal prüfen. |
| Paket nicht gefunden | Lokale Quelle einrichten, alle sechs Pakete herunterladen/bauen und Version `1.0.0-rc.1` verwenden. |
| Falsches SDK oder Framework | Mit .NET 10 SDK bauen und passende Runtime installieren. |

## Nächste Schritte

- [Weitere Beispiele](samples.md), darunter ein ASP.NET-Core-Webhook-Empfänger.
- [Authentifizierung](authentication.md): Erneuerung, stündliche Prüfung, Authorization Code und OpenID Connect.
- [EventSub](eventsub.md): Ereignisse, öffentliche HTTPS-Endpunkte und Wiederverbindungen.
- [API-Referenzen](README.md), [Tests](testing.md) und [Grenzen der Live-Prüfung](live-verification.md).

Speichere Zugangsdaten nicht im Quellcode oder in Logs. Für produktive Anwendungen brauchst du außerdem sichere Token-Speicherung, geeignete Fehlerbehandlung und die in [SECURITY.md](../SECURITY.md) beschriebenen Betriebsmaßnahmen.
