# Tutorial: your first API call and chat bot

**English** · [Deutsch](tutorial.de.md) · [Project home](../README.md)

This guide uses the `1.0.0-rc.1` release candidate. You will register a Twitch application, run a real API request, authorize your own account and run a chat bot. Use a test channel for experiments.

## 1. Get the project

Install Git and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). .NET 10 is required to build this repository; consumers may use .NET 8 or .NET 10. Install the .NET 8 runtime as well if you want to run its tests.

```sh
git clone https://github.com/dermixer1305/TwitchDock.NET.git
cd TwitchDock.NET
git checkout v1.0.0-rc.1
dotnet build TwitchDock.slnx -c Release
```

Keep the following commands in this repository directory unless a step says otherwise. Rider, Visual Studio and VS Code can all be used to edit the samples.

## 2. Register your Twitch application

1. Open the [Twitch developer console](https://dev.twitch.tv/console/apps) and sign in. Your account needs a verified email address and two-factor authentication.
2. Choose **Register Your Application**. Use your own unique application name, such as `MyChannel Integration Test` with a personal suffix if needed.
3. Add `http://localhost:3000` as an OAuth redirect URL and click **Add**. Select a suitable category, such as **Chat Bot**.
4. For the app-token example below, select the **Confidential** client type. Keep its secret on your own machine/server; do not distribute it with an application.
5. Create the application, then choose **Manage**. Copy the **Client ID**. Use **New Secret** to generate the **Client Secret** for the app-token example.

The device login used by our chat sample needs only the client ID. The redirect URL is not used by that device flow; it is available for a later authorization-code flow. Generating another secret invalidates the previous one.

Sources: [register an app](https://dev.twitch.tv/docs/authentication/register-app/), [OAuth flows](https://dev.twitch.tv/docs/authentication/getting-tokens-oauth/).

## 3. Read real Twitch API data

In **PowerShell**, enter your credentials at the prompts rather than putting them in a script or command history:

```powershell
$env:TWITCH_CLIENT_ID = Read-Host 'Client ID'
$secretInput = Read-Host 'Client secret' -AsSecureString
$env:TWITCH_CLIENT_SECRET = [System.Net.NetworkCredential]::new('', $secretInput).Password
dotnet run --project samples/TwitchDock.Quickstart -c Release -f net10.0 -- twitchdev
Remove-Item Env:TWITCH_CLIENT_SECRET
$secretInput = $null
```

In **Bash**:

```bash
read -r -p 'Client ID: ' TWITCH_CLIENT_ID
read -r -s -p 'Client secret: ' TWITCH_CLIENT_SECRET
export TWITCH_CLIENT_ID TWITCH_CLIENT_SECRET
dotnet run --project samples/TwitchDock.Quickstart -c Release -f net10.0 -- twitchdev
unset TWITCH_CLIENT_SECRET
```

Expected: the terminal prints the ID and display name for `twitchdev`. The sample gets and validates an app token, then calls Helix Get Users. Substitute another channel login after `--` to look up that user. Tokens and secrets are not printed.

## 4. Authorize your account and try chat

Keep `TWITCH_CLIENT_ID` set from the previous step, then run:

```sh
dotnet run --project samples/TwitchDock.ChatBot -c Release -f net10.0
```

1. Open the Twitch URL printed in the terminal. Enter the displayed code if asked.
2. Sign in with the account you want the sample to use. Review and approve **reading and sending chat messages** (`user:read:chat`, `user:write:chat`).
3. Wait until the terminal says **Connected**. Open that account's Twitch channel chat.
4. Type **`!ping`** in the channel. The sample prints the incoming message and sends **`pong`** as a reply. You can do this with the same account; a second bot account is optional.
5. Press **Ctrl+C** to stop the program and close the WebSocket connection.

The sample uses the SDK's device-authorization polling, validates the resulting user token, creates an EventSub WebSocket subscription and sends replies through Helix. Twitch can accept an HTTP request but drop the message; the sample checks `IsSent` and displays any drop reason.

Tokens stay in memory and are not saved. This teaching sample uses a static token and does not refresh it; restart and sign in again after expiry. Stopping the program does not revoke the app authorization. You can remove the app in [Twitch connections](https://www.twitch.tv/settings/connections). For persistent bots, implement refresh and secure persistence as described in [authentication](authentication.md#token-providers-and-refresh).

### Use an existing token or a different channel

The sample also supports these environment variables:

| Variable | Default / requirement |
| --- | --- |
| `TWITCH_CLIENT_ID` | Required: your application's client ID |
| `TWITCH_ACCESS_TOKEN` | Optional: raw user token, without an `oauth:` prefix; otherwise device login starts |
| `TWITCH_BOT_USER_ID` | Optional: defaults to the token's user ID; if supplied it must match |
| `TWITCH_BROADCASTER_ID` | Optional: numeric ID of the channel to join; defaults to the token's user ID |

Remove previously set optional variables to return to the single-account tutorial. For other channels, use the appropriate user consent and any permissions Twitch requires for the chosen operation. An app token cannot replace the user token for this WebSocket chat example.

## 5. Add TwitchDock to your own project

The packages are not on nuget.org yet. From the repository root, build all six packages and register the resulting directory as a local source.

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

Without building: download `TwitchDock.NET-1.0.0-rc.1-packages.zip` from the [GitHub release](https://github.com/dermixer1305/TwitchDock.NET/releases/tag/v1.0.0-rc.1), extract it into a directory and register that directory's absolute path instead. All six packages use the same version. Keep nuget.org enabled for Microsoft dependencies. If the source name already exists, use `dotnet nuget update source twitchdock-local --source <absolute-folder>`.

Create a separate application:

```sh
dotnet new console -n MyFirstBot -o artifacts/MyFirstBot -f net10.0
dotnet add artifacts/MyFirstBot/MyFirstBot.csproj package TwitchDock.DependencyInjection --version 1.0.0-rc.1
```

Replace `artifacts/MyFirstBot/Program.cs` with:

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

Set the environment variables again using step 3's prompts, then run `dotnet run --project artifacts/MyFirstBot -f net10.0`. Remove the secret variable afterwards. The [quickstart](quickstart.md) shows dependency injection, pagination and more operations. A .NET 8 application can use the same packages with a suitable SDK/runtime.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| `Invalid client name` during registration | Choose a unique app name; do not copy another app's name. |
| Missing client ID / secret | Set the variable in the same terminal that starts the program. |
| `401` or invalid token | Check the app credentials or repeat user authorization; app and user tokens serve different operations. |
| Missing scope or wrong user | Authorize the requested permissions and use the correct account. Remove stale optional chat variables. |
| Device code expired | Restart the chat sample and use the new URL/code. |
| No `pong` | Wait for Connected, use the configured channel and send exactly `!ping`. Check for a logged drop reason. |
| Package not found | Configure the local feed, download/build all six packages, and use version `1.0.0-rc.1`. |
| SDK / target framework error | Build with the .NET 10 SDK; install the runtime matching the framework you run. |

## Where to go next

- [Runnable samples](samples.md), including an ASP.NET Core webhook receiver.
- [Authentication](authentication.md): refresh, hourly validation, authorization code and OpenID Connect.
- [EventSub](eventsub.md): subscriptions, routing, public HTTPS callbacks and reconnect behavior.
- [API reference index](README.md), [testing](testing.md) and [live verification limits](live-verification.md).

Keep secrets out of source control and logs. The sample code is an introduction; production applications also need secure token storage, suitable error handling and the operational practices in [SECURITY.md](../SECURITY.md).
