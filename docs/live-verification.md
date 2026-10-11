# Live verification / Live-Prüfung

## English

Performed on **2026-10-09** against the real Twitch services with maintainer-controlled credentials and a test channel. No credentials, access tokens, refresh tokens or raw chat logs are included in this report.

| Check | Result | Scope |
| --- | --- | --- |
| Automated `LiveTwitchTests` | 5 passed on .NET 8; 5 passed on .NET 10; none skipped | Windows; real network requests |
| App-token lifecycle | Passed | Acquire through client credentials, validate, revoke test token |
| Helix reads | Passed | Users, games, search, streams, channels, global emotes and badges, cheermotes, content labels, videos, clips, teams, subscription and conduit lists |
| EventSub WebSocket welcome | Passed | Establish a real session and parse its welcome |
| Public iCalendar / signing keys / invalid-credential errors | Passed within test assertions | The calendar test accepts a structured 4xx response when unavailable; the signing-key test checks retrieval/parsing and rejection of a synthetic invalid ID token, not a complete OIDC login |
| Device authorization and user validation | Passed | User approved read/write chat scopes on Twitch; SDK exchanged the device code and checked token identity and scopes |
| User-token Helix read | Passed | Fetch the authorized user's profile |
| EventSub chat subscription and typed message | Passed | Enabled `channel.chat.message` v1 subscription; received and decoded the user's test message |
| Helix Send Chat Message | Passed | One authorized message sent to the test channel; Twitch returned `IsSent = true` and a message ID |

The interactive checks used a local test harness, not the published ChatBot sample. They ran before the project/namespace rename from `TwitchSdk` to `TwitchDock`. The rename and the sample's new interactive onboarding are covered by subsequent build and automated checks; the earlier live runs do not by themselves verify every change in the release commit.

### 2026-10-11: tutorial bot with saved login (1.0.0-rc.3 from nuget.org)

