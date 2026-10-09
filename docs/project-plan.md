# Projektplan – Moderne Twitch SDK für .NET

**Projektname (vorläufig):** TwitchSdk  
**Programmiersprache:** C#  
**Zielplattformen:** .NET 8 und .NET 10  
**Projektart:** Open-Source-Bibliothek / Software Development Kit  
**Veröffentlichung:** GitHub und NuGet  
**Lizenz:** MIT (geplant)

---

## 1. Projektvision

Ziel ist die Entwicklung einer vollständig neuen, modernen und langfristig wartbaren Twitch-SDK für das .NET-Ökosystem.

Die SDK soll Entwicklern ermöglichen, sämtliche offiziell dokumentierten und öffentlich zugänglichen Funktionen der aktuellen Twitch API komfortabel über C# zu verwenden, ohne HTTP-Anfragen, OAuth-Verfahren, EventSub-Verbindungen oder API-Datenmodelle selbst implementieren zu müssen.

Das Projekt soll eine ernstzunehmende Alternative zu bestehenden Bibliotheken wie TwitchLib darstellen.

Es handelt sich ausdrücklich **nicht um einen Fork oder eine Modernisierung von TwitchLib**, sondern um eine unabhängige Neuentwicklung.

## 2. Hauptziele

### 2.1 Vollständige Twitch-API-Unterstützung

- Unterstützung aller aktuellen Helix-REST-API-Endpoints.
- Unterstützung aller offiziell dokumentierten EventSub-Subscription-Typen.
- Unterstützung von EventSub über WebSockets und Webhooks.
- Integration der offiziell unterstützten Twitch-Chat-Funktionen.
- Unterstützung sämtlicher relevanter OAuth-Verfahren und Berechtigungen.
- Vollständige Unterstützung von Pagination, Rate Limits, Fehlerantworten und API-spezifischen Parametern.
- Korrekte Request- und Response-Modelle für jeden unterstützten Endpoint.

**Ziel:** 100 % Abdeckung der dokumentierten, allgemein verfügbaren Twitch-API-Funktionen, soweit sie durch externe Anwendungen zugänglich sind.

Nicht öffentlich verfügbare oder speziell zugangsbeschränkte Funktionen werden gesondert dokumentiert.

### 2.2 Moderne Architektur

Die SDK soll von Grund auf mit modernen .NET-Konzepten entwickelt werden.

Technische Anforderungen:

- Multi-Targeting mit .NET 8 und .NET 10.
- Asynchrone Programmierung mit async/await.
- Dependency Injection und IHttpClientFactory.
- System.Text.Json für JSON-Verarbeitung.
- Nullable Reference Types.
- CancellationToken-Unterstützung.
- Typsichere Request- und Response-Modelle.
- Strukturierte Fehlerbehandlung.
- Automatisches Rate-Limit-Handling.
- Thread-Sicherheit für gemeinsam verwendete Clients.
- Native-AOT-Kompatibilität, soweit sinnvoll.
- Möglichst geringe externe Abhängigkeiten.

### 2.3 Benutzerfreundlichkeit

Entwickler sollen die SDK mit minimalem Konfigurationsaufwand verwenden können.

Einfachheit und eine konsistente API haben hohe Priorität.

Beispielsweise sollen Entwickler:

- Twitch-Benutzer abfragen können.
- Livestreams und Kanalinformationen abrufen können.
- Chat-Nachrichten senden und empfangen können.
- EventSub-Events abonnieren können.
- Twitch-OAuth mit Token-Erneuerung verwenden können.

Komplexe technische Vorgänge wie Token-Verwaltung, WebSocket-Reconnects und HTTP-Fehlerbehandlung sollen möglichst automatisch ablaufen.

## 3. Geplante Module

| Modul | Verantwortlichkeit |
|---|---|
| TwitchSdk.Core | Gemeinsame Infrastruktur, HTTP, Fehler, Rate Limits |
| TwitchSdk.Authentication | OAuth, Scopes und Token-Verwaltung |
| TwitchSdk.Helix | Vollständige Twitch-REST-API |
| TwitchSdk.EventSub | EventSub WebSocket und Webhook |
| TwitchSdk.Chat | Twitch-Chat-Funktionen |
| TwitchSdk.DependencyInjection | Integration in .NET-Anwendungen |

