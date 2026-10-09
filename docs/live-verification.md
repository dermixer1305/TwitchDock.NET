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

**Not yet live verified:** all endpoint groups, privileged/paid/account-restricted operations, authorization-code and implicit flows, complete OIDC login, token-refresh rotation, hourly validation over an extended session, IRC, public HTTPS webhook delivery/retries, conduit shards, WebSocket migration/reconnection and revocation events. Fixture and local CLI coverage do not replace these checks. See the [release checklist](releases.md).

## Deutsch

Am **09.10.2026** wurden echte Twitch-Dienste mit eigenen Zugangsdaten und einem Testkanal geprüft. Die automatischen Live-Tests bestanden jeweils **5 von 5 Prüfungen unter .NET 8 und .NET 10**; kein Test wurde übersprungen.

Erfolgreich waren App-Token-Erzeugung, -Prüfung und -Widerruf, lesende API-Aufrufe, der WebSocket-Verbindungsaufbau, die Freigabe per Gerätecode, Benutzertoken-Prüfung, Profilabfrage, ein echtes EventSub-Chat-Abonnement sowie Empfang und Versand einer Chatnachricht. Twitch bestätigte den Versand mit `IsSent = true`.

Die interaktiven Tests liefen mit einem lokalen Testprogramm vor der Umbenennung in TwitchDock. Sie sind kein Nachweis dafür, dass das später ergänzte Chatbot-Tutorial bereits vollständig live durchlaufen wurde. Der Kalender-Test toleriert einen strukturierten 4xx-Fehler; die Prüfung der OpenID-Schlüssel ersetzt keine vollständige OpenID-Anmeldung.

Noch offen sind insbesondere umfassende Tests aller API-Gruppen und Spezialrechte, weiterer Anmeldeverfahren, Token-Erneuerung, länger laufender Token-Prüfung, IRC, öffentlicher Webhooks, Conduits und Wiederverbindungen. Deshalb wird die Veröffentlichung als **Release Candidate** gekennzeichnet.
