namespace TwitchSdk.Helix.Models;

public sealed class UpdateUserRequest
{
    public string? Description { get; init; }
}

public sealed record GetUserBlockListRequest
{
    public required string BroadcasterId { get; init; }
    public int? First { get; init; }
    public string? After { get; init; }
}

public sealed class BlockedUser
{
    public required string UserId { get; init; }
    public required string UserLogin { get; init; }
    public required string DisplayName { get; init; }
}

public sealed class BlockUserRequest
{
    public required string TargetUserId { get; init; }
    public string? SourceContext { get; init; }
    public string? Reason { get; init; }
}

public sealed class InstalledUserExtension
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string Name { get; init; }
    public bool CanActivate { get; init; }
    public IReadOnlyList<string> Type { get; init; } = [];
}

public sealed class UserActiveExtensionsResponse
{
    public UserActiveExtensions Data { get; init; } = new();
}

public sealed class UserActiveExtensions
{
    public IReadOnlyDictionary<string, ActiveUserExtension> Panel { get; init; } = new Dictionary<string, ActiveUserExtension>();
    public IReadOnlyDictionary<string, ActiveUserExtension> Overlay { get; init; } = new Dictionary<string, ActiveUserExtension>();
    public IReadOnlyDictionary<string, ActiveUserComponentExtension> Component { get; init; } = new Dictionary<string, ActiveUserComponentExtension>();
}

public class ActiveUserExtension
{
    public bool Active { get; init; }
    public string? Id { get; init; }
    public string? Version { get; init; }
    public string? Name { get; init; }
}

public sealed class ActiveUserComponentExtension : ActiveUserExtension
{
    public int? X { get; init; }
    public int? Y { get; init; }
}

public sealed class UpdateUserExtensionsRequest
{
    public required UserExtensionUpdates Data { get; init; }
}

public sealed class UserExtensionUpdates
{
    public IReadOnlyDictionary<string, UserExtensionActivation>? Panel { get; init; }
    public IReadOnlyDictionary<string, UserExtensionActivation>? Overlay { get; init; }
    public IReadOnlyDictionary<string, UserComponentExtensionActivation>? Component { get; init; }
}

public class UserExtensionActivation
{
    public required bool Active { get; init; }
    public string? Id { get; init; }
    public string? Version { get; init; }
}

public sealed class UserComponentExtensionActivation : UserExtensionActivation
{
    public int? X { get; init; }
    public int? Y { get; init; }
}

public sealed record GetUsersRequest
{
    public IReadOnlyList<string> Ids { get; init; } = [];
    public IReadOnlyList<string> Logins { get; init; } = [];
}

public sealed class TwitchUser
{
    public required string Id { get; init; }
    public required string Login { get; init; }
    public required string DisplayName { get; init; }
    public string Type { get; init; } = "";
    public string BroadcasterType { get; init; } = "";
    public string Description { get; init; } = "";
    public string ProfileImageUrl { get; init; } = "";
    public string OfflineImageUrl { get; init; } = "";
    /// <summary>Deprecated by Twitch; its value is not valid and must not be used.</summary>
    public long ViewCount { get; init; }
    public string? Email { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