Die Module sollen unabhängig testbar sein und gemeinsame Funktionen nicht mehrfach implementieren.

## 4. Entwicklungsphasen

### Phase 1 – Analyse und Planung

**Ziel:** Den tatsächlichen Funktionsumfang der Twitch API vollständig erfassen.

Aufgaben:

1. Offizielle Twitch-API-Dokumentation auswerten.
2. Sämtliche Helix-Endpoints erfassen.
3. Sämtliche EventSub-Subscription-Typen erfassen.
4. OAuth-Verfahren und Scopes dokumentieren.
5. Twitch-Chat-Schnittstellen analysieren.
6. Öffentliche, eingeschränkte und veraltete Funktionen unterscheiden.
7. Eine maschinenlesbare API-Abdeckungsmatrix erstellen.
8. Namenskonventionen und Architekturregeln festlegen.

**Ergebnis:** Eine nachvollziehbare und überprüfbare technische Spezifikation.

### Phase 2 – Projektgrundlage

**Ziel:** Eine stabile Entwicklungsbasis schaffen.

Aufgaben:

1. GitHub-Repository vorbereiten.
2. .NET-Solution und Modulprojekte erstellen.
3. Gemeinsame Coding-Standards definieren.
4. Build- und Test-Pipelines einrichten.
5. NuGet-Paketierung vorbereiten.
6. CI für .NET 8 und .NET 10 konfigurieren.
7. Versionsstrategie und Release-Prozess festlegen.
8. Dokumentationsstruktur erstellen.

**Ergebnis:** Alle Module sind buildbar, testbar und technisch sauber organisiert.

### Phase 3 – Core und Authentifizierung

**Ziel:** Die technische Grundlage aller weiteren Funktionen implementieren.

Aufgaben:

- HTTP-Pipeline entwickeln.
- OAuth-App- und User-Token-Flows implementieren.
- Automatische Token-Erneuerung entwickeln.
- Scope-Validierung integrieren.
- Rate Limits erkennen und behandeln.
- Fehlerantworten einheitlich modellieren.
- Pagination-Mechanismen bereitstellen.
- Logging und CancellationToken-Unterstützung integrieren.

**Ergebnis:** Eine stabile, wiederverwendbare Infrastruktur.

### Phase 4 – Helix API

**Ziel:** Die vollständige aktuelle Twitch-REST-API implementieren.

Für jeden Endpoint werden folgende Bestandteile entwickelt:

- C#-Methode.
- Request-Modell.
- Response-Modell.
- Parameter- und Scope-Verarbeitung.
- Fehlerbehandlung.
- Automatisierte Tests.
- Dokumentation mit Beispiel.

Die Implementierung erfolgt nach API-Funktionsgruppen, beispielsweise Users, Streams, Channels, Chat, Moderation, Channel Points, Subscriptions, Bits, Clips, Videos, Analytics und Ads.

**Ergebnis:** Vollständige und nachvollziehbare Helix-Unterstützung.

### Phase 5 – EventSub

**Ziel:** Twitch-Ereignisse zuverlässig empfangen und verarbeiten.

Aufgaben:

- EventSub-WebSocket-Client.
- EventSub-Webhook-Receiver.
- Subscription-Verwaltung.
- Modelle für sämtliche dokumentierten Subscription-Typen.
- Automatische WebSocket-Reconnects.
- Behandlung von Session-Wechseln und Keepalives.
- Webhook-Signaturprüfung.
- Deduplizierung und Fehlerbehandlung.
- Tests für Verbindungsabbrüche und ungültige Nachrichten.

**Ergebnis:** Stabile EventSub-Integration für verschiedene Anwendungstypen.

### Phase 6 – Twitch Chat

**Ziel:** Eine moderne und komfortable Chat-Integration anbieten.

Aufgaben:

- Chat-Nachrichten empfangen.
- Chat-Nachrichten senden.
- Chat-Events bereitstellen.
- Nachrichtenmetadaten verarbeiten.
- Moderationsfunktionen integrieren.
- Unterstützte Chat-Transportwege kapseln.
- Verbindungsausfälle behandeln.

Die Architektur berücksichtigt die offiziellen Twitch-Schnittstellen und vermeidet unnötige Abhängigkeiten von veralteten Verfahren.

**Ergebnis:** Eine einfach nutzbare Chat-Bibliothek für Bots und andere Anwendungen.