The program from [tutorial step 7](tutorial.md#7-run-the-bot-permanently), built against the published `TwitchDock.DependencyInjection` 1.0.0-rc.3 package on Windows with .NET 10, run against the maintainer's channel with a **Confidential** app. Only the client ID and secret input differed from the tutorial (read from the environment and a hidden prompt).

| Check | Result |
| --- | --- |
| Device login with `user:read:chat`, `user:write:chat`, `moderator:read:followers`, `channel:read:subscriptions`; `FileTokenStore` saves the login | Passed |
| EventSub WebSocket subscriptions for chat message, follow, subscribe and raid; `!ping` answered with `pong` in chat; Ctrl+C stops cleanly | Passed |
| Restart **without** client secret | Failed as expected for a Confidential app: HTTP 400 `missing_client_secret`. This led to the tutorial fix (secret required, clear message) and to OAuth exception messages naming the reason |
| Restart with client secret: saved refresh token exchanged, rotated refresh token saved, bot online without a new login | Passed |
| Second restart with the rotated refresh token | Passed |

Follow, subscription and raid alerts were subscribed but not triggered. Refresh during a running session (after HTTP 401) and a Public app without secret were not exercised.

### 2026-10-11: IRC and recovery after a network outage (1.0.0-rc.3)

A test program ran EventSub chat (`!ping`) and `TwitchIrcClient` (`!irc`) side by side against the maintainer's channel, with a token holding `user:read:chat`, `user:write:chat`, `chat:read` and `chat:edit`.

| Check | Result |
| --- | --- |
| IRC login, join, typed `ROOMSTATE`, receiving chat, sending a reply through `SendMessageAsync` | Passed |
| Network disconnected for about 30 seconds: IRC detected the loss, retried with backoff (1, 2, 4, 8, 16 s), logged in again and rejoined the channel | Passed |
| Same outage: EventSub opened a new session and re-created its subscription; `!ping` answered afterwards | Passed |
| Session stability without an outage | **Failed**: a notification arriving just after the 10 s keepalive window made the client reconnect, and that notification was lost. Fixed by a 5 s grace period (unreleased) |

IRC also logged a full stack trace for every failed attempt during the outage; it now logs one line per attempt.

**Confirmation run with the fix** (local source, same program, 10 minutes): EventSub stayed on session 1 and IRC on connection 1 throughout. Chat messages after gaps of 14 seconds to over a minute (several keepalive windows) were all delivered and answered. The shorter IRC retry log was not exercised because there was no outage in this run.

### 2026-10-12: webhook delivery (1.0.0-rc.4 from nuget.org)

A receiver like [samples/TwitchDock.WebhookHost](../samples/TwitchDock.WebhookHost/Program.cs), reachable through a Cloudflare quick tunnel, with a `channel.update` v2 webhook subscription created with an app token and a random 64-character secret per run.

| Check | Result |
| --- | --- |
| Subscription created; Twitch's `webhook_callback_verification` answered by `EventSubWebhookHandler` (HTTP 200); status `enabled` within two seconds | Passed |
| Stream title change delivered as a signed notification, verified and routed to the typed `ChannelUpdateEvent` handler (HTTP 204) with correct title and category | Passed |
| Subscription deleted through Helix at the end | Passed |

Retries after failed deliveries, revocation and duplicate redelivery were not exercised.

**Not yet live verified:** all endpoint groups, privileged/paid/account-restricted operations, authorization-code and implicit flows, complete OIDC login, token refresh triggered by HTTP 401 during a running session, hourly validation over an extended session, webhook retries and revocations, conduit shards, Twitch-initiated WebSocket session migration and revocation events. Fixture and local CLI coverage do not replace these checks. See the [release checklist](releases.md).

## Deutsch

Am **09.10.2026** wurden echte Twitch-Dienste mit eigenen Zugangsdaten und einem Testkanal geprüft. Die automatischen Live-Tests bestanden jeweils **5 von 5 Prüfungen unter .NET 8 und .NET 10**; kein Test wurde übersprungen.

Erfolgreich waren App-Token-Erzeugung, -Prüfung und -Widerruf, lesende API-Aufrufe, der WebSocket-Verbindungsaufbau, die Freigabe per Gerätecode, Benutzertoken-Prüfung, Profilabfrage, ein echtes EventSub-Chat-Abonnement sowie Empfang und Versand einer Chatnachricht. Twitch bestätigte den Versand mit `IsSent = true`.

Die interaktiven Tests liefen mit einem lokalen Testprogramm vor der Umbenennung in TwitchDock. Sie sind kein Nachweis dafür, dass das später ergänzte Chatbot-Tutorial bereits vollständig live durchlaufen wurde. Der Kalender-Test toleriert einen strukturierten 4xx-Fehler; die Prüfung der OpenID-Schlüssel ersetzt keine vollständige OpenID-Anmeldung.

Am **11.10.2026** lief zusätzlich das Programm aus [Tutorial-Schritt 7](tutorial.de.md#7-den-bot-dauerhaft-laufen-lassen) mit dem veröffentlichten Paket 1.0.0-rc.3 (Windows, .NET 10, vertrauliche App) gegen den eigenen Kanal: Gerätecode-Anmeldung, Speichern mit `FileTokenStore`, EventSub-Abos für Chat, Follows, Abos und Raids, `!ping` mit Antwort `pong` und sauberes Beenden mit Strg+C waren erfolgreich. Ein Neustart ohne Client-Secret scheiterte wie bei vertraulichen Apps erwartet mit HTTP 400 `missing_client_secret`; daraus entstanden die Korrektur im Tutorial und Fehlermeldungen mit dem Grund. Mit Secret wurde das gespeicherte Refresh-Token erneuert, das neue gespeichert und der Bot ging ohne neue Anmeldung online; auch ein zweiter Neustart mit dem erneuerten Token gelang. Die Alerts selbst wurden nicht ausgelöst.

Am **12.10.2026** wurde außerdem die Webhook-Zustellung live geprüft: Über einen Cloudflare-Tunnel beantwortete `EventSubWebhookHandler` die Prüfanfrage von Twitch, das Abo für `channel.update` wurde sofort aktiv, eine Titeländerung kam als signierte Benachrichtigung mit korrektem Titel und Kategorie an, und das Abo wurde am Ende gelöscht. Wiederholte Zustellungen und Widerrufe wurden nicht geprüft.

Ebenfalls am **11.10.2026** liefen EventSub-Chat und IRC parallel: IRC-Anmeldung, Kanal betreten, Empfangen und Senden funktionierten, und nach einem rund 30-sekündigen Netzausfall verbanden sich beide Wege selbst neu und legten ihre Abos wieder an. Dabei fiel ein Fehler auf: Eine Benachrichtigung, die knapp nach dem 10-Sekunden-Keepalive-Fenster ankam, ließ den Client neu verbinden, und sie ging verloren. Seit 1.0.0-rc.4 gibt es 5 Sekunden Puffer; ein 10-minütiger Lauf damit blieb durchgehend stabil.

Noch offen sind insbesondere umfassende Tests aller API-Gruppen und Spezialrechte, weiterer Anmeldeverfahren, Token-Erneuerung nach HTTP 401 während des Betriebs, länger laufender Token-Prüfung, wiederholter Webhook-Zustellungen und Widerrufe, Conduits sowie von Twitch angestoßener Session-Wechsel. Deshalb wird die Veröffentlichung als **Release Candidate** gekennzeichnet.
