# Users, extension slots and whispers

The full Users group is available through `helix.Users`; `helix.GetUsersAsync` still delegates to that group. The Get Users contract is documented in [foundation endpoints](helix-foundation.md). Whispers are exposed through `helix.Whispers`. All methods accept a final `CancellationToken` and use the shared authorization, retry and error pipeline.

## Profile description

```csharp
var updated = await helix.Users.UpdateUserAsync(new()
{
    Description = "About this channel"
}, cancellationToken);
// An empty value clears the description; null omits the query parameter.
await helix.Users.UpdateUserAsync(new() { Description = "" }, cancellationToken);
```

Requires a user token with `user:edit`; Twitch derives the user from the token. The only documented update field is the optional description, up to 300 characters. It is sent as an encoded query parameter on PUT, with no JSON body. The response is a full `TwitchUser`; email is included only with `user:read:email`. Reading the response does not require that extra scope, and the deprecated `ViewCount` remains invalid.

## Block lists

```csharp
var page = await helix.Users.GetUserBlockListAsync(new()
{
    BroadcasterId = "123", First = 100, After = "cursor"
}, cancellationToken);
await foreach (var blocked in helix.Users.EnumerateUserBlockListAsync(new()
{
    BroadcasterId = "123"
}, cancellationToken))
    Console.WriteLine(blocked.DisplayName);

await helix.Users.BlockUserAsync(new()
{
    TargetUserId = "456", SourceContext = "chat", Reason = "harassment"
}, cancellationToken);
await helix.Users.UnblockUserAsync("456", cancellationToken);
```

Reading requires `user:read:blocked_users` and the broadcaster's own user token. Pages accept `First` 1–100 and `After`, and return blocked user IDs, logins and display names in newest-block-first order. The forward enumerator follows cursors and honors cancellation.

Blocking/unblocking requires `user:manage:blocked_users`; Twitch gets the acting user from the token. Source context is optional (`chat` or `whisper`), as is reason (`harassment`, `spam`, `other`). Blocking oneself is rejected by Twitch. Repeating an already satisfied block/unblock is ignored by Twitch. Both calls send query parameters without a body and complete on HTTP 204. The unblocking implementation follows the documented authorization section's manage scope, not the inconsistent read-scope wording in its error table.

## Installed and active extensions

```csharp
var installed = await helix.Users.GetUserExtensionsAsync(cancellationToken);
foreach (var extension in installed.Data)
    Console.WriteLine($"{extension.Name} {extension.Version}: {extension.CanActivate}");

var ownActive = await helix.Users.GetUserActiveExtensionsAsync(cancellationToken: cancellationToken);
var broadcasterActive = await helix.Users.GetUserActiveExtensionsAsync("123", cancellationToken);
var firstPanel = broadcasterActive.Data.Panel["1"];
```

Installed extensions require a user token with **either** `user:read:broadcast` **or** `user:edit:broadcast`; Twitch includes inactive installed extensions only with the edit scope. Each entry contains ID, version, name, activation eligibility and supported types (`panel`, `overlay`, `component`, `mobile`). There is no pagination.

Active extensions accept app or user tokens. App tokens require an explicit user ID. Omitting it with a user token selects the authenticated user. To include development extensions, Twitch additionally requires a user token with `user:read:broadcast` or `user:edit:broadcast`. No extra scope is required for publicly visible active extensions.

The response's `Data` is an object, not an array. It contains separate panel, overlay and component dictionaries keyed by numbered slots such as `"1"`. Each slot has `Active` and optional ID/version/name; inactive slots can omit all metadata. Component slots additionally have nullable X/Y coordinates. The SDK preserves zero coordinates and inactive slots instead of inventing metadata. Check a slot's existence before indexing it.

## Update extension slots

```csharp
var extensions = await helix.Users.UpdateUserExtensionsAsync(new()
{
    Data = new()
    {
        Panel = new Dictionary<string, UserExtensionActivation>
        {
            ["1"] = new() { Active = true, Id = "panel-extension-id", Version = "1.0.0" }
        },
        Overlay = new Dictionary<string, UserExtensionActivation>
        {
            ["1"] = new() { Active = false }
        },
        Component = new Dictionary<string, UserComponentExtensionActivation>
        {
            ["1"] = new() { Active = true, Id = "component-extension-id", Version = "1.0.0", X = 0, Y = 10 },
            ["2"] = new() { Active = false }
        }
    }
}, cancellationToken);
```

Requires `user:edit:broadcast` on a user token; the token identifies the channel. Request types are separate from responses, so display names cannot accidentally enter a request. Supply only the extension-type dictionaries being updated; null dictionaries are omitted. Numbered slot keys must be positive integers. Activating a slot requires ID/version, plus X/Y for a component. Deactivation can contain just `Active = false`. Twitch verifies installation, configuration, slot availability and extension/version existence. Activating the same extension under multiple types has undefined write order and last-write-wins behavior; configure its intended type explicitly. HTTP 404 means the specified extension/version was not found.

## Whispers

```csharp
await helix.Whispers.SendWhisperAsync(new()
{
    FromUserId = "123", ToUserId = "456", Message = "Hello"
}, cancellationToken);
```

Requires a user token with `user:manage:whispers`, matching `FromUserId`. The sender needs a verified phone number, and sender/recipient must differ. The SDK sends sender/recipient as query parameters and only `message` in the JSON body. Empty messages are rejected locally.

Twitch truncates messages to 500 characters for a recipient who has not previously whispered the sender, or 10000 characters if they have. The SDK does not guess that relationship or truncate the caller's text. HTTP 204 means the request completed; Twitch can silently drop a policy-rejected whisper while returning 204, so completion is not a delivery receipt. Recipient settings, suspension, phone verification and account restrictions remain server decisions.

Twitch's documented limits are 40 unique recipients per day, 3 whispers per second and 100 per minute. The shared transport handles returned 429/Retry-After responses with bounded retries; it does not maintain a durable recipient quota or infer undisclosed anti-abuse decisions. Exhausted retry budgets return `TwitchApiException`. HTTP 400/401/403/404 and other errors preserve Twitch's status and message. Ambiguous mutations are not replayed after a server failure.

`UsersAndWhispersTests` and `helix-users.json` verify all documented response fields, query/body separation, omitted versus empty values, numbered slot maps, inactive slots, alternative scopes, token ownership, pagination and errors. Tests use simulated responses and send no real whispers or account changes. Credentialed integration remains a stable-release requirement.

Sources: pinned official [Update User](https://dev.twitch.tv/docs/api/reference/#update-user), [Block List](https://dev.twitch.tv/docs/api/reference/#get-user-block-list), [Block User](https://dev.twitch.tv/docs/api/reference/#block-user), [Unblock User](https://dev.twitch.tv/docs/api/reference/#unblock-user), [Installed Extensions](https://dev.twitch.tv/docs/api/reference/#get-user-extensions), [Active Extensions](https://dev.twitch.tv/docs/api/reference/#get-user-active-extensions), [Update User Extensions](https://dev.twitch.tv/docs/api/reference/#update-user-extensions), [Send Whisper](https://dev.twitch.tv/docs/api/reference/#send-whisper).
