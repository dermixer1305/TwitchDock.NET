using TwitchDock.Core;

namespace TwitchDock.Helix.Clients;

/// <summary>Moderation endpoints. Each area lives in its own ModerationClient.*.cs partial file.</summary>
public sealed partial class ModerationClient(TwitchHttpClient transport)
{
    private readonly TwitchHttpClient _transport = transport ?? throw new ArgumentNullException(nameof(transport));
}
