using TwitchSdk.Core;

namespace TwitchSdk.Helix.Clients;

/// <summary>Chat endpoints. Each area lives in its own ChatClient.*.cs partial file.</summary>
public sealed partial class ChatClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));
}
