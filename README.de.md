# TwitchDock.NET

**Ein unabhängiges .NET-SDK für die Twitch-API, EventSub und Chat.**

[English](README.md) · **Deutsch** · [Tutorial](docs/tutorial.de.md) · [Dokumentation](docs/README.md) · [Downloads](https://github.com/dermixer1305/TwitchDock.NET/releases)

[![Build and test](https://github.com/dermixer1305/TwitchDock.NET/actions/workflows/ci.yml/badge.svg)](https://github.com/dermixer1305/TwitchDock.NET/actions/workflows/ci.yml)

Entwickle Chatbots, Kanalwerkzeuge und Stream-Integrationen in C#. TwitchDock bietet typisierte Anfragen und Ereignisse, asynchrone Methoden und Unterstützung für **.NET 8 und .NET 10**, Trimming und Native AOT.

Das Projekt wurde eigenständig neu entwickelt. Es ist weder ein offizielles Twitch-Produkt noch ein Fork oder Nachfolger von TwitchLib. Es besteht keine Verbindung zu Twitch und keine Unterstützung durch Twitch.

## Stand der Veröffentlichung

**1.0.0-rc.1 ist eine Vorabversion, noch keine stabile Version 1.0.**

- **API-Abdeckung:** 149 Helix-Endpunkte und 83 EventSub-Kombinationen aus Typ und Version gemäß dem Dokumentationsstand vom 09.10.2026. Typisierte Modelle, Berechtigungsprüfungen, Tests und Dokumentation sind vorhanden. Beta- und veraltete APIs sind gekennzeichnet. [Abdeckungsbericht](docs/coverage.md)
- **Automatische Prüfungen:** 878 Unit- und Vertragstests pro Zielplattform, Integrationstests mit der Twitch CLI, Prüfungen der öffentlichen API sowie Paket- und Native-AOT-Tests.
- **Live getestet:** App-Token abrufen, prüfen und widerrufen; echte API-Daten lesen; Benutzeranmeldung per Gerätecode; EventSub-Chat abonnieren; eine echte Nachricht empfangen und eine Nachricht erfolgreich senden. [Testbericht](docs/live-verification.md)
- **Noch offen:** unter anderem umfassende Tests von IRC, öffentlichen Webhooks, Wiederverbindungen, Token-Erneuerung und APIs mit besonderen Zugriffsrechten.

Die Pakete sind **noch nicht auf nuget.org veröffentlicht**. Nutze die GitHub-Release-Pakete oder einen lokalen Build.

## Pakete

| Paket | Inhalt |
| --- | --- |
| `TwitchDock.Core` | HTTP-Anfragen, Ratenbegrenzung, Wiederholungen, Seitennavigation und Berechtigungsprüfung |
| `TwitchDock.Authentication` | OAuth, Gerätecode, OpenID Connect, Token-Erneuerung und -Prüfung |
| `TwitchDock.Helix` | Twitch REST API: Benutzer, Kanäle, Streams, Chat, Moderation, Umfragen, Kanalpunkte und mehr |
| `TwitchDock.EventSub` | Ereignisse über WebSocket oder Webhooks, Signaturprüfung und Ereignisverteilung |
| `TwitchDock.Chat` | Chat über EventSub und Helix sowie ein IRC-Client |
| `TwitchDock.DependencyInjection` | Einbindung mit `AddTwitchDock`; bringt alle Module mit |

## Direkt ausprobieren

Zum Bauen brauchst du das **.NET 10 SDK**. Die Bibliotheken funktionieren auch in Anwendungen mit .NET 8.

```sh
git clone https://github.com/dermixer1305/TwitchDock.NET.git
cd TwitchDock.NET
dotnet build TwitchDock.slnx -c Release
```

Registriere eine eigene Anwendung in der [Twitch-Entwicklerkonsole](https://dev.twitch.tv/console/apps). Starte anschließend in **PowerShell**:

```powershell
$env:TWITCH_CLIENT_ID = Read-Host 'Client-ID deiner Twitch-Anwendung'
dotnet run --project samples/TwitchDock.ChatBot -c Release -f net10.0
```

Öffne den ausgegebenen Twitch-Link und erlaube das Lesen und Senden von Chatnachrichten. Standardmäßig verbindet sich das Beispiel mit **deinem eigenen Kanal**. Schreibe dort `!ping`: Das Beispiel antwortet mit `pong`. Mit Strg+C beendest du es. Für diese Anmeldung brauchst du kein Client-Secret; Zugriffstokens werden weder ausgegeben noch gespeichert.

Das **[deutsche Tutorial](docs/tutorial.de.md)** erklärt Registrierung, ersten API-Aufruf, Chatbot, Installation in einem eigenen Projekt und häufige Fehler. Es enthält auch Befehle für Bash unter Linux und macOS.

## In einem eigenen Projekt verwenden

Lade alle sechs `.nupkg`-Dateien aus dem [Release](https://github.com/dermixer1305/TwitchDock.NET/releases/tag/v1.0.0-rc.1) in einen lokalen Paketordner. Alternativ:

```sh
dotnet pack TwitchDock.slnx -c Release -o artifacts/packages
```

Binde den Ordner als lokale NuGet-Quelle ein und lasse nuget.org für Microsoft-Abhängigkeiten aktiviert. Die vollständigen Befehle stehen im Tutorial. Danach im eigenen Projekt:

```sh
dotnet add package TwitchDock.DependencyInjection --version 1.0.0-rc.1
```

Namensräume und Paketnamen beginnen mit `TwitchDock`. Frühere unveröffentlichte Entwicklungsstände verwendeten `TwitchSdk`.

## Weitere Dokumentation

- [Tutorial auf Deutsch](docs/tutorial.de.md) und [English tutorial](docs/tutorial.md)
- [C#-Schnellstart](docs/quickstart.md), [Anmeldung und Tokens](docs/authentication.md), [EventSub](docs/eventsub.md)
- [Ausführbare Beispiele](docs/samples.md): API-Abfrage, Chatbot und Webhook-Empfänger
- [Dokumentationsindex](docs/README.md), [Roadmap](docs/roadmap.md) und [Changelog](CHANGELOG.md)

Die ausführliche API-Referenz ist auf Englisch verfügbar. Projektübersicht und Einsteigertutorial gibt es auf Deutsch und Englisch.

## Mitmachen und Sicherheit

Melde Fehler und Verbesserungsvorschläge über [GitHub Issues](https://github.com/dermixer1305/TwitchDock.NET/issues). Bitte füge ein kleines reproduzierbares Beispiel hinzu und entferne Zugangsdaten. Für Beiträge siehe [CONTRIBUTING.md](CONTRIBUTING.md).

Sicherheitslücken bitte **privat** über [GitHub melden](https://github.com/dermixer1305/TwitchDock.NET/security/advisories/new). Hinweise zum sicheren Betrieb stehen in [SECURITY.md](SECURITY.md).

## Lizenz

[MIT-Lizenz](LICENSE). Twitch ist eine Marke von Twitch Interactive, Inc. Dieses Projekt ist unabhängig und wird nicht von Twitch unterstützt. Die Nutzungsbedingungen und Berechtigungsvorgaben der Twitch-API gelten weiterhin.