### Phase 7 – Qualitätssicherung

**Ziel:** Eine zuverlässige, produktionsfähige SDK entwickeln.

Anforderungen:

- Unit-Tests für zentrale Komponenten.
- Contract-Tests für API-Modelle.
- Integrationstests für OAuth und Twitch-Kommunikation.
- Simulation von HTTP-Fehlern.
- Tests für Rate Limits und Token-Ablauf.
- Tests für EventSub-Reconnects.
- Prüfung der Thread-Sicherheit.
- Prüfung auf Breaking Changes.
- API-Abdeckungsbericht.
- Dokumentierte Sicherheitsmaßnahmen.

**Ergebnis:** Eine umfangreich getestete Bibliothek mit nachvollziehbarer Qualität.

### Phase 8 – Dokumentation und Veröffentlichung

**Ziel:** Die SDK für andere Entwickler öffentlich zugänglich machen.

Aufgaben:

- GitHub-README erstellen.
- Quickstart für .NET 8 und .NET 10.
- Beispiele für Authentifizierung.
- Beispiele für Helix und EventSub.
- Beispielprojekte für Twitch-Bots.
- API-Referenz dokumentieren.
- NuGet-Pakete veröffentlichen.
- Semantic Versioning verwenden.
- Changelog pflegen.
- Contribution Guidelines erstellen.

**Ergebnis:** Erste öffentliche stabile Version 1.0.

## 5. Langfristige Wartung

Die Twitch API entwickelt sich kontinuierlich weiter. Die SDK soll daher nicht nach dem ersten Release stehen bleiben.

Geplant sind:

- Automatisierte Überwachung offizieller API-Änderungen.
- Erkennung neu hinzugefügter und entfernter Endpoints.
- Erkennung von Änderungen an Request-/Response-Modellen.
- Automatisierte Hinweise auf fehlende EventSub-Typen.
- Regelmäßige Updates der Abdeckungsmatrix.
- Deprecation-Hinweise bei veralteten Twitch-Funktionen.
- Versionierte Releases und Migration Guides.

Neue API-Funktionen sollen möglichst schnell und ohne größere Architekturänderungen integrierbar sein.

## 6. Definition of Done

Ein Endpoint oder EventSub-Typ gilt erst dann als vollständig implementiert, wenn:

1. Die Funktion über die öffentliche SDK nutzbar ist.
2. Alle dokumentierten Parameter unterstützt werden.
3. Request- und Response-Modelle vollständig sind.
4. Erforderliche Berechtigungen berücksichtigt werden.
5. Fehlerfälle korrekt behandelt werden.
6. Automatisierte Tests existieren und erfolgreich sind.
7. Eine verständliche Dokumentation vorhanden ist.

Für Version 1.0 müssen außerdem alle allgemein verfügbaren Helix-Endpoints und unterstützten EventSub-Typen der festgelegten API-Dokumentationsversion vollständig in der Abdeckungsmatrix aufgeführt und implementiert sein. Dokumentierte Ausnahmen müssen ausdrücklich erkennbar sein.

## 7. Was ausdrücklich vermieden werden soll

- Unnötige Übernahme alter TwitchLib-Architektur.
- Unvollständige API-Implementierungen ohne Kennzeichnung.
- Fehlende oder inkonsistente Datenmodelle.
- Hardcodierte Tokens oder Zugangsdaten.
- Doppelte Logik in unterschiedlichen Modulen.
- Übermäßig große Klassen ohne klare Verantwortlichkeit.
- Breaking Changes ohne Versionierung.
- Funktionen ohne automatisierte Tests.
- Abhängigkeit von einer einzigen Twitch-API-Version ohne Änderungsstrategie.

## 8. Langfristige Vision

Die neue SDK soll zu einer zuverlässigen, umfassenden und modernen C#-Bibliothek für das Twitch-Ökosystem werden.

Entwickler sollen mit ihr einfache Chatbots ebenso wie umfangreiche Streaming-Dashboards, Moderationstools und Twitch-Integrationen erstellen können.

**Oberstes Projektziel:**

Eine vollständige, moderne, gut dokumentierte und langfristig gepflegte Twitch-SDK für .NET 8 und .NET 10, die sich konsequent an der offiziellen Twitch API orientiert und Entwicklern eine einheitliche, zuverlässige Schnittstelle zur Verfügung stellt.